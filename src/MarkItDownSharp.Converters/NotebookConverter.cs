using System.Text;
using System.Text.Json;
using MarkItDownSharp.Converters.Internal;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Converters;

public sealed class NotebookConverter : IMarkItDownConverter, IExtensionProvider
{
    public int Priority => 0;
    public string Name => "Jupyter notebook";
    public string Description => "IPYNB notebooks: Markdown cells, fenced code cells and text outputs.";
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".ipynb" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        var json = await TextDecoding.ReadAllTextAsync(stream, cancellationToken);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("cells", out var cells) || cells.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The file is not a Jupyter notebook: it has no 'cells' array.");
        }

        var language = ReadLanguage(root);
        var output = new StringBuilder();
        string? title = null;

        foreach (var cell in cells.EnumerateArray())
        {
            var type = cell.TryGetProperty("cell_type", out var typeElement) ? typeElement.GetString() : null;
            var source = ReadText(cell, "source");
            switch (type)
            {
                case "markdown":
                    title ??= FirstHeading(source);
                    output.Append(source.TrimEnd()).Append("\n\n");
                    break;
                case "code":
                    if (!string.IsNullOrWhiteSpace(source))
                    {
                        AppendFence(output, language, source);
                    }

                    AppendOutputs(output, cell);
                    break;
                case "raw":
                    if (!string.IsNullOrWhiteSpace(source))
                    {
                        AppendFence(output, string.Empty, source);
                    }

                    break;
            }
        }

        return new ConversionResult(
            output.ToString().Trim(),
            title ?? Path.GetFileNameWithoutExtension(metadata.FileName));
    }

    private static string ReadLanguage(JsonElement root)
    {
        if (root.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
        {
            if (metadata.TryGetProperty("language_info", out var info) && info.TryGetProperty("name", out var name) && name.GetString() is { } value)
            {
                return value;
            }

            if (metadata.TryGetProperty("kernelspec", out var kernel) && kernel.TryGetProperty("language", out var kernelLanguage) && kernelLanguage.GetString() is { } kernelValue)
            {
                return kernelValue;
            }
        }

        return "python";
    }

    private static void AppendOutputs(StringBuilder output, JsonElement cell)
    {
        if (!cell.TryGetProperty("outputs", out var outputs) || outputs.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in outputs.EnumerateArray())
        {
            var text = item.TryGetProperty("output_type", out var kind) ? kind.GetString() switch
            {
                "stream" => ReadText(item, "text"),
                "execute_result" or "display_data" when item.TryGetProperty("data", out var data) => ReadText(data, "text/plain"),
                "error" => $"{ReadScalar(item, "ename")}: {ReadScalar(item, "evalue")}",
                _ => string.Empty
            } : string.Empty;

            if (!string.IsNullOrWhiteSpace(text))
            {
                output.Append("Output:\n\n");
                AppendFence(output, string.Empty, text);
            }
        }
    }

    private static void AppendFence(StringBuilder output, string language, string text)
    {
        var fence = new string('`', Math.Max(3, LongestBacktickRun(text) + 1));
        output.Append(fence).Append(language).Append('\n').Append(text.TrimEnd()).Append('\n').Append(fence).Append("\n\n");
    }

    // Notebook text fields are either one string or an array of lines.
    private static string ReadText(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => TextDecoding.NormalizeNewlines(value.GetString() ?? string.Empty),
            JsonValueKind.Array => TextDecoding.NormalizeNewlines(string.Concat(value.EnumerateArray().Select(x => x.GetString()))),
            _ => string.Empty
        };
    }

    private static string ReadScalar(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static string? FirstHeading(string markdown)
    {
        foreach (var line in markdown.Split('\n'))
        {
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                return line[2..].Trim();
            }
        }

        return null;
    }

    private static int LongestBacktickRun(string text)
    {
        var longest = 0;
        var current = 0;
        foreach (var c in text)
        {
            current = c == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }
}

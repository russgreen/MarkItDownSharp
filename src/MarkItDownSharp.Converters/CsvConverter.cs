using System.Text;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Converters;

public sealed class CsvConverter : IMarkItDownConverter, IExtensionProvider
{
    public int Priority => 0;
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".csv", ".tsv" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var separator = metadata.Extension?.Equals(".tsv", StringComparison.OrdinalIgnoreCase) == true ? '\t' : ',';
        var output = new StringBuilder();
        var isFirst = true;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            var cells = ParseRow(line, separator);
            output.Append("| ").AppendJoin(" | ", cells.Select(Escape)).AppendLine(" |");
            if (isFirst)
            {
                output.Append("| ").AppendJoin(" | ", cells.Select(_ => "---")).AppendLine(" |");
                isFirst = false;
            }
        }

        return new ConversionResult(output.ToString().TrimEnd(), Path.GetFileNameWithoutExtension(metadata.FileName));
    }

    private static string[] ParseRow(string line, char separator)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == separator && !inQuotes)
            {
                cells.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        cells.Add(current.ToString());
        return cells.ToArray();
    }

    private static string Escape(string value) =>
        value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Trim();
}

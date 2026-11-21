using System.Text;
using System.Xml.Linq;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Converters;

public sealed class StructuredTextConverter : IMarkItDownConverter, IExtensionProvider
{
    public int Priority => 0;
    public IReadOnlyCollection<string> Extensions { get; } =
        new[] { ".json", ".xml", ".yaml", ".yml" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        var language = metadata.Extension?.TrimStart('.').ToLowerInvariant();
        if (language is "xml")
        {
            text = XDocument.Parse(text).ToString(SaveOptions.None);
            language = "xml";
        }
        else if (language is "yml")
        {
            language = "yaml";
        }

        var markdown = $"```{language}\n{text}\n```";
        return new ConversionResult(markdown, Path.GetFileNameWithoutExtension(metadata.FileName));
    }
}

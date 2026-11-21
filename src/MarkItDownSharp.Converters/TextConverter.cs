using MarkItDownSharp.Converters.Internal;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Converters;

public sealed class TextConverter : IMarkItDownConverter, IExtensionProvider
{
    public int Priority => 10;
    public string Name => "Text";
    public string Description => "Plain text and Markdown. Detects the file encoding and normalises line endings.";
    public IReadOnlyCollection<string> Extensions { get; } =
        new[] { ".txt", ".md", ".log" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        var text = await TextDecoding.ReadAllTextAsync(stream, cancellationToken);
        return new ConversionResult(text, Path.GetFileNameWithoutExtension(metadata.FileName));
    }
}

using System.Text;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Converters;

public sealed class TextConverter : IMarkItDownConverter, IExtensionProvider
{
    public int Priority => 10;
    public IReadOnlyCollection<string> Extensions { get; } =
        new[] { ".txt", ".md", ".log" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var text = await reader.ReadToEndAsync(cancellationToken);
        return new ConversionResult(text, Path.GetFileNameWithoutExtension(metadata.FileName));
    }
}

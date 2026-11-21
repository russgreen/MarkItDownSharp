namespace MarkItDownSharp.Core;

public interface IMarkItDownConverter
{
    int Priority { get; }
    bool CanConvert(StreamMetadata metadata);
    Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default);
}

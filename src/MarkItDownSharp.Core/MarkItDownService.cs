namespace MarkItDownSharp.Core;

public sealed class MarkItDownService
{
    private readonly IReadOnlyList<IMarkItDownConverter> _converters;

    public MarkItDownService(IEnumerable<IMarkItDownConverter> converters)
    {
        _converters = converters.OrderBy(x => x.Priority).ToArray();
    }

    public IReadOnlyCollection<string> SupportedExtensions =>
        _converters
            .SelectMany(x => x is IExtensionProvider provider ? provider.Extensions : Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public async Task<ConversionResult> ConvertFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var metadata = InferMetadata(path);
        var converter = FindConverter(metadata)
            ?? throw new NotSupportedException($"No converter is registered for '{metadata.Extension ?? "unknown"}' files.");
        return await converter.ConvertAsync(stream, metadata, cancellationToken);
    }

    public async Task<ConversionResult> ConvertStreamAsync(Stream stream, string fileName, CancellationToken cancellationToken = default)
    {
        var metadata = InferMetadata(fileName);
        var converter = FindConverter(metadata)
            ?? throw new NotSupportedException($"No converter is registered for '{metadata.Extension ?? "unknown"}' files.");
        return await converter.ConvertAsync(stream, metadata, cancellationToken);
    }

    private IMarkItDownConverter? FindConverter(StreamMetadata metadata) =>
        _converters.FirstOrDefault(x => x.CanConvert(metadata));

    private static StreamMetadata InferMetadata(string path)
    {
        var extension = Path.GetExtension(path);
        var mimeType = extension.ToLowerInvariant() switch
        {
            ".txt" or ".md" or ".csv" or ".tsv" or ".json" or ".xml" or ".yaml" or ".yml" => "text/plain",
            ".html" or ".htm" => "text/html",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".pdf" => "application/pdf",
            ".epub" => "application/epub+zip",
            _ => null
        };

        return new StreamMetadata(
            Path.GetFileName(path),
            extension,
            mimeType,
            null,
            Path.GetFullPath(path));
    }
}

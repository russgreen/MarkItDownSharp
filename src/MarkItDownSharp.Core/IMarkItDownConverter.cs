namespace MarkItDownSharp.Core;

/// <summary>
/// Converts one family of file formats to Markdown.
/// </summary>
public interface IMarkItDownConverter
{
    /// <summary>Lower values are tried first when several converters accept the same file.</summary>
    int Priority { get; }

    /// <summary>Stable identifier used for diagnostics and for enabling or disabling the converter.</summary>
    string Name => GetType().Name;

    /// <summary>Short human-readable description shown on the diagnostics page.</summary>
    string Description => string.Empty;

    /// <summary>True when the converter sends document content to another machine.</summary>
    bool RequiresNetwork => false;

    bool CanConvert(StreamMetadata metadata);

    Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default);
}

/// <summary>
/// Optional refinement for converters that need to look at the content (for example to tell an RSS feed from other XML)
/// before they can accept a file. The stream is seekable and positioned at the start.
/// </summary>
public interface IContentSniffer
{
    Task<bool> AcceptsContentAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken);
}

/// <summary>
/// Implemented by converters that convert nested content (archives) and therefore need the owning service.
/// </summary>
public interface IServiceAwareConverter
{
    void Attach(IConversionHost host);
}

/// <summary>
/// The conversion entry point exposed to converters that recurse into nested files.
/// </summary>
public interface IConversionHost
{
    /// <summary>How many nested conversions are currently active above this one.</summary>
    int Depth { get; }

    bool CanConvert(string fileName);

    Task<ConversionResult> ConvertStreamAsync(Stream stream, string fileName, CancellationToken cancellationToken = default);
}

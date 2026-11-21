using System.IO.Compression;
using System.Text;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Converters;

/// <summary>
/// Converts every supported file inside a ZIP archive, recursing into nested archives up to <see cref="MaxDepth"/>.
/// Entry count and size limits protect against decompression bombs.
/// </summary>
public sealed class ZipConverter : IMarkItDownConverter, IExtensionProvider, IServiceAwareConverter
{
    private IConversionHost? _host;

    public int MaxDepth { get; init; } = 3;
    public int MaxEntries { get; init; } = 500;
    public long MaxEntryBytes { get; init; } = 50L * 1024 * 1024;
    public long MaxTotalBytes { get; init; } = 200L * 1024 * 1024;

    public int Priority => 20;
    public string Name => "ZIP archive";
    public string Description => "ZIP archives: each supported file is converted, nested archives are followed to a limited depth.";
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".zip" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    void IServiceAwareConverter.Attach(IConversionHost host) => _host = host;

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        var host = _host ?? throw new InvalidOperationException("The ZIP converter must be registered with a MarkItDownService.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var output = new StringBuilder();
        var skipped = new List<string>();
        long totalBytes = 0;
        var processed = 0;

        foreach (var entry in archive.Entries.Where(e => e.Name.Length > 0).OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (processed >= MaxEntries)
            {
                skipped.Add($"{entry.FullName} (entry limit of {MaxEntries} reached)");
                continue;
            }

            var isNestedArchive = entry.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
            if (isNestedArchive && host.Depth >= MaxDepth)
            {
                skipped.Add($"{entry.FullName} (nested deeper than {MaxDepth} levels)");
                continue;
            }

            if (!host.CanConvert(entry.Name))
            {
                skipped.Add($"{entry.FullName} (unsupported format)");
                continue;
            }

            if (entry.Length > MaxEntryBytes || totalBytes + entry.Length > MaxTotalBytes)
            {
                skipped.Add($"{entry.FullName} (too large)");
                continue;
            }

            try
            {
                // The declared length can be forged, so enforce the limit while copying as well.
                using var buffer = new MemoryStream();
                await using (var entryStream = entry.Open())
                {
                    if (!await CopyLimitedAsync(entryStream, buffer, MaxEntryBytes, cancellationToken))
                    {
                        skipped.Add($"{entry.FullName} (too large)");
                        continue;
                    }
                }

                totalBytes += buffer.Length;
                buffer.Position = 0;
                var result = await host.ConvertStreamAsync(buffer, entry.Name, cancellationToken);
                output.Append("## ").Append(entry.FullName).Append("\n\n").Append(result.Markdown.Trim()).Append("\n\n");
                processed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                skipped.Add($"{entry.FullName} (conversion failed: {ex.Message})");
            }
        }

        if (skipped.Count > 0)
        {
            output.Append("## Skipped files\n\n");
            foreach (var item in skipped)
            {
                output.Append("- ").Append(item).Append('\n');
            }
        }

        return new ConversionResult(output.ToString().Trim(), Path.GetFileNameWithoutExtension(metadata.FileName));
    }

    private static async Task<bool> CopyLimitedAsync(Stream source, Stream destination, long limit, CancellationToken cancellationToken)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > limit)
            {
                return false;
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return true;
    }
}

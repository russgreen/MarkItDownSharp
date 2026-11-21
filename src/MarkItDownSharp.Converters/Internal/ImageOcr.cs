using System.IO.Compression;
using System.Text;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Converters.Internal;

/// <summary>
/// Runs an <see cref="IOcrEngine"/> over images embedded in a document and formats the result as a Markdown section.
/// </summary>
internal static class ImageOcr
{
    private const int MaxImages = 50;
    private const long MaxImageBytes = 20 * 1024 * 1024;

    private static readonly Dictionary<string, string> RasterTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
        [".tif"] = "image/tiff",
        [".tiff"] = "image/tiff"
    };

    /// <summary>
    /// OCRs raster images stored in a <c>media</c> folder of an Office Open XML package (word/media, ppt/media, xl/media).
    /// </summary>
    public static async Task<string> FromPackageAsync(Stream package, IOcrEngine? engine, CancellationToken cancellationToken)
    {
        if (engine is null)
        {
            return string.Empty;
        }

        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        var images = new List<(string Name, byte[] Bytes, string? MimeType)>();
        foreach (var entry in archive.Entries
                     .Where(e => ("/" + e.FullName).Contains("/media/", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
        {
            if (images.Count >= MaxImages)
            {
                break;
            }

            if (!RasterTypes.TryGetValue(Path.GetExtension(entry.Name), out var mimeType) || entry.Length > MaxImageBytes)
            {
                continue;
            }

            await using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            await entryStream.CopyToAsync(buffer, cancellationToken);
            images.Add((entry.Name, buffer.ToArray(), mimeType));
        }

        return await FormatAsync(images, engine, cancellationToken);
    }

    public static async Task<string> FormatAsync(
        IEnumerable<(string Name, byte[] Bytes, string? MimeType)> images,
        IOcrEngine? engine,
        CancellationToken cancellationToken)
    {
        if (engine is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var (name, bytes, mimeType) in images)
        {
            var text = await engine.RecognizeAsync(bytes, mimeType, cancellationToken);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            builder.Append("### ").Append(name).Append("\n\n").Append(TextDecoding.NormalizeNewlines(text).Trim()).Append("\n\n");
        }

        return builder.Length == 0 ? string.Empty : "## Text recognised in images\n\n" + builder.ToString().TrimEnd();
    }
}

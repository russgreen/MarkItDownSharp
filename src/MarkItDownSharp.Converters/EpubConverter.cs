using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using MarkItDownSharp.Converters.Internal;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Converters;

public sealed class EpubConverter : IMarkItDownConverter, IExtensionProvider
{
    private const long MaxChapterBytes = 20 * 1024 * 1024;

    public int Priority => 0;
    public string Name => "EPUB";
    public string Description => "EPUB books: metadata followed by every chapter in reading order.";
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".epub" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var container = await LoadXmlAsync(archive, "META-INF/container.xml", cancellationToken)
            ?? throw new InvalidDataException("The EPUB has no META-INF/container.xml.");
        var packagePath = container.Descendants().FirstOrDefault(e => e.Name.LocalName == "rootfile")?.Attribute("full-path")?.Value
            ?? throw new InvalidDataException("The EPUB container does not name a package document.");
        var package = await LoadXmlAsync(archive, packagePath, cancellationToken)
            ?? throw new InvalidDataException($"The EPUB package document '{packagePath}' is missing.");

        var title = package.Descendants().FirstOrDefault(e => e.Name.LocalName == "title")?.Value.Trim();
        var authors = package.Descendants().Where(e => e.Name.LocalName == "creator").Select(e => e.Value.Trim()).Where(x => x.Length > 0).ToList();
        var language = package.Descendants().FirstOrDefault(e => e.Name.LocalName == "language")?.Value.Trim();

        var manifest = package.Descendants().Where(e => e.Name.LocalName == "item")
            .Where(e => e.Attribute("id") is not null && e.Attribute("href") is not null)
            .GroupBy(e => e.Attribute("id")!.Value)
            .ToDictionary(g => g.Key, g => g.First().Attribute("href")!.Value);
        var baseFolder = packagePath.Contains('/') ? packagePath[..packagePath.LastIndexOf('/')] : string.Empty;

        var output = new StringBuilder();
        output.Append("# ").Append(string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(metadata.FileName) : title).Append("\n\n");
        if (authors.Count > 0)
        {
            output.Append("**Authors:** ").Append(string.Join(", ", authors)).Append("\n\n");
        }

        if (!string.IsNullOrWhiteSpace(language))
        {
            output.Append("**Language:** ").Append(language).Append("\n\n");
        }

        foreach (var itemRef in package.Descendants().Where(e => e.Name.LocalName == "itemref"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (itemRef.Attribute("linear")?.Value == "no" ||
                itemRef.Attribute("idref")?.Value is not { } id ||
                !manifest.TryGetValue(id, out var href))
            {
                continue;
            }

            var entryPath = Resolve(baseFolder, href);
            var entry = archive.GetEntry(entryPath);
            if (entry is null || entry.Length > MaxChapterBytes)
            {
                continue;
            }

            await using var entryStream = entry.Open();
            var html = await TextDecoding.ReadAllTextAsync(entryStream, cancellationToken);
            var markdown = HtmlConverter.ConvertHtml(html).Markdown;
            if (markdown.Length > 0)
            {
                output.Append(markdown).Append("\n\n");
            }
        }

        return new ConversionResult(
            output.ToString().Trim(),
            string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(metadata.FileName) : title);
    }

    private static async Task<XDocument?> LoadXmlAsync(ZipArchive archive, string path, CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry(path);
        if (entry is null)
        {
            return null;
        }

        await using var stream = entry.Open();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;
        return SafeXml.Load(buffer);
    }

    // Manifest hrefs are relative to the package document, may be percent-encoded and may contain ".." segments.
    private static string Resolve(string baseFolder, string href)
    {
        var path = Uri.UnescapeDataString(href.Split('#')[0]);
        var segments = new List<string>();
        foreach (var segment in (baseFolder.Length == 0 ? path : baseFolder + "/" + path).Split('/'))
        {
            if (segment == "..")
            {
                if (segments.Count > 0)
                {
                    segments.RemoveAt(segments.Count - 1);
                }
            }
            else if (segment != "." && segment.Length > 0)
            {
                segments.Add(segment);
            }
        }

        return string.Join('/', segments);
    }
}

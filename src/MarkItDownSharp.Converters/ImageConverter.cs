using System.Text;
using MarkItDownSharp.Converters.Internal;
using MarkItDownSharp.Core;
using MetadataExtractor;

namespace MarkItDownSharp.Converters;

public sealed class ImageConverter : IMarkItDownConverter, IExtensionProvider
{
    private const int MaxTagValueLength = 200;

    private static readonly string[] IgnoredDirectories = ["ICC Profile", "Photoshop", "XMP", "IPTC"];

    private readonly IImageDescriber? _describer;
    private readonly IOcrEngine? _ocr;

    public ImageConverter(IImageDescriber? describer = null, IOcrEngine? ocr = null)
    {
        _describer = describer;
        _ocr = ocr;
    }

    public int Priority => 0;
    public string Name => "Images";
    public string Description => "Image metadata (dimensions, camera, location) with an optional AI description and OCR text.";
    public bool RequiresNetwork => _describer is not null;
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        var name = Path.GetFileNameWithoutExtension(metadata.FileName);
        var output = new StringBuilder();
        output.Append("# ").Append(name).Append("\n\n");

        var rows = new List<IReadOnlyList<string>> { new[] { "Property", "Value" } };
        try
        {
            foreach (var directory in ImageMetadataReader.ReadMetadata(stream))
            {
                if (IgnoredDirectories.Contains(directory.Name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var tag in directory.Tags)
                {
                    var description = tag.Description;
                    if (string.IsNullOrWhiteSpace(description) || description.Length > MaxTagValueLength)
                    {
                        continue;
                    }

                    rows.Add([$"{directory.Name}: {tag.Name}", description]);
                }
            }
        }
        catch (ImageProcessingException)
        {
            // Unrecognised or damaged image data: still produce the file name and any description/OCR below.
        }

        if (rows.Count > 1)
        {
            output.Append("## Metadata\n\n").Append(MarkdownTable.Build(rows)).Append("\n\n");
        }

        if (_describer is not null || _ocr is not null)
        {
            stream.Position = 0;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            var bytes = buffer.ToArray();

            if (_describer is not null)
            {
                var description = await _describer.DescribeAsync(bytes, metadata.MimeType ?? "image/png", cancellationToken);
                if (!string.IsNullOrWhiteSpace(description))
                {
                    output.Append("## Description\n\n").Append(TextDecoding.NormalizeNewlines(description).Trim()).Append("\n\n");
                }
            }

            if (_ocr is not null)
            {
                var text = await _ocr.RecognizeAsync(bytes, metadata.MimeType, cancellationToken);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    output.Append("## Text in image\n\n").Append(TextDecoding.NormalizeNewlines(text).Trim()).Append("\n\n");
                }
            }
        }

        return new ConversionResult(output.ToString().Trim(), name);
    }
}

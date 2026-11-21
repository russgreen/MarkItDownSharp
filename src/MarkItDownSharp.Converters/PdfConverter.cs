using System.Text;
using MarkItDownSharp.Converters.Internal;
using MarkItDownSharp.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace MarkItDownSharp.Converters;

public sealed class PdfConverter : IMarkItDownConverter, IExtensionProvider
{
    private const int MaxOcrImages = 50;

    private readonly IOcrEngine? _ocr;

    public PdfConverter(IOcrEngine? ocr = null)
    {
        _ocr = ocr;
    }

    public int Priority => 0;
    public string Name => "PDF";
    public string Description => "PDF text extraction in reading order. Scanned pages can be recognised when OCR is enabled.";
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".pdf" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        var output = new StringBuilder();
        var images = new List<(string Name, byte[] Bytes, string? MimeType)>();
        string? title;

        using (var document = PdfDocument.Open(stream))
        {
            title = document.Information.Title;
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = ContentOrderTextExtractor.GetText(page).Trim();
                if (text.Length > 0)
                {
                    output.Append(TextDecoding.NormalizeNewlines(text)).Append("\n\n");
                }

                if (_ocr is not null && images.Count < MaxOcrImages)
                {
                    CollectImages(page, images);
                }
            }
        }

        var ocr = await ImageOcr.FormatAsync(images, _ocr, cancellationToken);
        if (ocr.Length > 0)
        {
            output.Append(ocr).Append("\n\n");
        }

        return new ConversionResult(
            output.ToString().Trim(),
            string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(metadata.FileName) : title);
    }

    private static void CollectImages(Page page, List<(string Name, byte[] Bytes, string? MimeType)> images)
    {
        var index = 0;
        foreach (var image in page.GetImages())
        {
            if (images.Count >= MaxOcrImages)
            {
                return;
            }

            index++;
            var name = $"Page {page.Number}, image {index}";
            if (image.TryGetPng(out var png) && png is not null)
            {
                images.Add((name, png, "image/png"));
            }
            else if (image.RawBytes.Length > 0 && IsJpeg(image.RawBytes))
            {
                images.Add((name, image.RawBytes.ToArray(), "image/jpeg"));
            }
        }
    }

    private static bool IsJpeg(ReadOnlySpan<byte> bytes) =>
        bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
}

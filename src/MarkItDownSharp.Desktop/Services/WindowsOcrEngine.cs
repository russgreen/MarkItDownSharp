using MarkItDownSharp.Core;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace MarkItDownSharp.Desktop.Services;

/// <summary>Recognises text locally with the Windows OCR engine. No image data leaves the computer.</summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    private readonly OcrEngine? _engine = OcrEngine.TryCreateFromUserProfileLanguages();

    public async Task<string?> RecognizeAsync(byte[] image, string? mimeType, CancellationToken cancellationToken)
    {
        if (_engine is null)
        {
            return null;
        }

        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream))
            {
                writer.WriteBytes(image);
                await writer.StoreAsync();
                writer.DetachStream();
            }

            stream.Seek(0);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            if (bitmap.PixelWidth > OcrEngine.MaxImageDimension || bitmap.PixelHeight > OcrEngine.MaxImageDimension)
            {
                return null;
            }

            var result = await _engine.RecognizeAsync(bitmap);
            return string.IsNullOrWhiteSpace(result.Text) ? null : result.Text;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or ArgumentException)
        {
            // Unsupported or damaged image: skip it rather than failing the whole document.
            return null;
        }
    }
}

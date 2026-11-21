using System.Text;

namespace MarkItDownSharp.Converters.Internal;

/// <summary>
/// Detects the encoding of text files and normalises line endings so every converter produces the same Markdown newlines.
/// </summary>
public static class TextDecoding
{
    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, throwOnInvalidBytes: true);

    static TextDecoding()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static async Task<string> ReadAllTextAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    public static string Decode(ReadOnlySpan<byte> bytes) =>
        NormalizeNewlines(DecodeRaw(bytes, out _));

    public static string DecodeRaw(ReadOnlySpan<byte> bytes, out Encoding encoding)
    {
        if (bytes.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            encoding = Encoding.UTF8;
            return encoding.GetString(bytes[3..]);
        }

        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
        {
            encoding = new UTF32Encoding(false, true);
            return encoding.GetString(bytes[4..]);
        }

        if (bytes.StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
        {
            encoding = new UTF32Encoding(true, true);
            return encoding.GetString(bytes[4..]);
        }

        if (bytes.StartsWith(new byte[] { 0xFF, 0xFE }))
        {
            encoding = Encoding.Unicode;
            return encoding.GetString(bytes[2..]);
        }

        if (bytes.StartsWith(new byte[] { 0xFE, 0xFF }))
        {
            encoding = Encoding.BigEndianUnicode;
            return encoding.GetString(bytes[2..]);
        }

        if (LooksLikeUtf16WithoutBom(bytes, out var bigEndian))
        {
            encoding = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
            return encoding.GetString(bytes);
        }

        try
        {
            encoding = StrictUtf8;
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8: fall back to the Windows ANSI code page most legacy text files use.
            encoding = Encoding.GetEncoding(1252);
            return encoding.GetString(bytes);
        }
    }

    public static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static bool LooksLikeUtf16WithoutBom(ReadOnlySpan<byte> bytes, out bool bigEndian)
    {
        bigEndian = false;
        var sample = bytes[..Math.Min(bytes.Length, 512)];
        if (sample.Length < 4 || sample.Length % 2 != 0)
        {
            return false;
        }

        var evenZeros = 0;
        var oddZeros = 0;
        for (var i = 0; i < sample.Length; i++)
        {
            if (sample[i] == 0)
            {
                if (i % 2 == 0)
                {
                    evenZeros++;
                }
                else
                {
                    oddZeros++;
                }
            }
        }

        var half = sample.Length / 2;
        if (oddZeros > half * 0.6 && evenZeros == 0)
        {
            return true;
        }

        if (evenZeros > half * 0.6 && oddZeros == 0)
        {
            bigEndian = true;
            return true;
        }

        return false;
    }
}

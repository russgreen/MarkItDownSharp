namespace MarkItDownSharp.Core;

/// <summary>Extracts text from a raster image. Implementations may be local (Windows OCR) or remote.</summary>
public interface IOcrEngine
{
    /// <returns>The recognised text, or <see langword="null"/> when nothing was found.</returns>
    Task<string?> RecognizeAsync(byte[] image, string? mimeType, CancellationToken cancellationToken);
}

/// <summary>Produces a short natural-language description of an image.</summary>
public interface IImageDescriber
{
    Task<string?> DescribeAsync(byte[] image, string mimeType, CancellationToken cancellationToken);
}

/// <summary>Transcribes speech from an audio stream.</summary>
public interface ISpeechTranscriber
{
    Task<string?> TranscribeAsync(Stream audio, string fileName, CancellationToken cancellationToken);
}

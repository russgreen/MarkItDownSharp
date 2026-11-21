using System.Globalization;
using System.Text;
using MarkItDownSharp.Converters.Internal;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Converters;

public sealed class AudioConverter : IMarkItDownConverter, IExtensionProvider
{
    private readonly ISpeechTranscriber? _transcriber;

    public AudioConverter(ISpeechTranscriber? transcriber = null)
    {
        _transcriber = transcriber;
    }

    public int Priority => 0;
    public string Name => "Audio";
    public string Description => "Audio tags and properties (title, artist, duration, bitrate) with an optional speech transcript.";
    public bool RequiresNetwork => _transcriber is not null;
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".mp3", ".wav", ".m4a", ".flac", ".ogg" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        var fileName = metadata.FileName ?? "audio";
        var rows = new List<IReadOnlyList<string>> { new[] { "Property", "Value" } };
        string? title = null;

        try
        {
            using var file = TagLib.File.Create(new StreamAbstraction(fileName, stream), TagLib.ReadStyle.Average);
            var tag = file.Tag;
            title = string.IsNullOrWhiteSpace(tag.Title) ? null : tag.Title;
            Add(rows, "Title", tag.Title);
            Add(rows, "Artist", tag.JoinedPerformers);
            Add(rows, "Album", tag.Album);
            Add(rows, "Year", tag.Year > 0 ? tag.Year.ToString(CultureInfo.InvariantCulture) : null);
            Add(rows, "Genre", tag.JoinedGenres);
            Add(rows, "Duration", file.Properties.Duration > TimeSpan.Zero ? file.Properties.Duration.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture) : null);
            Add(rows, "Bitrate", file.Properties.AudioBitrate > 0 ? $"{file.Properties.AudioBitrate} kbps" : null);
            Add(rows, "Sample rate", file.Properties.AudioSampleRate > 0 ? $"{file.Properties.AudioSampleRate} Hz" : null);
            Add(rows, "Channels", file.Properties.AudioChannels > 0 ? file.Properties.AudioChannels.ToString(CultureInfo.InvariantCulture) : null);
        }
        catch (Exception ex) when (ex is TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
        {
            // Keep going so a transcript is still produced for files TagLib cannot parse.
        }

        var output = new StringBuilder();
        output.Append("# ").Append(title ?? Path.GetFileNameWithoutExtension(fileName)).Append("\n\n");
        if (rows.Count > 1)
        {
            output.Append("## Metadata\n\n").Append(MarkdownTable.Build(rows)).Append("\n\n");
        }

        if (_transcriber is not null)
        {
            stream.Position = 0;
            var transcript = await _transcriber.TranscribeAsync(stream, fileName, cancellationToken);
            if (!string.IsNullOrWhiteSpace(transcript))
            {
                output.Append("## Transcript\n\n").Append(TextDecoding.NormalizeNewlines(transcript).Trim()).Append("\n\n");
            }
        }

        return new ConversionResult(output.ToString().Trim(), title ?? Path.GetFileNameWithoutExtension(fileName));
    }

    private static void Add(List<IReadOnlyList<string>> rows, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            rows.Add([name, value]);
        }
    }

    private sealed class StreamAbstraction : TagLib.File.IFileAbstraction
    {
        private readonly Stream _stream;

        public StreamAbstraction(string name, Stream stream)
        {
            Name = name;
            _stream = stream;
        }

        public string Name { get; }
        public Stream ReadStream => _stream;
        public Stream WriteStream => throw new NotSupportedException("Audio files are read-only.");

        // The caller owns the stream, so TagLib must not close it.
        public void CloseStream(Stream stream)
        {
        }
    }
}

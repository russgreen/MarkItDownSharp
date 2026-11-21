using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using MarkItDownSharp.Converters.Internal;
using MarkItDownSharp.Core;
using YamlDotNet.RepresentationModel;

namespace MarkItDownSharp.Converters;

public sealed class StructuredTextConverter : IMarkItDownConverter, IExtensionProvider
{
    private static readonly JsonWriterOptions JsonWriterOptions = new()
    {
        Indented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonDocumentOptions JsonReaderOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public int Priority => 0;
    public string Name => "Structured text";
    public string Description => "JSON, YAML and XML as formatted code blocks. Invalid documents are kept verbatim.";
    public IReadOnlyCollection<string> Extensions { get; } =
        new[] { ".json", ".xml", ".yaml", ".yml" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        var text = (await TextDecoding.ReadAllTextAsync(stream, cancellationToken)).Trim('﻿');
        var language = metadata.Extension?.TrimStart('.').ToLowerInvariant() switch
        {
            "yml" => "yaml",
            var other => other
        };

        var formatted = language switch
        {
            "json" => FormatJson(text),
            "xml" => FormatXml(text),
            "yaml" => FormatYaml(text),
            _ => text
        };

        // Use a fence longer than any backtick run inside the document so the block cannot be closed early.
        var fence = new string('`', Math.Max(3, LongestBacktickRun(formatted) + 1));
        var markdown = $"{fence}{language}\n{formatted.TrimEnd()}\n{fence}";
        return new ConversionResult(markdown, Path.GetFileNameWithoutExtension(metadata.FileName));
    }

    private static string FormatJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, JsonReaderOptions);
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, JsonWriterOptions))
            {
                document.WriteTo(writer);
            }

            return TextDecoding.NormalizeNewlines(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
        }
        catch (JsonException)
        {
            return text;
        }
    }

    private static string FormatXml(string text)
    {
        try
        {
            var document = SafeXml.Parse(text);
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
                OmitXmlDeclaration = document.Declaration is null
            };

            using var writer = new StringWriter();
            using (var xml = XmlWriter.Create(writer, settings))
            {
                document.Save(xml);
            }

            return writer.ToString();
        }
        catch (XmlException)
        {
            return text;
        }
    }

    private static string FormatYaml(string text)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(text));
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return text;
        }

        // YAML comments are not part of the representation model, so the original text is kept and only tidied.
        var lines = text.Split('\n').Select(line => line.TrimEnd());
        return string.Join('\n', lines);
    }

    private static int LongestBacktickRun(string text)
    {
        var longest = 0;
        var current = 0;
        foreach (var c in text)
        {
            current = c == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }
}

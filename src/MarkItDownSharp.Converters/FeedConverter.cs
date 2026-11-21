using System.Text;
using System.Xml;
using System.Xml.Linq;
using MarkItDownSharp.Converters.Internal;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Converters;

public sealed class FeedConverter : IMarkItDownConverter, IExtensionProvider, IContentSniffer
{
    private static readonly string[] FeedRoots = ["rss", "feed", "RDF"];

    public int Priority => -10;
    public string Name => "RSS and Atom";
    public string Description => "RSS 2.0, RSS 1.0 and Atom feeds. XML files are only treated as feeds when their root element says so.";
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".rss", ".atom", ".xml" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public Task<bool> AcceptsContentAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken)
    {
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using var reader = XmlReader.Create(stream, settings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    return Task.FromResult(FeedRoots.Contains(reader.LocalName, StringComparer.Ordinal));
                }
            }
        }
        catch (XmlException)
        {
        }

        return Task.FromResult(false);
    }

    public Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        var document = SafeXml.Load(stream);
        var root = document.Root ?? throw new InvalidDataException("The feed has no root element.");
        var output = new StringBuilder();
        string? title;

        if (root.Name.LocalName == "feed")
        {
            title = Text(root, "title");
            AppendHeader(output, title, Text(root, "subtitle"));
            foreach (var entry in root.Elements().Where(e => e.Name.LocalName == "entry"))
            {
                var link = entry.Elements().FirstOrDefault(e => e.Name.LocalName == "link" && ((string?)e.Attribute("rel") is null or "alternate"))?.Attribute("href")?.Value;
                AppendItem(
                    output,
                    Text(entry, "title"),
                    link,
                    Text(entry, "updated") ?? Text(entry, "published"),
                    Text(entry, "content") ?? Text(entry, "summary"),
                    entry.Elements().FirstOrDefault(e => e.Name.LocalName == "author")?.Elements().FirstOrDefault(e => e.Name.LocalName == "name")?.Value);
            }
        }
        else
        {
            // RSS 2.0 nests items in <channel>; RSS 1.0 (RDF) places them beside it.
            var channel = root.Elements().FirstOrDefault(e => e.Name.LocalName == "channel") ?? root;
            title = Text(channel, "title");
            AppendHeader(output, title, Text(channel, "description"));
            foreach (var item in root.Descendants().Where(e => e.Name.LocalName == "item"))
            {
                AppendItem(
                    output,
                    Text(item, "title"),
                    Text(item, "link"),
                    Text(item, "pubDate") ?? Text(item, "date"),
                    Text(item, "encoded") ?? Text(item, "description"),
                    Text(item, "creator") ?? Text(item, "author"));
            }
        }

        var markdown = TextDecoding.NormalizeNewlines(output.ToString()).Trim();
        return Task.FromResult(new ConversionResult(markdown, title ?? Path.GetFileNameWithoutExtension(metadata.FileName)));
    }

    private static string? Text(XElement parent, string localName)
    {
        var value = parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static void AppendHeader(StringBuilder output, string? title, string? description)
    {
        output.Append("# ").Append(title ?? "Untitled feed").Append("\n\n");
        if (description is not null)
        {
            output.Append(HtmlConverter.ConvertHtml(description).Markdown).Append("\n\n");
        }
    }

    private static void AppendItem(StringBuilder output, string? title, string? link, string? date, string? body, string? author)
    {
        output.Append("## ");
        output.Append(link is null ? title ?? "Untitled" : $"[{title ?? link}]({link})");
        output.Append("\n\n");

        var details = new List<string>();
        if (date is not null)
        {
            details.Add(date);
        }

        if (author is not null)
        {
            details.Add($"by {author}");
        }

        if (details.Count > 0)
        {
            output.Append('*').Append(string.Join(" ", details)).Append("*\n\n");
        }

        if (body is not null)
        {
            output.Append(HtmlConverter.ConvertHtml(body).Markdown).Append("\n\n");
        }
    }
}

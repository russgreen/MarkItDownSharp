using HtmlAgilityPack;
using MarkItDownSharp.Converters.Internal;
using MarkItDownSharp.Core;
using ReverseMarkdown;

namespace MarkItDownSharp.Converters;

public sealed class HtmlConverter : IMarkItDownConverter, IExtensionProvider
{
    private static readonly string[] RemovedElements = ["script", "style", "noscript", "template", "head", "svg", "iframe"];

    public int Priority => 0;
    public string Name => "HTML";
    public string Description => "HTML pages converted with a structured HTML parser (headings, lists, tables, links, code).";
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".html", ".htm" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        var html = await TextDecoding.ReadAllTextAsync(stream, cancellationToken);
        var (markdown, title) = ConvertHtml(html);
        return new ConversionResult(markdown, title ?? Path.GetFileNameWithoutExtension(metadata.FileName));
    }

    /// <summary>
    /// Converts an HTML or XHTML fragment or document to Markdown. Shared by the EPUB, feed and e-mail converters.
    /// </summary>
    public static (string Markdown, string? Title) ConvertHtml(string html)
    {
        var document = new HtmlDocument { OptionFixNestedTags = true };
        document.LoadHtml(html);

        var title = document.DocumentNode.SelectSingleNode("//title")?.InnerText;
        title = string.IsNullOrWhiteSpace(title) ? null : HtmlEntity.DeEntitize(title).Trim();

        foreach (var node in document.DocumentNode.Descendants().Where(n => RemovedElements.Contains(n.Name, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            node.Remove();
        }

        var root = document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
        var config = new Config { GithubFlavored = true };
        config.Tags.Unknown = Config.UnknownTagsOption.Bypass;
        config.Formatting.RemoveComments = true;
        config.Formatting.ListBulletChar = '-';
        config.Links.SmartHref = true;
        var converter = new Converter(config);

        var markdown = TextDecoding.NormalizeNewlines(converter.Convert(root.InnerHtml)).Trim();
        return (markdown, title);
    }
}

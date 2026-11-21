using System.Text;
using System.Text.RegularExpressions;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Converters;

public sealed partial class HtmlConverter : IMarkItDownConverter, IExtensionProvider
{
    public int Priority => 0;
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".html", ".htm" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var html = await reader.ReadToEndAsync(cancellationToken);

        html = Regex.Replace(html, @"(?is)<(script|style).*?>.*?</\1>", "");
        html = Regex.Replace(html, @"(?is)<h([1-6])[^>]*>(.*?)</h\1>", m =>
            $"{Environment.NewLine}{new string('#', int.Parse(m.Groups[1].Value))} {StripTags(m.Groups[2].Value)}{Environment.NewLine}");
        html = Regex.Replace(html, @"(?is)<(p|div|section|article|blockquote)[^>]*>", Environment.NewLine);
        html = Regex.Replace(html, @"(?is)</(p|div|section|article|blockquote)>", Environment.NewLine);
        html = Regex.Replace(html, @"(?is)<li[^>]*>", "- ");
        html = Regex.Replace(html, @"(?is)</li>", Environment.NewLine);
        html = Regex.Replace(html, @"(?is)<br\s*/?>", Environment.NewLine);
        html = Regex.Replace(html, @"(?is)<a[^>]*href=[""']([^""']+)[""'][^>]*>(.*?)</a>", "[${2}](${1})");
        html = Regex.Replace(html, @"(?is)<(strong|b)[^>]*>(.*?)</\1>", "**${2}**");
        html = Regex.Replace(html, @"(?is)<(em|i)[^>]*>(.*?)</\1>", "*${2}*");
        html = StripTags(html);
        html = System.Net.WebUtility.HtmlDecode(html);
        html = Regex.Replace(html, @"[ \t]+\n", "\n");
        html = Regex.Replace(html, @"\n{3,}", "\n\n").Trim();

        return new ConversionResult(html, Path.GetFileNameWithoutExtension(metadata.FileName));
    }

    private static string StripTags(string value) =>
        Regex.Replace(value, @"<[^>]+>", "");
}

using System.Text;
using MarkItDownSharp.Converters;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Tests;

public sealed class ConversionTests
{
    [Fact]
    public async Task TextConverter_PreservesContentAndTitle()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new MarkItDownService([new TextConverter()]);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("hello\nworld"));

        var result = await service.ConvertStreamAsync(stream, "notes.txt", cancellationToken);

        Assert.Equal("hello\nworld", result.Markdown);
        Assert.Equal("notes", result.Title);
    }

    [Fact]
    public async Task CsvConverter_HandlesQuotedCommasAndEscapesPipes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new MarkItDownService([new CsvConverter()]);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name,Value\n\"one, two\",a|b"));

        var result = await service.ConvertStreamAsync(stream, "values.csv", cancellationToken);

        Assert.Equal("| Name | Value |\n| --- | --- |\n| one, two | a\\|b |", result.Markdown.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task HtmlConverter_ExtractsStructure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new MarkItDownService([new HtmlConverter()]);
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "<h1>Title</h1><p>Hello <strong>world</strong>.</p><script>alert(1)</script>"));

        var result = await service.ConvertStreamAsync(stream, "page.html", cancellationToken);

        Assert.Contains("# Title", result.Markdown);
        Assert.Contains("Hello **world**.", result.Markdown);
        Assert.DoesNotContain("alert", result.Markdown);
    }

    [Fact]
    public async Task Service_RejectsUnsupportedFiles()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var service = new MarkItDownService([new TextConverter()]);
        await using var stream = new MemoryStream();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => service.ConvertStreamAsync(stream, "document.bin", cancellationToken));
    }
}

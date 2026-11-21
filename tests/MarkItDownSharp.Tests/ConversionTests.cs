using System.Text;
using MarkItDownSharp.Converters;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Tests;

public sealed class ConversionTests
{
    private static MarkItDownService Service(params IMarkItDownConverter[] converters) => new(converters);

    private static async Task<ConversionResult> ConvertAsync(IMarkItDownConverter converter, string fileName, byte[] bytes)
    {
        await using var stream = new MemoryStream(bytes);
        return await Service(converter).ConvertStreamAsync(stream, fileName, TestContext.Current.CancellationToken);
    }

    private static Task<ConversionResult> ConvertAsync(IMarkItDownConverter converter, string fileName, string text) =>
        ConvertAsync(converter, fileName, Encoding.UTF8.GetBytes(text));

    [Fact]
    public async Task TextConverter_PreservesContentAndTitle()
    {
        var result = await ConvertAsync(new TextConverter(), "notes.txt", "hello\nworld");

        Assert.Equal("hello\nworld", result.Markdown);
        Assert.Equal("notes", result.Title);
    }

    [Fact]
    public async Task TextConverter_NormalisesNewlines()
    {
        var result = await ConvertAsync(new TextConverter(), "notes.txt", "a\r\nb\rc\n");

        Assert.Equal("a\nb\nc\n", result.Markdown);
    }

    [Fact]
    public async Task TextConverter_DetectsUtf16WithBom()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("héllo")).ToArray();

        var result = await ConvertAsync(new TextConverter(), "utf16.txt", bytes);

        Assert.Equal("héllo", result.Markdown);
    }

    [Fact]
    public async Task TextConverter_DetectsUtf16WithoutBom()
    {
        var result = await ConvertAsync(new TextConverter(), "utf16.txt", Encoding.Unicode.GetBytes("plain text here"));

        Assert.Equal("plain text here", result.Markdown);
    }

    [Fact]
    public async Task TextConverter_FallsBackToWindows1252ForInvalidUtf8()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1252).GetBytes("café – “quoted”");

        var result = await ConvertAsync(new TextConverter(), "legacy.txt", bytes);

        Assert.Equal("café – “quoted”", result.Markdown);
    }

    [Fact]
    public async Task TextConverter_StripsUtf8Bom()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("bom")).ToArray();

        var result = await ConvertAsync(new TextConverter(), "bom.txt", bytes);

        Assert.Equal("bom", result.Markdown);
    }

    [Fact]
    public async Task CsvConverter_HandlesQuotedCommasAndEscapesPipes()
    {
        var result = await ConvertAsync(new CsvConverter(), "values.csv", "Name,Value\n\"one, two\",a|b");

        Assert.Equal("| Name | Value |\n| --- | --- |\n| one, two | a\\|b |", result.Markdown);
    }

    [Fact]
    public async Task CsvConverter_KeepsQuotedLineBreaksInsideOneCell()
    {
        var result = await ConvertAsync(new CsvConverter(), "values.csv", "A,B\r\n\"line1\r\nline2\",x\r\n");

        Assert.Equal("| A | B |\n| --- | --- |\n| line1 line2 | x |", result.Markdown);
    }

    [Fact]
    public async Task CsvConverter_ConvertsTsv()
    {
        var result = await ConvertAsync(new CsvConverter(), "values.tsv", "A\tB\n1\t2");

        Assert.Equal("| A | B |\n| --- | --- |\n| 1 | 2 |", result.Markdown);
    }

    [Fact]
    public async Task HtmlConverter_ExtractsStructure()
    {
        var result = await ConvertAsync(
            new HtmlConverter(),
            "page.html",
            "<h1>Title</h1><p>Hello <strong>world</strong>.</p><script>alert(1)</script>");

        Assert.Contains("# Title", result.Markdown);
        Assert.Contains("Hello **world**.", result.Markdown);
        Assert.DoesNotContain("alert", result.Markdown);
    }

    [Fact]
    public async Task HtmlConverter_ConvertsListsTablesLinksAndCode()
    {
        var html = """
            <html><head><title>Doc title</title><style>p{color:red}</style></head><body>
            <ul><li>one</li><li>two</li></ul>
            <table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>
            <p>See <a href="https://example.com/page">the page</a>.</p>
            <pre><code>var x = 1;</code></pre>
            </body></html>
            """;

        var result = await ConvertAsync(new HtmlConverter(), "page.html", html);

        Assert.Equal("Doc title", result.Title);
        Assert.Contains("- one", result.Markdown);
        Assert.Contains("- two", result.Markdown);
        Assert.Contains("| A | B |", result.Markdown);
        Assert.Contains("| 1 | 2 |", result.Markdown);
        Assert.Contains("[the page](https://example.com/page)", result.Markdown);
        Assert.Contains("var x = 1;", result.Markdown);
        Assert.DoesNotContain("color:red", result.Markdown);
        Assert.DoesNotContain("Doc title", result.Markdown);
    }

    [Fact]
    public async Task StructuredText_PrettyPrintsJson()
    {
        var result = await ConvertAsync(new StructuredTextConverter(), "data.json", "{\"a\":1,\"b\":[1,2],\"c\":\"é\"}");

        Assert.Equal("```json\n{\n  \"a\": 1,\n  \"b\": [\n    1,\n    2\n  ],\n  \"c\": \"é\"\n}\n```", result.Markdown);
    }

    [Fact]
    public async Task StructuredText_KeepsInvalidJsonVerbatim()
    {
        var result = await ConvertAsync(new StructuredTextConverter(), "data.json", "{ not json");

        Assert.Equal("```json\n{ not json\n```", result.Markdown);
    }

    [Fact]
    public async Task StructuredText_IndentsXml()
    {
        var result = await ConvertAsync(new StructuredTextConverter(), "data.xml", "<root><child>1</child><child>2</child></root>");

        Assert.Equal("```xml\n<root>\n  <child>1</child>\n  <child>2</child>\n</root>\n```", result.Markdown);
    }

    [Fact]
    public async Task StructuredText_NormalisesYamlWhitespaceAndKeepsComments()
    {
        var result = await ConvertAsync(new StructuredTextConverter(), "data.yml", "# comment\r\nname: test   \r\nitems:\r\n  - a\r\n");

        Assert.Equal("```yaml\n# comment\nname: test\nitems:\n  - a\n```", result.Markdown);
    }

    [Fact]
    public async Task StructuredText_UsesLongerFenceWhenContentContainsBackticks()
    {
        var result = await ConvertAsync(new StructuredTextConverter(), "data.yaml", "text: \"```\"");

        Assert.StartsWith("````yaml\n", result.Markdown);
        Assert.EndsWith("\n````", result.Markdown);
    }

    [Fact]
    public async Task FeedConverter_ConvertsRss()
    {
        const string rss = """
            <?xml version="1.0"?>
            <rss version="2.0"><channel><title>My Feed</title><description>About things</description>
            <item><title>First</title><link>https://example.com/1</link><pubDate>Mon, 01 Jan 2024 00:00:00 GMT</pubDate>
            <description>&lt;p&gt;Hello &lt;b&gt;feed&lt;/b&gt;&lt;/p&gt;</description></item>
            </channel></rss>
            """;
        var service = Service(new FeedConverter(), new StructuredTextConverter());
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(rss));

        var result = await service.ConvertStreamAsync(stream, "feed.xml", TestContext.Current.CancellationToken);

        Assert.Equal("My Feed", result.Title);
        Assert.Contains("# My Feed", result.Markdown);
        Assert.Contains("## [First](https://example.com/1)", result.Markdown);
        Assert.Contains("Hello **feed**", result.Markdown);
    }

    [Fact]
    public async Task FeedConverter_ConvertsAtom()
    {
        const string atom = """
            <feed xmlns="http://www.w3.org/2005/Atom"><title>Atom Feed</title>
            <entry><title>Entry</title><link href="https://example.com/e"/><updated>2024-01-01T00:00:00Z</updated>
            <author><name>Ada</name></author><summary>Short summary</summary></entry></feed>
            """;

        var result = await ConvertAsync(new FeedConverter(), "feed.atom", atom);

        Assert.Contains("# Atom Feed", result.Markdown);
        Assert.Contains("## [Entry](https://example.com/e)", result.Markdown);
        Assert.Contains("by Ada", result.Markdown);
        Assert.Contains("Short summary", result.Markdown);
    }

    [Fact]
    public async Task Service_FallsBackToStructuredTextWhenXmlIsNotAFeed()
    {
        var service = Service(new FeedConverter(), new StructuredTextConverter());
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes("<config><a>1</a></config>"));

        var result = await service.ConvertStreamAsync(stream, "config.xml", TestContext.Current.CancellationToken);

        Assert.StartsWith("```xml", result.Markdown);
    }

    [Fact]
    public async Task NotebookConverter_ConvertsCellsAndOutputs()
    {
        const string notebook = """
            {"cells":[
              {"cell_type":"markdown","source":["# Analysis\n","Some text"]},
              {"cell_type":"code","source":["print(1)"],"outputs":[{"output_type":"stream","name":"stdout","text":["1\n"]}]},
              {"cell_type":"code","source":"x = 2","outputs":[{"output_type":"execute_result","data":{"text/plain":["2"]}}]}
            ],"metadata":{"language_info":{"name":"python"}},"nbformat":4}
            """;

        var result = await ConvertAsync(new NotebookConverter(), "analysis.ipynb", notebook);

        Assert.Equal("Analysis", result.Title);
        Assert.Contains("# Analysis\nSome text", result.Markdown);
        Assert.Contains("```python\nprint(1)\n```", result.Markdown);
        Assert.Contains("Output:\n\n```\n1\n```", result.Markdown);
        Assert.Contains("```python\nx = 2\n```", result.Markdown);
    }

    [Fact]
    public async Task NotebookConverter_RejectsFilesWithoutCells()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => ConvertAsync(new NotebookConverter(), "bad.ipynb", "{}"));
    }

    [Fact]
    public async Task Service_RejectsUnsupportedFiles()
    {
        var service = Service(new TextConverter());
        await using var stream = new MemoryStream();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => service.ConvertStreamAsync(stream, "document.bin", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Service_HandlesNonSeekableStreams()
    {
        var service = Service(new FeedConverter(), new StructuredTextConverter());
        await using var inner = new MemoryStream(Encoding.UTF8.GetBytes("<rss version=\"2.0\"><channel><title>T</title></channel></rss>"));
        await using var stream = new NonSeekableStream(inner);

        var result = await service.ConvertStreamAsync(stream, "feed.rss", TestContext.Current.CancellationToken);

        Assert.Equal("T", result.Title);
    }

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

using System.IO.Compression;
using System.Text;
using MarkItDownSharp.Converters;
using MarkItDownSharp.Core;
using MarkItDownSharp.Tests.Builders;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace MarkItDownSharp.Tests;

public sealed class DocumentConverterTests
{
    private static async Task<ConversionResult> ConvertAsync(MarkItDownService service, string fileName, byte[] bytes)
    {
        await using var stream = new MemoryStream(bytes);
        return await service.ConvertStreamAsync(stream, fileName, TestContext.Current.CancellationToken);
    }

    // ---- DOCX ----

    [Fact]
    public async Task Docx_ConvertsHeadingsFormattingListsLinksAndTables()
    {
        var bytes = DocxBuilder.Build();

        var result = await ConvertAsync(new MarkItDownService([new DocxConverter()]), "report.docx", bytes);

        Assert.Equal("Quarterly report", result.Title);
        Assert.Contains("# Heading One", result.Markdown);
        Assert.Contains("## Heading Two", result.Markdown);
        Assert.Contains("Plain **bold** and *italic* text", result.Markdown);
        Assert.Contains("[Example](https://example.com/)", result.Markdown);
        Assert.Contains("- bullet one", result.Markdown);
        Assert.Contains("1. step one", result.Markdown);
        Assert.Contains("| Name | Qty |", result.Markdown);
        Assert.Contains("| Apples | 3 |", result.Markdown);
    }

    [Fact]
    public async Task Docx_RunsOcrOverEmbeddedImages()
    {
        var bytes = DocxBuilder.Build(includeImage: true);
        var service = new MarkItDownService([new DocxConverter(new FakeOcr("TEXT FROM PICTURE"))]);

        var result = await ConvertAsync(service, "report.docx", bytes);

        Assert.Contains("## Text recognised in images", result.Markdown);
        Assert.Contains("TEXT FROM PICTURE", result.Markdown);
    }

    [Fact]
    public async Task Docx_DoesNotMentionOcrWhenNoEngineIsConfigured()
    {
        var result = await ConvertAsync(new MarkItDownService([new DocxConverter()]), "report.docx", DocxBuilder.Build(includeImage: true));

        Assert.DoesNotContain("recognised", result.Markdown);
    }

    // ---- XLSX / XLS ----

    [Fact]
    public async Task Xlsx_ConvertsEachSheetToATable()
    {
        var result = await ConvertAsync(new MarkItDownService([new ExcelConverter()]), "book.xlsx", XlsxBuilder.Build());

        Assert.Contains("## Sales", result.Markdown);
        Assert.Contains("| Region | Total |", result.Markdown);
        Assert.Contains("| North | 12.5 |", result.Markdown);
        Assert.Contains("## Notes", result.Markdown);
        Assert.Contains("| remember | the milk |", result.Markdown);
    }

    [Fact]
    public void Excel_RegistersBothXlsxAndLegacyXls()
    {
        var converter = new ExcelConverter();

        Assert.True(converter.CanConvert(new StreamMetadata(Extension: ".xls")));
        Assert.True(converter.CanConvert(new StreamMetadata(Extension: ".XLSX")));
    }

    // ---- PPTX ----

    [Fact]
    public async Task Pptx_ConvertsSlidesTextTablesAndNotes()
    {
        var result = await ConvertAsync(new MarkItDownService([new PptxConverter()]), "deck.pptx", PptxBuilder.Build());

        Assert.Contains("## Welcome", result.Markdown);
        Assert.Contains("<!-- Slide number: 1 -->", result.Markdown);
        Assert.Contains("- First point", result.Markdown);
        Assert.Contains("### Notes", result.Markdown);
        Assert.Contains("Speaker says hello", result.Markdown);
    }

    // ---- PDF ----

    [Fact]
    public async Task Pdf_ExtractsTextFromEveryPage()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText("First page text", 12, new PdfPoint(50, 700), font);
        builder.AddPage(PageSize.A4).AddText("Second page text", 12, new PdfPoint(50, 700), font);

        var result = await ConvertAsync(new MarkItDownService([new PdfConverter()]), "paper.pdf", builder.Build());

        Assert.Contains("First page text", result.Markdown);
        Assert.Contains("Second page text", result.Markdown);
        Assert.True(result.Markdown.IndexOf("First", StringComparison.Ordinal) < result.Markdown.IndexOf("Second", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pdf_RunsOcrOverEmbeddedImages()
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(PageSize.A4);
        page.AddPng(DocxBuilder.OnePixelPng, new PdfRectangle(50, 600, 150, 700));
        var service = new MarkItDownService([new PdfConverter(new FakeOcr("SCANNED WORDS"))]);

        var result = await ConvertAsync(service, "scan.pdf", builder.Build());

        Assert.Contains("SCANNED WORDS", result.Markdown);
    }

    // ---- EPUB ----

    [Fact]
    public async Task Epub_ConvertsMetadataAndChaptersInSpineOrder()
    {
        var result = await ConvertAsync(new MarkItDownService([new EpubConverter()]), "book.epub", BuildEpub());

        Assert.Equal("The Test Book", result.Title);
        Assert.Contains("# The Test Book", result.Markdown);
        Assert.Contains("**Authors:** Ada Lovelace", result.Markdown);
        var one = result.Markdown.IndexOf("Chapter One", StringComparison.Ordinal);
        var two = result.Markdown.IndexOf("Chapter Two", StringComparison.Ordinal);
        Assert.True(one >= 0 && two > one, "Chapters must follow the spine order, not the file order.");
    }

    // ---- ZIP ----

    [Fact]
    public async Task Zip_ConvertsSupportedEntriesAndListsSkippedOnes()
    {
        var zip = BuildZip(("docs/a.txt", "alpha"u8.ToArray()), ("data.csv", "x,y\n1,2"u8.ToArray()), ("binary.bin", [1, 2, 3]));

        var result = await ConvertAsync(CreateService(), "bundle.zip", zip);

        Assert.Contains("## data.csv", result.Markdown);
        Assert.Contains("| x | y |", result.Markdown);
        Assert.Contains("## docs/a.txt", result.Markdown);
        Assert.Contains("alpha", result.Markdown);
        Assert.Contains("## Skipped files", result.Markdown);
        Assert.Contains("binary.bin (unsupported format)", result.Markdown);
    }

    [Fact]
    public async Task Zip_RecursesIntoNestedArchives()
    {
        var inner = BuildZip(("deep.txt", "deep content"u8.ToArray()));
        var outer = BuildZip(("inner.zip", inner));

        var result = await ConvertAsync(CreateService(), "outer.zip", outer);

        Assert.Contains("## inner.zip", result.Markdown);
        Assert.Contains("## deep.txt", result.Markdown);
        Assert.Contains("deep content", result.Markdown);
    }

    [Fact]
    public async Task Zip_StopsRecursingAtMaximumDepth()
    {
        var bytes = BuildZip(("bottom.txt", "bottom"u8.ToArray()));
        for (var i = 0; i < 6; i++)
        {
            bytes = BuildZip(($"level{i}.zip", bytes));
        }

        var result = await ConvertAsync(CreateService(), "nested.zip", bytes);

        Assert.DoesNotContain("bottom", result.Markdown);
        Assert.Contains("nested deeper than 3 levels", result.Markdown);
    }

    [Fact]
    public async Task Zip_EnforcesEntrySizeLimitAndKeepsGoing()
    {
        var zip = BuildZip(("big.txt", new byte[5000]), ("small.txt", "ok"u8.ToArray()));
        var service = new MarkItDownService([new TextConverter(), new ZipConverter { MaxEntryBytes = 1000 }]);

        var result = await ConvertAsync(service, "limits.zip", zip);

        Assert.Contains("big.txt (too large)", result.Markdown);
        Assert.Contains("## small.txt", result.Markdown);
    }

    [Fact]
    public async Task Zip_ReportsEntriesThatFailToConvert()
    {
        var zip = BuildZip(("broken.ipynb", "not json"u8.ToArray()), ("fine.txt", "fine"u8.ToArray()));

        var result = await ConvertAsync(CreateService(), "mixed.zip", zip);

        Assert.Contains("broken.ipynb (conversion failed:", result.Markdown);
        Assert.Contains("## fine.txt", result.Markdown);
    }

    [Fact]
    public async Task Zip_ConvertsOfficeDocumentsInsideArchives()
    {
        var zip = BuildZip(("report.docx", DocxBuilder.Build()));

        var result = await ConvertAsync(CreateService(), "office.zip", zip);

        Assert.Contains("# Heading One", result.Markdown);
    }

    // ---- ConverterCatalog ----

    [Fact]
    public void Catalog_RegistersTheBuiltInFormats()
    {
        var catalog = ConverterCatalog.Create(new ConverterSettings());

        var extensions = catalog.Service.SupportedExtensions;
        foreach (var extension in new[] { ".docx", ".xlsx", ".xls", ".pptx", ".pdf", ".epub", ".zip", ".msg", ".ipynb", ".rss", ".atom", ".png", ".mp3" })
        {
            Assert.Contains(extension, extensions);
        }
    }

    [Fact]
    public void Catalog_OnlyAddsNetworkConvertersWhenConfigured()
    {
        var plain = ConverterCatalog.Create(new ConverterSettings()).Service.Describe();
        Assert.DoesNotContain(plain, c => c.Name.StartsWith("Azure", StringComparison.Ordinal));

        var configured = ConverterCatalog.Create(new ConverterSettings
        {
            DocumentIntelligence = new AzureSettings { Endpoint = "https://example.cognitiveservices.azure.com/", ApiKey = "key" }
        }).Service.Describe();

        Assert.Contains(configured, c => c.Name == "Azure Document Intelligence" && c.RequiresNetwork);
    }

    private static MarkItDownService CreateService() => ConverterCatalog.Create(new ConverterSettings()).Service;

    // ---- Document builders ----

    private static byte[] BuildEpub() => BuildZip(
        ("mimetype", "application/epub+zip"u8.ToArray()),
        ("META-INF/container.xml", Encoding.UTF8.GetBytes(
            """<container xmlns="urn:oasis:names:tc:opendocument:xmlns:container" version="1.0"><rootfiles><rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/></rootfiles></container>""")),
        ("OEBPS/content.opf", Encoding.UTF8.GetBytes(
            """
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/"><dc:title>The Test Book</dc:title><dc:creator>Ada Lovelace</dc:creator><dc:language>en</dc:language></metadata>
              <manifest><item id="b" href="text/two.xhtml" media-type="application/xhtml+xml"/><item id="a" href="text/one%20first.xhtml" media-type="application/xhtml+xml"/></manifest>
              <spine><itemref idref="a"/><itemref idref="b"/></spine>
            </package>
            """)),
        ("OEBPS/text/one first.xhtml", Encoding.UTF8.GetBytes("<html><body><h1>Chapter One</h1><p>Opening.</p></body></html>")),
        ("OEBPS/text/two.xhtml", Encoding.UTF8.GetBytes("<html><body><h1>Chapter Two</h1><p>Closing.</p></body></html>")));

    private static byte[] BuildZip(params (string Name, byte[] Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var entryStream = entry.Open();
                entryStream.Write(content);
            }
        }

        return stream.ToArray();
    }

    private sealed class FakeOcr(string text) : IOcrEngine
    {
        public Task<string?> RecognizeAsync(byte[] image, string? mimeType, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(image.Length > 0 ? text : null);
    }
}

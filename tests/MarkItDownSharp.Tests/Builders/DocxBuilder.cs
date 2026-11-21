using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace MarkItDownSharp.Tests.Builders;

internal static class DocxBuilder
{
    public static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    public static byte[] Build(bool includeImage = false)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            document.PackageProperties.Title = "Quarterly report";
            var main = document.AddMainDocumentPart();

            var styles = main.AddNewPart<StyleDefinitionsPart>();
            styles.Styles = new Styles(
                new Style(new StyleName { Val = "heading 1" }) { Type = StyleValues.Paragraph, StyleId = "Heading1" },
                new Style(new StyleName { Val = "heading 2" }) { Type = StyleValues.Paragraph, StyleId = "Heading2" });

            var numbering = main.AddNewPart<NumberingDefinitionsPart>();
            numbering.Numbering = new Numbering(
                new AbstractNum(new Level(new NumberingFormat { Val = NumberFormatValues.Bullet }) { LevelIndex = 0 }) { AbstractNumberId = 1 },
                new AbstractNum(new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }) { LevelIndex = 0 }) { AbstractNumberId = 2 },
                new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 },
                new NumberingInstance(new AbstractNumId { Val = 2 }) { NumberID = 2 });

            var link = main.AddHyperlinkRelationship(new Uri("https://example.com/"), true);

            var body = new Body(
                StyledParagraph("Heading1", "Heading One"),
                StyledParagraph("Heading2", "Heading Two"),
                new Paragraph(
                    new Run(new Text("Plain ") { Space = SpaceProcessingModeValues.Preserve }),
                    new Run(new RunProperties(new Bold()), new Text("bold")),
                    new Run(new Text(" and ") { Space = SpaceProcessingModeValues.Preserve }),
                    new Run(new RunProperties(new Italic()), new Text("italic")),
                    new Run(new Text(" text") { Space = SpaceProcessingModeValues.Preserve })),
                new Paragraph(new Hyperlink(new Run(new Text("Example"))) { Id = link.Id }),
                ListParagraph(1, "bullet one"),
                ListParagraph(2, "step one"),
                new Table(
                    new TableRow(Cell("Name"), Cell("Qty")),
                    new TableRow(Cell("Apples"), Cell("3"))));

            if (includeImage)
            {
                var image = main.AddImagePart(ImagePartType.Png);
                using var imageStream = new MemoryStream(OnePixelPng);
                image.FeedData(imageStream);
            }

            main.Document = new Document(body);
        }

        return stream.ToArray();
    }

    private static Paragraph StyledParagraph(string style, string text) =>
        new(new ParagraphProperties(new ParagraphStyleId { Val = style }), new Run(new Text(text)));

    private static Paragraph ListParagraph(int numberId, string text) =>
        new(
            new ParagraphProperties(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = numberId })),
            new Run(new Text(text)));

    private static TableCell Cell(string text) => new(new Paragraph(new Run(new Text(text))));
}

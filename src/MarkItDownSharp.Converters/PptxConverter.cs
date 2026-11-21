using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using MarkItDownSharp.Converters.Internal;
using MarkItDownSharp.Core;
using A = DocumentFormat.OpenXml.Drawing;

namespace MarkItDownSharp.Converters;

public sealed class PptxConverter : IMarkItDownConverter, IExtensionProvider
{
    private readonly IOcrEngine? _ocr;

    public PptxConverter(IOcrEngine? ocr = null)
    {
        _ocr = ocr;
    }

    public int Priority => 0;
    public string Name => "PowerPoint (PPTX)";
    public string Description => "PowerPoint presentations: one section per slide with titles, text, tables, notes and optional image OCR.";
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".pptx" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        var output = new StringBuilder();
        string? title;
        using (var presentation = PresentationDocument.Open(stream, false))
        {
            var presentationPart = presentation.PresentationPart ?? throw new InvalidDataException("The file has no presentation part.");
            title = presentation.PackageProperties.Title;
            var slideNumber = 0;
            foreach (var slideId in presentationPart.Presentation?.SlideIdList?.Elements<SlideId>() ?? Enumerable.Empty<SlideId>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (slideId.RelationshipId?.Value is not { } relationshipId ||
                    presentationPart.GetPartById(relationshipId) is not SlidePart slidePart)
                {
                    continue;
                }

                slideNumber++;
                AppendSlide(output, slidePart, slideNumber);
            }
        }

        var ocr = await ImageOcr.FromPackageAsync(stream, _ocr, cancellationToken);
        if (ocr.Length > 0)
        {
            output.Append(ocr).Append("\n\n");
        }

        return new ConversionResult(
            output.ToString().Trim(),
            string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(metadata.FileName) : title);
    }

    private static void AppendSlide(StringBuilder output, SlidePart slidePart, int number)
    {
        var tree = slidePart.Slide?.CommonSlideData?.ShapeTree;
        var slideTitle = tree?.Elements<Shape>().FirstOrDefault(IsTitle) is { } titleShape ? ParagraphsOf(titleShape).FirstOrDefault().Text : null;
        output.Append("<!-- Slide number: ").Append(number).Append(" -->\n");
        output.Append("## ").Append(string.IsNullOrWhiteSpace(slideTitle) ? $"Slide {number}" : slideTitle.Trim()).Append("\n\n");

        foreach (var element in tree?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
        {
            AppendShape(output, element);
        }

        var notes = slidePart.NotesSlidePart?.NotesSlide?.Descendants<Shape>()
            .Where(s => s.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape?.Type?.Value == PlaceholderValues.Body)
            .SelectMany(ParagraphsOf)
            .Select(p => p.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        if (notes is { Count: > 0 })
        {
            output.Append("### Notes\n\n").Append(string.Join("\n\n", notes)).Append("\n\n");
        }
    }

    private static void AppendShape(StringBuilder output, OpenXmlElement element)
    {
        switch (element)
        {
            case Shape shape when !IsTitle(shape):
                foreach (var (text, level) in ParagraphsOf(shape))
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    output.Append(new string(' ', level * 2)).Append(level > 0 || IsBody(shape) ? "- " : string.Empty).Append(text.Trim()).Append("\n\n");
                }

                break;
            case GraphicFrame frame:
                var table = frame.Descendants<A.Table>().FirstOrDefault();
                if (table is not null)
                {
                    var rows = table.Elements<A.TableRow>()
                        .Select(row => (IReadOnlyList<string>)row.Elements<A.TableCell>().Select(c => string.Join(" ", c.TextBody?.Elements<A.Paragraph>().Select(ParagraphText) ?? [])).ToArray())
                        .ToList();
                    output.Append(MarkdownTable.Build(rows)).Append("\n\n");
                }

                break;
            case Picture picture:
                var description = picture.NonVisualPictureProperties?.NonVisualDrawingProperties?.Description?.Value;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    output.Append("![").Append(description.Replace("\n", " ").Trim()).Append("]()\n\n");
                }

                break;
            case GroupShape group:
                foreach (var child in group.ChildElements)
                {
                    AppendShape(output, child);
                }

                break;
        }
    }

    private static bool IsTitle(Shape shape)
    {
        var type = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape?.Type?.Value;
        return type == PlaceholderValues.Title || type == PlaceholderValues.CenteredTitle;
    }

    private static bool IsBody(Shape shape)
    {
        var placeholder = shape.NonVisualShapeProperties?.ApplicationNonVisualDrawingProperties?.PlaceholderShape;
        return placeholder is not null && (placeholder.Type is null || placeholder.Type.Value == PlaceholderValues.Body);
    }

    private static IEnumerable<(string Text, int Level)> ParagraphsOf(Shape shape) =>
        shape.TextBody?.Elements<A.Paragraph>().Select(p => (ParagraphText(p), p.ParagraphProperties?.Level?.Value ?? 0))
        ?? Enumerable.Empty<(string, int)>();

    private static string ParagraphText(A.Paragraph paragraph)
    {
        var builder = new StringBuilder();
        foreach (var child in paragraph.ChildElements)
        {
            switch (child)
            {
                case A.Run run:
                    builder.Append(run.Text?.Text);
                    break;
                case A.Break:
                    builder.Append(' ');
                    break;
                case A.Field field:
                    builder.Append(field.Text?.Text);
                    break;
            }
        }

        return builder.ToString();
    }
}

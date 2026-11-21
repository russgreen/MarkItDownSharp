using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace MarkItDownSharp.Tests.Builders;

internal static class PptxBuilder
{
    public static byte[] Build()
    {
        using var stream = new MemoryStream();
        using (var document = PresentationDocument.Create(stream, PresentationDocumentType.Presentation))
        {
            var presentationPart = document.AddPresentationPart();
            presentationPart.Presentation = new DocumentFormat.OpenXml.Presentation.Presentation();
            var slidePart = presentationPart.AddNewPart<SlidePart>();

            slidePart.Slide = new Slide(new CommonSlideData(new ShapeTree(
                GroupHeader(),
                new GroupShapeProperties(),
                Placeholder(2, "Title", PlaceholderValues.Title, "Welcome"),
                Placeholder(3, "Body", PlaceholderValues.Body, "First point"))));

            var notesPart = slidePart.AddNewPart<NotesSlidePart>();
            notesPart.NotesSlide = new NotesSlide(new CommonSlideData(new ShapeTree(
                GroupHeader(),
                new GroupShapeProperties(),
                Placeholder(2, "Notes", PlaceholderValues.Body, "Speaker says hello"))));

            presentationPart.Presentation.SlideIdList = new SlideIdList(
                new SlideId { Id = 256, RelationshipId = presentationPart.GetIdOfPart(slidePart) });
        }

        return stream.ToArray();
    }

    private static NonVisualGroupShapeProperties GroupHeader() =>
        new(
            new NonVisualDrawingProperties { Id = 1, Name = string.Empty },
            new NonVisualGroupShapeDrawingProperties(),
            new ApplicationNonVisualDrawingProperties());

    private static Shape Placeholder(uint id, string name, PlaceholderValues type, string text) =>
        new(
            new NonVisualShapeProperties(
                new NonVisualDrawingProperties { Id = id, Name = name },
                new NonVisualShapeDrawingProperties(),
                new ApplicationNonVisualDrawingProperties(new PlaceholderShape { Type = type, Index = id })),
            new ShapeProperties(),
            new TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph(new A.Run(new A.Text(text)))));
}

using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using MarkItDownSharp.Converters.Internal;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Converters;

public sealed class DocxConverter : IMarkItDownConverter, IExtensionProvider
{
    private readonly IOcrEngine? _ocr;

    public DocxConverter(IOcrEngine? ocr = null)
    {
        _ocr = ocr;
    }

    public int Priority => 0;
    public string Name => "Word (DOCX)";
    public string Description => "Word documents: headings, lists, tables, bold/italic, hyperlinks and optional image OCR.";
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".docx" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        string markdown;
        string? title;
        using (var document = WordprocessingDocument.Open(stream, false))
        {
            var main = document.MainDocumentPart ?? throw new InvalidDataException("The file has no main document part.");
            var context = new Context(main);
            var blocks = new List<string>();
            var body = main.Document?.Body;
            if (body is not null)
            {
                foreach (var element in body.ChildElements)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AppendBlock(element, context, blocks);
                }
            }

            markdown = string.Join("\n\n", blocks.Where(x => x.Length > 0));
            title = document.PackageProperties.Title;
        }

        var ocr = await ImageOcr.FromPackageAsync(stream, _ocr, cancellationToken);
        if (ocr.Length > 0)
        {
            markdown += "\n\n" + ocr;
        }

        return new ConversionResult(
            markdown.Trim(),
            string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(metadata.FileName) : title);
    }

    private static void AppendBlock(OpenXmlElement element, Context context, List<string> blocks)
    {
        switch (element)
        {
            case Paragraph paragraph:
                var text = ConvertParagraph(paragraph, context);
                if (text.Length > 0)
                {
                    blocks.Add(text);
                }

                break;
            case Table table:
                blocks.Add(ConvertTable(table, context));
                break;
            case SdtBlock sdt:
                foreach (var child in sdt.SdtContentBlock?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
                {
                    AppendBlock(child, context, blocks);
                }

                break;
        }
    }

    private static string ConvertTable(Table table, Context context)
    {
        var rows = table.Elements<TableRow>()
            .Select(row => (IReadOnlyList<string>)row.Elements<TableCell>()
                .Select(cell => string.Join(" ", cell.Elements<Paragraph>().Select(p => ConvertInlines(p, context)).Where(x => x.Length > 0)))
                .ToArray())
            .ToList();
        return MarkdownTable.Build(rows);
    }

    private static string ConvertParagraph(Paragraph paragraph, Context context)
    {
        var inline = ConvertInlines(paragraph, context);
        if (inline.Length == 0)
        {
            return string.Empty;
        }

        var properties = paragraph.ParagraphProperties;
        var styleId = properties?.ParagraphStyleId?.Val?.Value;
        var heading = HeadingLevel(styleId, context);
        if (heading > 0)
        {
            return new string('#', heading) + " " + inline.Replace("**", string.Empty);
        }

        var numbering = properties?.NumberingProperties;
        if (numbering?.NumberingId?.Val?.Value is { } numberId && numberId != 0)
        {
            var level = numbering.NumberingLevelReference?.Val?.Value ?? 0;
            var marker = context.IsBullet(numberId, level) ? "-" : "1.";
            return new string(' ', level * 2) + marker + " " + inline;
        }

        if (string.Equals(styleId, "Quote", StringComparison.OrdinalIgnoreCase))
        {
            return "> " + inline;
        }

        return inline;
    }

    private static int HeadingLevel(string? styleId, Context context)
    {
        if (styleId is null)
        {
            return 0;
        }

        var name = context.StyleName(styleId) ?? styleId;
        if (name.Equals("Title", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        foreach (var prefix in new[] { "heading ", "heading" })
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(name[prefix.Length..].Trim(), out var level) && level is >= 1 and <= 6)
            {
                return level;
            }
        }

        return 0;
    }

    private static string ConvertInlines(OpenXmlElement parent, Context context)
    {
        var segments = new List<Segment>();
        CollectSegments(parent, context, null, segments);

        // Merge neighbouring segments that share formatting so "a" + "b" in bold becomes one **ab**.
        var merged = new List<Segment>();
        foreach (var segment in segments)
        {
            if (merged.Count > 0 && merged[^1].CanMerge(segment))
            {
                merged[^1] = merged[^1] with { Text = merged[^1].Text + segment.Text };
            }
            else
            {
                merged.Add(segment);
            }
        }

        var builder = new StringBuilder();
        foreach (var segment in merged)
        {
            builder.Append(segment.Render());
        }

        return builder.ToString().Trim();
    }

    private static void CollectSegments(OpenXmlElement parent, Context context, string? link, List<Segment> segments)
    {
        foreach (var child in parent.ChildElements)
        {
            switch (child)
            {
                case Run run:
                    var text = RunText(run);
                    if (text.Length > 0)
                    {
                        var properties = run.RunProperties;
                        segments.Add(new Segment(
                            text,
                            IsOn(properties?.Bold),
                            IsOn(properties?.Italic),
                            link));
                    }

                    break;
                case Hyperlink hyperlink:
                    string? target = null;
                    if (hyperlink.Id?.Value is { } relationshipId)
                    {
                        target = context.Main.HyperlinkRelationships.FirstOrDefault(r => r.Id == relationshipId)?.Uri.ToString();
                    }
                    else if (hyperlink.Anchor?.Value is { } anchor)
                    {
                        target = "#" + anchor;
                    }

                    CollectSegments(hyperlink, context, target, segments);
                    break;
                case SdtRun or SimpleField:
                    CollectSegments(child, context, link, segments);
                    break;
            }
        }
    }

    private static string RunText(Run run)
    {
        var builder = new StringBuilder();
        foreach (var child in run.ChildElements)
        {
            switch (child)
            {
                case Text text:
                    builder.Append(text.Text);
                    break;
                case TabChar:
                    builder.Append('\t');
                    break;
                case Break:
                    builder.Append(' ');
                    break;
                case NoBreakHyphen:
                    builder.Append('-');
                    break;
            }
        }

        return builder.ToString();
    }

    private static bool IsOn(OnOffType? value) => value is not null && (value.Val is null || value.Val.Value);

    private sealed record Segment(string Text, bool Bold, bool Italic, string? Link)
    {
        public bool CanMerge(Segment other) => Bold == other.Bold && Italic == other.Italic && Link == other.Link;

        public string Render()
        {
            var text = Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            // Markers must hug the text, so whitespace stays outside them.
            var leading = text[..(text.Length - text.TrimStart().Length)];
            var trailing = text[text.TrimEnd().Length..];
            var core = text.Trim();
            if (Italic)
            {
                core = $"*{core}*";
            }

            if (Bold)
            {
                core = $"**{core}**";
            }

            if (Link is not null)
            {
                core = $"[{core}]({Link})";
            }

            return leading + core + trailing;
        }
    }

    private sealed class Context
    {
        private readonly Dictionary<string, string> _styleNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<int, int> _numberToAbstract = new();
        private readonly Dictionary<(int Abstract, int Level), string> _formats = new();

        public Context(MainDocumentPart main)
        {
            Main = main;
            foreach (var style in main.StyleDefinitionsPart?.Styles?.Elements<Style>() ?? Enumerable.Empty<Style>())
            {
                if (style.StyleId?.Value is { } id && style.StyleName?.Val?.Value is { } name)
                {
                    _styleNames[id] = name;
                }
            }

            var numbering = main.NumberingDefinitionsPart?.Numbering;
            if (numbering is null)
            {
                return;
            }

            foreach (var abstractNumber in numbering.Elements<AbstractNum>())
            {
                var abstractId = abstractNumber.AbstractNumberId?.Value ?? -1;
                foreach (var level in abstractNumber.Elements<Level>())
                {
                    _formats[(abstractId, level.LevelIndex?.Value ?? 0)] = level.NumberingFormat?.Val?.InnerText ?? "decimal";
                }
            }

            foreach (var instance in numbering.Elements<NumberingInstance>())
            {
                if (instance.NumberID?.Value is { } id && instance.AbstractNumId?.Val?.Value is { } abstractId)
                {
                    _numberToAbstract[id] = abstractId;
                }
            }
        }

        public MainDocumentPart Main { get; }

        public string? StyleName(string styleId) => _styleNames.GetValueOrDefault(styleId);

        public bool IsBullet(int numberId, int level) =>
            _numberToAbstract.TryGetValue(numberId, out var abstractId) &&
            _formats.TryGetValue((abstractId, level), out var format) &&
            format == "bullet";
    }
}

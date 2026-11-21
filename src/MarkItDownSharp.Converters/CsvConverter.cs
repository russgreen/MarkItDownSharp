using System.Text;
using MarkItDownSharp.Converters.Internal;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Converters;

public sealed class CsvConverter : IMarkItDownConverter, IExtensionProvider
{
    public int Priority => 0;
    public string Name => "Delimited text";
    public string Description => "CSV and TSV files as Markdown tables, including quoted cells that span lines.";
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".csv", ".tsv" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        var text = await TextDecoding.ReadAllTextAsync(stream, cancellationToken);
        var separator = metadata.Extension?.Equals(".tsv", StringComparison.OrdinalIgnoreCase) == true ? '\t' : ',';
        var rows = ParseRecords(text, separator);
        return new ConversionResult(MarkdownTable.Build(rows), Path.GetFileNameWithoutExtension(metadata.FileName));
    }

    private static List<IReadOnlyList<string>> ParseRecords(string text, char separator)
    {
        var rows = new List<IReadOnlyList<string>>();
        var cells = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var recordHasContent = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                recordHasContent = true;
                if (inQuotes && i + 1 < text.Length && text[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == separator && !inQuotes)
            {
                cells.Add(current.ToString());
                current.Clear();
                recordHasContent = true;
            }
            else if (c == '\n' && !inQuotes)
            {
                EndRecord();
            }
            else
            {
                current.Append(c);
                recordHasContent = true;
            }
        }

        EndRecord();
        return rows;

        void EndRecord()
        {
            if (recordHasContent || cells.Count > 0)
            {
                cells.Add(current.ToString());
                rows.Add(cells.ToArray());
            }

            cells = [];
            current.Clear();
            recordHasContent = false;
        }
    }
}

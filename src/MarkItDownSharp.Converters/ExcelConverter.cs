using System.Globalization;
using System.Text;
using ExcelDataReader;
using MarkItDownSharp.Converters.Internal;
using MarkItDownSharp.Core;

namespace MarkItDownSharp.Converters;

public sealed class ExcelConverter : IMarkItDownConverter, IExtensionProvider
{
    private readonly IOcrEngine? _ocr;

    static ExcelConverter()
    {
        // ExcelDataReader needs the legacy code pages to decode BIFF (.xls) strings.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public ExcelConverter(IOcrEngine? ocr = null)
    {
        _ocr = ocr;
    }

    public int Priority => 0;
    public string Name => "Excel (XLSX, XLS)";
    public string Description => "Excel workbooks: every worksheet becomes a Markdown table under a sheet heading.";
    public IReadOnlyCollection<string> Extensions { get; } = new[] { ".xlsx", ".xlsm", ".xls" };

    public bool CanConvert(StreamMetadata metadata) =>
        Extensions.Contains(metadata.Extension, StringComparer.OrdinalIgnoreCase);

    public async Task<ConversionResult> ConvertAsync(Stream stream, StreamMetadata metadata, CancellationToken cancellationToken = default)
    {
        var output = new StringBuilder();
        using (var reader = ExcelReaderFactory.CreateReader(stream, new ExcelReaderConfiguration { LeaveOpen = true }))
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rows = new List<IReadOnlyList<string>>();
                while (reader.Read())
                {
                    var cells = new string[reader.FieldCount];
                    for (var i = 0; i < cells.Length; i++)
                    {
                        cells[i] = Format(reader.GetValue(i));
                    }

                    rows.Add(cells);
                }

                var trimmed = Trim(rows);
                output.Append("## ").Append(reader.Name).Append("\n\n");
                output.Append(trimmed.Count == 0 ? "*Empty sheet*" : MarkdownTable.Build(trimmed)).Append("\n\n");
            }
            while (reader.NextResult());
        }

        if (metadata.Extension?.Equals(".xls", StringComparison.OrdinalIgnoreCase) == false)
        {
            var ocr = await ImageOcr.FromPackageAsync(stream, _ocr, cancellationToken);
            if (ocr.Length > 0)
            {
                output.Append(ocr).Append("\n\n");
            }
        }

        return new ConversionResult(output.ToString().Trim(), Path.GetFileNameWithoutExtension(metadata.FileName));
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTime date => date.TimeOfDay == TimeSpan.Zero
            ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : date.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        double number => number.ToString("G15", CultureInfo.InvariantCulture),
        bool flag => flag ? "TRUE" : "FALSE",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? string.Empty
    };

    // Worksheets often carry formatting beyond the data; drop empty trailing rows and columns.
    private static List<IReadOnlyList<string>> Trim(List<IReadOnlyList<string>> rows)
    {
        while (rows.Count > 0 && rows[^1].All(string.IsNullOrEmpty))
        {
            rows.RemoveAt(rows.Count - 1);
        }

        if (rows.Count == 0)
        {
            return rows;
        }

        var width = rows.Max(r => LastNonEmpty(r) + 1);
        return rows.Select(r => (IReadOnlyList<string>)r.Take(width).ToArray()).ToList();
    }

    private static int LastNonEmpty(IReadOnlyList<string> row)
    {
        for (var i = row.Count - 1; i >= 0; i--)
        {
            if (!string.IsNullOrEmpty(row[i]))
            {
                return i;
            }
        }

        return -1;
    }
}

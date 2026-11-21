using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace MarkItDownSharp.Tests.Builders;

internal static class XlsxBuilder
{
    public static byte[] Build()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var sheets = workbook.Workbook.AppendChild(new Sheets());

            AddSheet(workbook, sheets, 1, "Sales", [["Region", "Total"], ["North", "12.5"]], numericColumns: [1]);
            AddSheet(workbook, sheets, 2, "Notes", [["remember", "the milk"]], numericColumns: []);
        }

        return stream.ToArray();
    }

    private static void AddSheet(WorkbookPart workbook, Sheets sheets, uint id, string name, string[][] rows, int[] numericColumns)
    {
        var part = workbook.AddNewPart<WorksheetPart>();
        var data = new SheetData();
        foreach (var row in rows)
        {
            var xmlRow = new Row();
            for (var i = 0; i < row.Length; i++)
            {
                var isNumber = numericColumns.Contains(i) && double.TryParse(row[i], CultureInfo.InvariantCulture, out _);
                xmlRow.Append(isNumber
                    ? new Cell { DataType = CellValues.Number, CellValue = new CellValue(row[i]) }
                    : new Cell { DataType = CellValues.InlineString, InlineString = new InlineString(new Text(row[i])) });
            }

            data.Append(xmlRow);
        }

        part.Worksheet = new Worksheet(data);
        sheets.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = id, Name = name });
    }
}

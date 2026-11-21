using System.Text;

namespace MarkItDownSharp.Converters.Internal;

/// <summary>
/// Builds GitHub-flavoured Markdown pipe tables.
/// </summary>
public static class MarkdownTable
{
    public static string Build(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var width = rows.Max(r => r.Count);
        if (width == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var i = 0; i < rows.Count; i++)
        {
            AppendRow(builder, rows[i], width);
            if (i == 0)
            {
                builder.Append("| ").AppendJoin(" | ", Enumerable.Repeat("---", width)).Append(" |\n");
            }
        }

        return builder.ToString().TrimEnd();
    }

    public static string EscapeCell(string? value) =>
        (value ?? string.Empty)
            .Replace("|", "\\|")
            .Replace("\r\n", " ")
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> row, int width)
    {
        builder.Append("| ");
        for (var i = 0; i < width; i++)
        {
            if (i > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(EscapeCell(i < row.Count ? row[i] : string.Empty));
        }

        builder.Append(" |\n");
    }
}

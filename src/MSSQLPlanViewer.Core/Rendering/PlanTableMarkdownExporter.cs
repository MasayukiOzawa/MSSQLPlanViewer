using System.Text;

namespace MSSQLPlanViewer.Core.Rendering;

/// <summary>
/// Exports projected plan table rows as a GitHub Flavored Markdown table.
/// </summary>
public static class PlanTableMarkdownExporter
{
    public static string ToMarkdown(IReadOnlyList<PlanTableRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var columns = PlanTableColumns.All;
        var builder = new StringBuilder();
        AppendRecord(builder, PlanTableColumns.Headers);
        AppendSeparator(builder, columns.Count);

        var fields = new string[columns.Count];
        foreach (var row in rows)
        {
            for (var index = 0; index < columns.Count; index++)
            {
                fields[index] = columns[index].GetValue(row);
            }

            AppendRecord(builder, fields);
        }

        return builder.ToString();
    }

    private static void AppendRecord(StringBuilder builder, IReadOnlyList<string> fields)
    {
        builder.Append("| ");
        for (var index = 0; index < fields.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(EscapeCell(fields[index]));
        }

        builder.Append(" |\r\n");
    }

    private static void AppendSeparator(StringBuilder builder, int columnCount)
    {
        builder.Append('|');
        for (var index = 0; index < columnCount; index++)
        {
            builder.Append(" --- |");
        }

        builder.Append("\r\n");
    }

    /// <summary>
    /// Escapes characters that would break a Markdown table cell: pipes are escaped and
    /// line breaks are converted to the HTML break entity so each row stays on one line.
    /// </summary>
    private static string EscapeCell(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace("\n", "<br>", StringComparison.Ordinal)
            .Replace("\r", "<br>", StringComparison.Ordinal);
    }
}

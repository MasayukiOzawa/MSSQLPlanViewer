using System.Text;

namespace MSSQLPlanViewer.Core.Rendering;

/// <summary>
/// Exports projected plan table rows as RFC 4180 compliant CSV text.
/// </summary>
public static class PlanTableCsvExporter
{
    public static string ToCsv(IReadOnlyList<PlanTableRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var columns = PlanTableColumns.All;
        var builder = new StringBuilder();
        AppendRecord(builder, PlanTableColumns.Headers);

        var fields = new string[columns.Count];
        foreach (var row in rows)
        {
            for (var index = 0; index < columns.Count; index++)
            {
                var value = columns[index].GetValue(row);
                fields[index] = columns[index].IsText ? NeutralizeFormula(value) : value;
            }

            AppendRecord(builder, fields);
        }

        return builder.ToString();
    }

    private static readonly char[] FormulaTriggerCharacters = ['=', '+', '-', '@', '\t', '\r'];

    /// <summary>
    /// Prefixes text fields that begin with a spreadsheet formula trigger so they are not
    /// interpreted as formulas when the CSV is opened in Excel or Google Sheets (CSV injection).
    /// </summary>
    private static string NeutralizeFormula(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return Array.IndexOf(FormulaTriggerCharacters, value[0]) >= 0 ? "'" + value : value;
    }

    private static void AppendRecord(StringBuilder builder, IReadOnlyList<string> fields)
    {
        for (var index = 0; index < fields.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(EscapeField(fields[index]));
        }

        builder.Append("\r\n");
    }

    private static string EscapeField(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var requiresQuoting =
            value.Contains('"', StringComparison.Ordinal) ||
            value.Contains(',', StringComparison.Ordinal) ||
            value.Contains('\n', StringComparison.Ordinal) ||
            value.Contains('\r', StringComparison.Ordinal);

        if (!requiresQuoting)
        {
            return value;
        }

        return string.Concat("\"", value.Replace("\"", "\"\"", StringComparison.Ordinal), "\"");
    }
}

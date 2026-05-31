using System.Globalization;
using System.Text;

namespace MSSQLPlanViewer.Core.Rendering;

/// <summary>
/// Exports projected plan table rows as RFC 4180 compliant CSV text.
/// </summary>
public static class PlanTableCsvExporter
{
    private static readonly string[] HeaderColumns =
    [
        "NodeId",
        "ParentNodeId",
        "Depth",
        "PhysicalOp",
        "LogicalOp",
        "ObjectName",
        "CostRatio",
        "EstimatedSubtreeCost",
        "EstimatedCpuCost",
        "EstimatedIoCost",
        "EstimatedRows",
        "AverageRowSize",
        "ActualRows",
        "ActualExecutions",
        "ActualLogicalReads",
        "ActualPhysicalReads",
        "ActualCpuMs",
        "ActualElapsedMs",
        "WarningCount",
        "IsParallel",
        "Summary"
    ];

    public static string ToCsv(IReadOnlyList<PlanTableRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        AppendRecord(builder, HeaderColumns);

        foreach (var row in rows)
        {
            AppendRecord(builder, BuildFields(row));
        }

        return builder.ToString();
    }

    private static string[] BuildFields(PlanTableRow row) =>
    [
        NeutralizeFormula(row.NodeId),
        NeutralizeFormula(row.ParentNodeId ?? string.Empty),
        FormatInt(row.Depth),
        NeutralizeFormula(row.PhysicalOp),
        NeutralizeFormula(row.LogicalOp),
        NeutralizeFormula(row.ObjectName),
        FormatDecimal(row.CostRatio),
        FormatNullableDecimal(row.EstimatedSubtreeCost),
        FormatNullableDecimal(row.EstimatedCpuCost),
        FormatNullableDecimal(row.EstimatedIoCost),
        FormatNullableDouble(row.EstimatedRows),
        FormatNullableDouble(row.AverageRowSize),
        FormatNullableDouble(row.ActualRows),
        FormatNullableDouble(row.ActualExecutions),
        FormatNullableDouble(row.ActualLogicalReads),
        FormatNullableDouble(row.ActualPhysicalReads),
        FormatNullableDouble(row.ActualCpuMs),
        FormatNullableDouble(row.ActualElapsedMs),
        FormatInt(row.WarningCount),
        row.IsParallel ? "true" : "false",
        NeutralizeFormula(row.Summary)
    ];

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

    private static string FormatInt(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string FormatDecimal(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string FormatNullableDecimal(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatNullableDouble(double? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}

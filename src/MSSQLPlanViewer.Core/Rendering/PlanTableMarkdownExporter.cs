using System.Globalization;
using System.Text;

namespace MSSQLPlanViewer.Core.Rendering;

/// <summary>
/// Exports projected plan table rows as a GitHub Flavored Markdown table.
/// </summary>
public static class PlanTableMarkdownExporter
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

    public static string ToMarkdown(IReadOnlyList<PlanTableRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var builder = new StringBuilder();
        AppendRecord(builder, HeaderColumns);
        AppendSeparator(builder, HeaderColumns.Length);

        foreach (var row in rows)
        {
            AppendRecord(builder, BuildFields(row));
        }

        return builder.ToString();
    }

    private static string[] BuildFields(PlanTableRow row) =>
    [
        row.NodeId,
        row.ParentNodeId ?? string.Empty,
        FormatInt(row.Depth),
        row.PhysicalOp,
        row.LogicalOp,
        row.ObjectName,
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
        row.Summary
    ];

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

    private static string FormatInt(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string FormatDecimal(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string FormatNullableDecimal(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatNullableDouble(double? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}

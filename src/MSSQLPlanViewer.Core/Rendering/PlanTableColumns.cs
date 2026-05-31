using System.Globalization;

namespace MSSQLPlanViewer.Core.Rendering;

/// <summary>
/// Defines a single plan table export column: its header, a selector that produces the
/// formatted string value, and whether the value is free-form text (and therefore subject
/// to spreadsheet formula neutralization when exported to CSV).
/// </summary>
internal sealed record PlanTableColumn(string Header, Func<PlanTableRow, string> GetValue, bool IsText);

/// <summary>
/// Shared column definitions and number formatting used by the CSV and Markdown exporters,
/// keeping both exporters consistent in column order, headers, and culture-invariant formatting.
/// </summary>
internal static class PlanTableColumns
{
    public static IReadOnlyList<PlanTableColumn> All { get; } =
    [
        Text("NodeId", row => row.NodeId),
        Text("ParentNodeId", row => row.ParentNodeId ?? string.Empty),
        Value("Depth", row => FormatInt(row.Depth)),
        Text("PhysicalOp", row => row.PhysicalOp),
        Text("LogicalOp", row => row.LogicalOp),
        Text("ObjectName", row => row.ObjectName),
        Value("CostRatio", row => FormatDecimal(row.CostRatio)),
        Value("EstimatedSubtreeCost", row => FormatNullableDecimal(row.EstimatedSubtreeCost)),
        Value("EstimatedCpuCost", row => FormatNullableDecimal(row.EstimatedCpuCost)),
        Value("EstimatedIoCost", row => FormatNullableDecimal(row.EstimatedIoCost)),
        Value("EstimatedRows", row => FormatNullableDouble(row.EstimatedRows)),
        Value("AverageRowSize", row => FormatNullableDouble(row.AverageRowSize)),
        Value("ActualRows", row => FormatNullableDouble(row.ActualRows)),
        Value("ActualExecutions", row => FormatNullableDouble(row.ActualExecutions)),
        Value("ActualLogicalReads", row => FormatNullableDouble(row.ActualLogicalReads)),
        Value("ActualPhysicalReads", row => FormatNullableDouble(row.ActualPhysicalReads)),
        Value("ActualCpuMs", row => FormatNullableDouble(row.ActualCpuMs)),
        Value("ActualElapsedMs", row => FormatNullableDouble(row.ActualElapsedMs)),
        Value("WarningCount", row => FormatInt(row.WarningCount)),
        Value("IsParallel", row => row.IsParallel ? "true" : "false"),
        Text("Summary", row => row.Summary)
    ];

    public static IReadOnlyList<string> Headers { get; } =
        All.Select(column => column.Header).ToArray();

    private static PlanTableColumn Text(string header, Func<PlanTableRow, string> getValue) =>
        new(header, getValue, IsText: true);

    private static PlanTableColumn Value(string header, Func<PlanTableRow, string> getValue) =>
        new(header, getValue, IsText: false);

    private static string FormatInt(int value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string FormatDecimal(decimal value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string FormatNullableDecimal(decimal? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    private static string FormatNullableDouble(double? value) =>
        value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
}

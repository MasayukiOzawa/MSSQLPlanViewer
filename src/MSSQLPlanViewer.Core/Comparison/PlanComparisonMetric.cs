namespace MSSQLPlanViewer.Core.Comparison;

/// <summary>
/// A single high-level metric compared between two execution plans.
/// </summary>
/// <param name="MetricName">Display name of the metric.</param>
/// <param name="ValueA">Metric value for plan A, or null when not available.</param>
/// <param name="ValueB">Metric value for plan B, or null when not available.</param>
/// <param name="Delta">ValueB - ValueA when both values are available; otherwise null.</param>
/// <param name="DeltaPercent">Percentage change from A to B when A is non-zero; otherwise null.</param>
/// <param name="IsInteger">True when the metric is a whole-number count (rendered without decimals).</param>
public sealed record PlanComparisonMetric(
    string MetricName,
    double? ValueA,
    double? ValueB,
    double? Delta,
    double? DeltaPercent,
    bool IsInteger);

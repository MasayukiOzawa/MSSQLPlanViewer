namespace MSSQLPlanViewer.Core.Comparison;

/// <summary>
/// Result of comparing two execution plans across a set of high-level metrics.
/// </summary>
public sealed record PlanComparisonResult(IReadOnlyList<PlanComparisonMetric> Metrics);

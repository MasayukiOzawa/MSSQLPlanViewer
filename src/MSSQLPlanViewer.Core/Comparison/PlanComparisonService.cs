using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Comparison;

/// <summary>
/// Default implementation that compares two plans using high-level summary metrics only.
/// No node-level or visual differencing is performed.
/// </summary>
public sealed class PlanComparisonService : IPlanComparisonService
{
    public PlanComparisonResult Compare(ShowplanDocument planA, ShowplanDocument planB)
    {
        ArgumentNullException.ThrowIfNull(planA);
        ArgumentNullException.ThrowIfNull(planB);

        var metrics = new List<PlanComparisonMetric>
        {
            BuildMetric("Statements", planA.Statements.Count, planB.Statements.Count, isInteger: true),
            BuildMetric("Operators", planA.TotalNodeCount, planB.TotalNodeCount, isInteger: true),
            BuildMetric("Warnings", planA.TotalWarningCount, planB.TotalWarningCount, isInteger: true),
            BuildMetric(
                "Sum of statement estimated subtree costs",
                SumEstimatedSubtreeCost(planA),
                SumEstimatedSubtreeCost(planB),
                isInteger: false),
            BuildMetric(
                "Sum of estimated rows",
                SumEstimatedRows(planA),
                SumEstimatedRows(planB),
                isInteger: false)
        };

        return new PlanComparisonResult(metrics);
    }

    private static double? SumEstimatedSubtreeCost(ShowplanDocument plan)
    {
        var values = plan.Statements
            .Select(statement => statement.Summary.EstimatedSubtreeCost)
            .Where(value => value.HasValue)
            .Select(value => (double)value!.Value)
            .ToArray();

        return values.Length > 0 ? values.Sum() : null;
    }

    private static double? SumEstimatedRows(ShowplanDocument plan)
    {
        var values = plan.Statements
            .Select(statement => statement.Summary.EstimatedRows)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        return values.Length > 0 ? values.Sum() : null;
    }

    private static PlanComparisonMetric BuildMetric(string name, double? valueA, double? valueB, bool isInteger)
    {
        double? delta = valueA.HasValue && valueB.HasValue
            ? valueB.Value - valueA.Value
            : null;

        double? deltaPercent = delta.HasValue && valueA.HasValue && valueA.Value != 0d
            ? delta.Value / valueA.Value * 100d
            : null;

        return new PlanComparisonMetric(name, valueA, valueB, delta, deltaPercent, isInteger);
    }
}

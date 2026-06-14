using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Diagnostics.Rules;

public sealed class CardinalityEstimateSkewRule : IPlanDiagnosticRule
{
    public string RuleId => "CardinalityEstimateSkew";

    public IEnumerable<PlanDiagnostic> Evaluate(StatementPlan statement, PlanDiagnosticOptions options)
    {
        foreach (var node in statement.Nodes)
        {
            if (!node.EstimatedRows.HasValue || !node.RuntimeMetrics.ActualRows.HasValue)
            {
                continue;
            }

            var executions = Math.Max(node.RuntimeMetrics.ActualExecutions ?? 1d, 1d);
            var estimatedTotalRows = node.EstimatedRows.Value * executions;
            var actualRows = node.RuntimeMetrics.ActualRows.Value;
            var larger = Math.Max(estimatedTotalRows, actualRows);
            var smaller = Math.Max(Math.Min(estimatedTotalRows, actualRows), 1d);
            var ratio = larger / smaller;

            var severity = ratio >= options.CardinalityEstimateSkewCriticalRatio
                && larger >= options.CardinalityEstimateSkewCriticalMinimumRows
                    ? PlanDiagnosticSeverity.Critical
                    : ratio >= options.CardinalityEstimateSkewWarningRatio
                        && larger >= options.CardinalityEstimateSkewWarningMinimumRows
                            ? PlanDiagnosticSeverity.Warning
                            : (PlanDiagnosticSeverity?)null;

            if (!severity.HasValue)
            {
                continue;
            }

            yield return new PlanDiagnostic(
                RuleId,
                "Cardinality estimate skew",
                severity.Value,
                statement.StatementId,
                node.NodeId,
                $"Estimated and actual rows differ by {DiagnosticRuleHelpers.FormatRatio(ratio)}x on {node.PhysicalOp}.",
                "Review statistics, predicates, parameter sensitivity, and join order for this operator.",
                new[]
                {
                    DiagnosticRuleHelpers.Evidence("Estimated total rows", estimatedTotalRows),
                    DiagnosticRuleHelpers.Evidence("Actual rows", actualRows),
                    DiagnosticRuleHelpers.Evidence("Actual executions", executions),
                    DiagnosticRuleHelpers.Evidence("Ratio", ratio)
                });
        }
    }
}

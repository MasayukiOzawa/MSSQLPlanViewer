using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Diagnostics.Rules;

public sealed class LargeScanWithResidualPredicateRule : IPlanDiagnosticRule
{
    public string RuleId => "LargeScanWithResidualPredicate";

    public IEnumerable<PlanDiagnostic> Evaluate(StatementPlan statement, PlanDiagnosticOptions options)
    {
        foreach (var node in statement.Nodes)
        {
            if (!DiagnosticRuleHelpers.IsScan(node.PhysicalOp))
            {
                continue;
            }

            var predicate = node.Properties.FirstOrDefault(property => DiagnosticRuleHelpers.IsNamedProperty(property, "Predicate"));
            if (predicate is null)
            {
                continue;
            }

            var rows = node.RuntimeMetrics.ActualRows ?? node.EstimatedRows;
            var actualRowsRead = node.RuntimeMetrics.Threads.Sum(thread => thread.ActualRowsRead ?? 0d);
            var readToRowsRatio = node.RuntimeMetrics.ActualRows is > 0d && actualRowsRead > 0d
                ? actualRowsRead / node.RuntimeMetrics.ActualRows.Value
                : (double?)null;
            var severity = readToRowsRatio >= options.LargeScanRowsReadToRowsWarningRatio
                ? PlanDiagnosticSeverity.Warning
                : rows >= options.LargeScanRowsInfoThreshold
                    ? PlanDiagnosticSeverity.Info
                    : (PlanDiagnosticSeverity?)null;

            if (!severity.HasValue)
            {
                continue;
            }

            yield return new PlanDiagnostic(
                RuleId,
                "Large scan with residual predicate",
                severity.Value,
                statement.StatementId,
                node.NodeId,
                $"{node.PhysicalOp} evaluates a residual predicate over {DiagnosticRuleHelpers.FormatNumber(rows)} rows.",
                "Check whether a more selective index or SARGable predicate can reduce rows read.",
                new[]
                {
                    DiagnosticRuleHelpers.Evidence("Rows", rows),
                    DiagnosticRuleHelpers.Evidence("Rows read", actualRowsRead > 0d ? actualRowsRead : null),
                    DiagnosticRuleHelpers.Evidence("Rows read / rows", readToRowsRatio),
                    DiagnosticRuleHelpers.Evidence("Predicate", predicate.Value)
                });
        }
    }
}

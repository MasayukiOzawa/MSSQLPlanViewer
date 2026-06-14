using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Diagnostics.Rules;

public sealed class ParallelThreadSkewRule : IPlanDiagnosticRule
{
    public string RuleId => "ParallelThreadSkew";

    public IEnumerable<PlanDiagnostic> Evaluate(StatementPlan statement, PlanDiagnosticOptions options)
    {
        foreach (var node in statement.Nodes)
        {
            var statistics = ThreadDistributionStatistics.Compute(node.RuntimeMetrics.Threads);
            if (statistics is null || statistics.TotalRows < options.ParallelThreadSkewMinimumRows)
            {
                continue;
            }

            var severity = statistics.MaxToAverageRatio >= options.ParallelThreadSkewCriticalRatio
                || statistics.CoefficientOfVariation >= options.ParallelThreadSkewCriticalCoefficientOfVariation
                    ? PlanDiagnosticSeverity.Critical
                    : statistics.MaxToAverageRatio >= options.ParallelThreadSkewWarningRatio
                        ? PlanDiagnosticSeverity.Warning
                        : (PlanDiagnosticSeverity?)null;

            if (!severity.HasValue)
            {
                continue;
            }

            yield return new PlanDiagnostic(
                RuleId,
                "Parallel thread skew",
                severity.Value,
                statement.StatementId,
                node.NodeId,
                $"{node.PhysicalOp} has uneven row distribution across {statistics.WorkerThreadCount} worker threads.",
                "Investigate data skew, repartitioning choices, exchange operators, and whether the plan would benefit from different indexing or distribution.",
                new[]
                {
                    DiagnosticRuleHelpers.Evidence("Worker threads", statistics.WorkerThreadCount),
                    DiagnosticRuleHelpers.Evidence("Total rows", statistics.TotalRows),
                    DiagnosticRuleHelpers.Evidence("Max rows", statistics.MaxRows),
                    DiagnosticRuleHelpers.Evidence("Average rows", statistics.AverageRows),
                    DiagnosticRuleHelpers.Evidence("Max / average", statistics.MaxToAverageRatio),
                    DiagnosticRuleHelpers.Evidence("Coefficient of variation", statistics.CoefficientOfVariation)
                });
        }
    }
}

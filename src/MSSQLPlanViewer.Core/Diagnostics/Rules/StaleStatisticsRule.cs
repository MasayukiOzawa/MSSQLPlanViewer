using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Diagnostics.Rules;

public sealed class StaleStatisticsRule : IPlanDiagnosticRule
{
    public string RuleId => "StaleStatistics";

    public IEnumerable<PlanDiagnostic> Evaluate(StatementPlan statement, PlanDiagnosticOptions options)
    {
        foreach (var stats in statement.Summary.OptimizerStatsUsageEntries)
        {
            var rows = DiagnosticRuleHelpers.ParseDouble(stats.Rows);
            var modifications = DiagnosticRuleHelpers.ParseDouble(stats.StatisticsModificationCount);
            var samplingPercent = DiagnosticRuleHelpers.ParseDouble(stats.SamplingPercent);
            var statsName = BuildStatsName(stats);

            if (rows >= options.StaleStatisticsMinimumRows
                && modifications > rows * options.StaleStatisticsModificationRatio)
            {
                yield return new PlanDiagnostic(
                    RuleId,
                    "Stale statistics",
                    PlanDiagnosticSeverity.Warning,
                    statement.StatementId,
                    null,
                    $"{statsName} has {DiagnosticRuleHelpers.FormatNumber(modifications)} modifications over {DiagnosticRuleHelpers.FormatNumber(rows)} rows.",
                    "Update statistics or review automatic statistics maintenance for this table.",
                    BuildEvidence(statsName, rows, modifications, samplingPercent));
            }

            if (rows >= options.StaleStatisticsSamplingMinimumRows
                && samplingPercent < options.StaleStatisticsSamplingPercent)
            {
                yield return new PlanDiagnostic(
                    RuleId,
                    "Low-sample statistics",
                    PlanDiagnosticSeverity.Info,
                    statement.StatementId,
                    null,
                    $"{statsName} was sampled at {DiagnosticRuleHelpers.FormatNumber(samplingPercent)}%.",
                    "Consider whether a higher sample rate would improve estimates for this workload.",
                    BuildEvidence(statsName, rows, modifications, samplingPercent));
            }
        }
    }

    private static IReadOnlyList<PlanProperty> BuildEvidence(
        string statsName,
        double? rows,
        double? modifications,
        double? samplingPercent) =>
        new[]
        {
            DiagnosticRuleHelpers.Evidence("Statistics", statsName),
            DiagnosticRuleHelpers.Evidence("Rows", rows),
            DiagnosticRuleHelpers.Evidence("Modification count", modifications),
            DiagnosticRuleHelpers.Evidence("Sampling percent", samplingPercent)
        };

    private static string BuildStatsName(OptimizerStatsUsageEntry stats)
    {
        var table = string.Join(".", new[] { stats.Database, stats.Schema, stats.Table }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

        return string.IsNullOrWhiteSpace(table)
            ? stats.Statistics ?? "Statistics"
            : $"{table} {stats.Statistics}".Trim();
    }
}

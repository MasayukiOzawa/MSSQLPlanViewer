using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Diagnostics.Rules;

public sealed class TempDbSpillRule : IPlanDiagnosticRule
{
    private static readonly string[] SpillWarningNames =
    [
        "SpillToTempDb",
        "HashSpillDetails",
        "SortSpillDetails",
        "ExchangeSpillDetails",
        "SpillOccurred"
    ];

    public string RuleId => "TempDbSpill";

    public IEnumerable<PlanDiagnostic> Evaluate(StatementPlan statement, PlanDiagnosticOptions options)
    {
        foreach (var node in statement.Nodes)
        {
            var spillWarnings = node.Warnings
                .Where(warning => SpillWarningNames.Any(name => DiagnosticRuleHelpers.NameEquals(warning.Name, name)))
                .ToArray();
            if (spillWarnings.Length == 0)
            {
                continue;
            }

            var spillLevel = spillWarnings
                .Select(warning =>
                    DiagnosticRuleHelpers.ExtractNamedDouble(warning.Details, "SpillLevel")
                    ?? DiagnosticRuleHelpers.ExtractNamedDouble(warning.Value, "SpillLevel"))
                .Where(level => level.HasValue)
                .Max();
            var severity = spillLevel >= 2d
                ? PlanDiagnosticSeverity.Critical
                : PlanDiagnosticSeverity.Warning;

            yield return new PlanDiagnostic(
                RuleId,
                "TempDB spill",
                severity,
                statement.StatementId,
                node.NodeId,
                $"{node.PhysicalOp} spilled work data to tempdb.",
                "Check memory grants, row estimates, sort/hash inputs, and whether supporting indexes can reduce spill pressure.",
                new[]
                {
                    DiagnosticRuleHelpers.Evidence("Warnings", string.Join(", ", spillWarnings.Select(warning => warning.Name).Distinct(StringComparer.OrdinalIgnoreCase))),
                    DiagnosticRuleHelpers.Evidence("Spill level", spillLevel)
                });
        }
    }
}

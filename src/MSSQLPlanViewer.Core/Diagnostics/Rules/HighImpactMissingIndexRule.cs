using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Diagnostics.Rules;

public sealed class HighImpactMissingIndexRule : IPlanDiagnosticRule
{
    public string RuleId => "HighImpactMissingIndex";

    public IEnumerable<PlanDiagnostic> Evaluate(StatementPlan statement, PlanDiagnosticOptions options)
    {
        foreach (var missingIndex in statement.Summary.MissingIndexesEntries)
        {
            var impact = DiagnosticRuleHelpers.ParseDouble(missingIndex.Impact);
            if (impact < options.HighImpactMissingIndexImpact)
            {
                continue;
            }

            yield return new PlanDiagnostic(
                RuleId,
                "High-impact missing index",
                PlanDiagnosticSeverity.Warning,
                statement.StatementId,
                null,
                $"Missing index recommendation reports {DiagnosticRuleHelpers.FormatNumber(impact)} impact for {missingIndex.ObjectName}.",
                BuildRecommendation(missingIndex),
                new[]
                {
                    DiagnosticRuleHelpers.Evidence("Impact", impact),
                    DiagnosticRuleHelpers.Evidence("Object", missingIndex.ObjectName),
                    DiagnosticRuleHelpers.Evidence("EQUALITY", missingIndex.EqualityColumns),
                    DiagnosticRuleHelpers.Evidence("INEQUALITY", missingIndex.InequalityColumns),
                    DiagnosticRuleHelpers.Evidence("INCLUDE", missingIndex.IncludeColumns)
                });
        }
    }

    private static string BuildRecommendation(MissingIndexEntry entry)
    {
        var parts = new List<string>();
        AddPart(parts, "EQUALITY", entry.EqualityColumns);
        AddPart(parts, "INEQUALITY", entry.InequalityColumns);
        AddPart(parts, "INCLUDE", entry.IncludeColumns);

        return parts.Count == 0
            ? "Review the missing index recommendation and validate it against existing indexes and write workload."
            : $"Validate a covering index for {entry.ObjectName}: {string.Join("; ", parts)}.";
    }

    private static void AddPart(ICollection<string> parts, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{label}: {value}");
        }
    }
}

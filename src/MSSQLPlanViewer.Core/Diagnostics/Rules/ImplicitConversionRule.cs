using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Diagnostics.Rules;

public sealed class ImplicitConversionRule : IPlanDiagnosticRule
{
    public string RuleId => "ImplicitConversion";

    public IEnumerable<PlanDiagnostic> Evaluate(StatementPlan statement, PlanDiagnosticOptions options)
    {
        if (DiagnosticRuleHelpers.HasPlanAffectingConvert(statement))
        {
            var nodeWithWarning = statement.Nodes.FirstOrDefault(node =>
                DiagnosticRuleHelpers.HasWarning(node.Warnings, "PlanAffectingConvert"));

            yield return new PlanDiagnostic(
                RuleId,
                "Implicit conversion",
                PlanDiagnosticSeverity.Warning,
                statement.StatementId,
                nodeWithWarning?.NodeId,
                "The plan reports PlanAffectingConvert, which can prevent efficient seeks or distort estimates.",
                "Align parameter and column data types, remove implicit conversions from predicates, or use typed literals.",
                new[]
                {
                    DiagnosticRuleHelpers.Evidence("Warning", "PlanAffectingConvert")
                });
            yield break;
        }

        foreach (var node in statement.Nodes)
        {
            if (!DiagnosticRuleHelpers.IsScanOrSeek(node.PhysicalOp))
            {
                continue;
            }

            var predicate = node.Properties.FirstOrDefault(property =>
                (DiagnosticRuleHelpers.IsNamedProperty(property, "Predicate")
                || DiagnosticRuleHelpers.IsNamedProperty(property, "Seek predicate"))
                && property.Value.Contains("CONVERT_IMPLICIT", StringComparison.OrdinalIgnoreCase));
            if (predicate is null)
            {
                continue;
            }

            yield return new PlanDiagnostic(
                RuleId,
                "Implicit conversion",
                PlanDiagnosticSeverity.Warning,
                statement.StatementId,
                node.NodeId,
                $"{node.PhysicalOp} contains CONVERT_IMPLICIT in {predicate.Name}.",
                "Align compared data types so the optimizer can use indexes and estimates more reliably.",
                new[]
                {
                    DiagnosticRuleHelpers.Evidence(predicate.Name, predicate.Value)
                });
        }
    }
}

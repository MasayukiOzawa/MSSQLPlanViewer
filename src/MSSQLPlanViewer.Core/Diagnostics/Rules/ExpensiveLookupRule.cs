using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Diagnostics.Rules;

public sealed class ExpensiveLookupRule : IPlanDiagnosticRule
{
    public string RuleId => "ExpensiveLookup";

    public IEnumerable<PlanDiagnostic> Evaluate(StatementPlan statement, PlanDiagnosticOptions options)
    {
        foreach (var node in statement.Nodes)
        {
            if (!IsLookup(node))
            {
                continue;
            }

            var executionCount = node.RuntimeMetrics.ActualExecutions
                ?? (DiagnosticRuleHelpers.GetXmlAttributeDouble(node.XmlAttributes, "RelOp.EstimateRebinds") ?? 0d)
                + (DiagnosticRuleHelpers.GetXmlAttributeDouble(node.XmlAttributes, "RelOp.EstimateRewinds") ?? 0d)
                + 1d;

            var severity = executionCount >= options.ExpensiveLookupCriticalExecutions
                ? PlanDiagnosticSeverity.Critical
                : executionCount >= options.ExpensiveLookupWarningExecutions
                    ? PlanDiagnosticSeverity.Warning
                    : (PlanDiagnosticSeverity?)null;

            if (!severity.HasValue)
            {
                continue;
            }

            yield return new PlanDiagnostic(
                RuleId,
                "Expensive lookup",
                severity.Value,
                statement.StatementId,
                node.NodeId,
                $"{node.PhysicalOp} executed {DiagnosticRuleHelpers.FormatNumber(executionCount)} times.",
                "Consider a covering index, reducing outer input rows, or revisiting the join strategy.",
                new[]
                {
                    DiagnosticRuleHelpers.Evidence("Executions", executionCount),
                    DiagnosticRuleHelpers.Evidence("Object", node.ObjectReference is null ? null : $"{node.ObjectReference.Table} {node.ObjectReference.Index}".Trim())
                });
        }
    }

    private static bool IsLookup(PlanNode node) =>
        DiagnosticRuleHelpers.NameEquals(node.PhysicalOp, "RID Lookup")
        || DiagnosticRuleHelpers.NameEquals(
            DiagnosticRuleHelpers.GetXmlAttributeValue(node.XmlAttributes, "RelOp.IndexScan.Lookup"),
            "1")
        || DiagnosticRuleHelpers.NameEquals(
            DiagnosticRuleHelpers.GetXmlAttributeValue(node.XmlAttributes, "RelOp.IndexScan.Lookup"),
            "true");
}

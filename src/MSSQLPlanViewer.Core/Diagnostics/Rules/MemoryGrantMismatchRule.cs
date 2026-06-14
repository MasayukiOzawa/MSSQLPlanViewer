using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Diagnostics.Rules;

public sealed class MemoryGrantMismatchRule : IPlanDiagnosticRule
{
    public string RuleId => "MemoryGrantMismatch";

    public IEnumerable<PlanDiagnostic> Evaluate(StatementPlan statement, PlanDiagnosticOptions options)
    {
        var grantedMemoryKb = DiagnosticRuleHelpers.GetPropertyDouble(statement.Summary.MemoryGrantInfoProperties, "GrantedMemory");
        var maxUsedMemoryKb = DiagnosticRuleHelpers.GetPropertyDouble(statement.Summary.MemoryGrantInfoProperties, "MaxUsedMemory");
        var grantWaitTimeMs = DiagnosticRuleHelpers.GetPropertyDouble(statement.Summary.MemoryGrantInfoProperties, "GrantWaitTime");
        var hasMemoryGrantWarning = DiagnosticRuleHelpers.HasWarning(statement.Warnings, "MemoryGrantWarning");

        if (!grantedMemoryKb.HasValue && !maxUsedMemoryKb.HasValue && !grantWaitTimeMs.HasValue && !hasMemoryGrantWarning)
        {
            yield break;
        }

        var usedRatio = grantedMemoryKb is > 0d && maxUsedMemoryKb.HasValue
            ? maxUsedMemoryKb.Value / grantedMemoryKb.Value
            : (double?)null;
        var severity = hasMemoryGrantWarning
            ? PlanDiagnosticSeverity.Critical
            : grantedMemoryKb >= options.MemoryGrantMinimumGrantedKb
                && usedRatio < options.MemoryGrantLowUsageRatio
                    ? PlanDiagnosticSeverity.Warning
                    : grantWaitTimeMs > 0d
                        ? PlanDiagnosticSeverity.Info
                        : (PlanDiagnosticSeverity?)null;

        if (!severity.HasValue)
        {
            yield break;
        }

        yield return new PlanDiagnostic(
            RuleId,
            "Memory grant mismatch",
            severity.Value,
            statement.StatementId,
            null,
            BuildMessage(severity.Value, grantedMemoryKb, maxUsedMemoryKb, grantWaitTimeMs),
            "Review row estimates, memory-intensive operators, concurrency, and whether query/index changes can reduce grant pressure.",
            new[]
            {
                DiagnosticRuleHelpers.Evidence("GrantedMemory KB", grantedMemoryKb),
                DiagnosticRuleHelpers.Evidence("MaxUsedMemory KB", maxUsedMemoryKb),
                DiagnosticRuleHelpers.Evidence("Used ratio", usedRatio),
                DiagnosticRuleHelpers.Evidence("GrantWaitTime", grantWaitTimeMs),
                DiagnosticRuleHelpers.Evidence("MemoryGrantWarning", hasMemoryGrantWarning ? "true" : "false")
            });
    }

    private static string BuildMessage(
        PlanDiagnosticSeverity severity,
        double? grantedMemoryKb,
        double? maxUsedMemoryKb,
        double? grantWaitTimeMs) =>
        severity switch
        {
            PlanDiagnosticSeverity.Critical => "The statement reports a MemoryGrantWarning.",
            PlanDiagnosticSeverity.Warning => $"Granted memory was {DiagnosticRuleHelpers.FormatNumber(grantedMemoryKb)} KB but max used was {DiagnosticRuleHelpers.FormatNumber(maxUsedMemoryKb)} KB.",
            _ => $"The statement waited {DiagnosticRuleHelpers.FormatNumber(grantWaitTimeMs)} ms for a memory grant."
        };
}

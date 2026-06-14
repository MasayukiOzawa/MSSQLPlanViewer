using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Diagnostics;

public sealed record PlanDiagnostic(
    string RuleId,
    string RuleName,
    PlanDiagnosticSeverity Severity,
    string StatementId,
    string? NodeId,
    string Message,
    string Recommendation,
    IReadOnlyList<PlanProperty> Evidence);

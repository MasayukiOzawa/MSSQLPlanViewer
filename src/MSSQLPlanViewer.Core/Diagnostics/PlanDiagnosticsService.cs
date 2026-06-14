using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Diagnostics;

public sealed class PlanDiagnosticsService : IPlanDiagnosticsService
{
    private readonly IReadOnlyList<IPlanDiagnosticRule> rules;
    private readonly PlanDiagnosticOptions options;

    public PlanDiagnosticsService(IEnumerable<IPlanDiagnosticRule> rules)
        : this(rules, new PlanDiagnosticOptions())
    {
    }

    public PlanDiagnosticsService(IEnumerable<IPlanDiagnosticRule> rules, PlanDiagnosticOptions options)
    {
        this.rules = rules.ToArray();
        this.options = options;
    }

    public IReadOnlyList<PlanDiagnostic> Analyze(ShowplanDocument document)
    {
        var diagnostics = new List<PlanDiagnostic>();

        foreach (var statement in document.Statements)
        {
            foreach (var rule in rules)
            {
                try
                {
                    diagnostics.AddRange(rule.Evaluate(statement, options));
                }
                catch
                {
                    // Individual rules parse optional showplan text and should not block the rest of the analysis.
                }
            }
        }

        return diagnostics
            .OrderByDescending(diagnostic => diagnostic.Severity)
            .ThenBy(diagnostic => diagnostic.StatementId, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.NodeId ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.RuleId, StringComparer.Ordinal)
            .ToArray();
    }
}

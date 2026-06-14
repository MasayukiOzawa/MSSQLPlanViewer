using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Diagnostics;

public interface IPlanDiagnosticsService
{
    IReadOnlyList<PlanDiagnostic> Analyze(ShowplanDocument document);
}

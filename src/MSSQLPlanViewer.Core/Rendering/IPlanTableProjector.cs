using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Rendering;

public interface IPlanTableProjector
{
    IReadOnlyList<PlanTableRow> Project(StatementPlan statement);
}

using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Rendering;

public interface IPlanGraphLayoutService
{
    StatementGraphLayout CreateLayout(StatementPlan statement, decimal? statementCostRatio = null);
}

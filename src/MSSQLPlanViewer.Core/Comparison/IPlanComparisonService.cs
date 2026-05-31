using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Comparison;

/// <summary>
/// Compares two execution plans across a set of high-level summary metrics.
/// </summary>
public interface IPlanComparisonService
{
    PlanComparisonResult Compare(ShowplanDocument planA, ShowplanDocument planB);
}

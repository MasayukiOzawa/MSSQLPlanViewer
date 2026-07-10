using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Rendering;

internal static class PlanCostDisplay
{
    public static decimal ResolveDisplayCostBasis(StatementPlan statement)
    {
        var rootNodeCost = statement.Nodes
            .FirstOrDefault(node => string.Equals(node.NodeId, "0", StringComparison.Ordinal))
            ?.EstimatedSubtreeCost ?? 0;
        if (rootNodeCost > 0)
        {
            return rootNodeCost;
        }

        var statementCost = statement.Summary.EstimatedSubtreeCost ?? 0;
        if (statementCost > 0)
        {
            return statementCost;
        }

        return statement.Nodes.Max(node => node.EstimatedSubtreeCost ?? 0);
    }

    public static decimal CalculateCostRatio(PlanNode node, decimal displayCostBasis)
    {
        if (displayCostBasis <= 0)
        {
            return 0;
        }

        var cost = node.EstimatedSubtreeCost ?? 0;
        if (cost <= 0)
        {
            return 0;
        }

        var ratio = cost / displayCostBasis;
        return ratio >= 1 ? 1 : ratio;
    }
}

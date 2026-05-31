using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Rendering;

internal static class PlanTreeNavigator
{
    public static IReadOnlyList<string> ResolveRootNodeIds(
        StatementPlan statement,
        IReadOnlyDictionary<string, PlanNode> nodesById)
    {
        var incomingTargets = statement.Edges
            .Select(edge => edge.ToNodeId)
            .ToHashSet(StringComparer.Ordinal);

        var rootNodeIds = statement.RootNodeIds.Count > 0
            ? statement.RootNodeIds.Where(nodesById.ContainsKey).Distinct(StringComparer.Ordinal).ToArray()
            : statement.Nodes
                .Select(node => node.NodeId)
                .Where(nodeId => !incomingTargets.Contains(nodeId))
                .ToArray();

        if (rootNodeIds.Length == 0 && statement.Nodes.Count > 0)
        {
            rootNodeIds = [statement.Nodes[0].NodeId];
        }

        return rootNodeIds;
    }
}

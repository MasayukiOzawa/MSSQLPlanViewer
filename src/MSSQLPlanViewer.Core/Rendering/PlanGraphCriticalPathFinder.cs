using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Rendering;

internal static class PlanGraphCriticalPathFinder
{
    /// <summary>
    /// Traces the dominant estimated-cost chain from the most expensive root operator.
    /// Subtree cost is cumulative, so this highlights the branch contributing most to estimated plan cost.
    /// </summary>
    public static (HashSet<string> Nodes, HashSet<(string From, string To)> Edges) Compute(
        IReadOnlyDictionary<string, PlanNode> nodesById,
        IReadOnlyDictionary<string, string[]> childrenByParent,
        IReadOnlyList<string> rootNodeIds,
        decimal maxCost)
    {
        var pathNodes = new HashSet<string>(StringComparer.Ordinal);
        var pathEdges = new HashSet<(string From, string To)>();

        if (maxCost <= 0)
        {
            return (pathNodes, pathEdges);
        }

        var current = rootNodeIds
            .Where(nodesById.ContainsKey)
            .OrderByDescending(nodeId => nodesById[nodeId].EstimatedSubtreeCost ?? 0)
            .ThenBy(nodeId => nodeId, StringComparer.Ordinal)
            .FirstOrDefault();

        while (current is not null && pathNodes.Add(current))
        {
            if (!childrenByParent.TryGetValue(current, out var children))
            {
                break;
            }

            string? next = null;
            var bestCost = 0m;
            foreach (var childId in children)
            {
                var childCost = nodesById.TryGetValue(childId, out var childNode)
                    ? childNode.EstimatedSubtreeCost ?? 0
                    : 0;

                if (childCost <= 0)
                {
                    continue;
                }

                if (next is null
                    || childCost > bestCost
                    || (childCost == bestCost && string.CompareOrdinal(childId, next) < 0))
                {
                    next = childId;
                    bestCost = childCost;
                }
            }

            if (next is null || pathNodes.Contains(next))
            {
                break;
            }

            pathEdges.Add((current, next));
            current = next;
        }

        return (pathNodes, pathEdges);
    }
}

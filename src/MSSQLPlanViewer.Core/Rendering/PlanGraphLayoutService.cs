using MSSQLPlanViewer.Core.Formatting;
using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Rendering;

public sealed class PlanGraphLayoutService : IPlanGraphLayoutService
{
    private const double NodeWidth = 252;
    private const double NodeHeight = 96;
    private const double HorizontalSpacing = 56;
    private const double VerticalSpacing = 28;
    private const double Margin = 24;

    public StatementGraphLayout CreateLayout(StatementPlan statement)
    {
        if (statement.Nodes.Count == 0)
        {
            return new StatementGraphLayout(statement.StatementId, 0, 0, Array.Empty<GraphNodeLayout>(), Array.Empty<GraphEdgeLayout>());
        }

        var nodesById = statement.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        var childrenByParent = statement.Edges
            .GroupBy(edge => edge.FromNodeId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.ToNodeId)
                    .Where(nodesById.ContainsKey)
                    .Distinct(StringComparer.Ordinal)
                    .OrderByDescending(nodeId => nodesById[nodeId].EstimatedSubtreeCost ?? 0)
                    .ThenBy(nodeId => nodeId, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var rootNodeIds = PlanTreeNavigator.ResolveRootNodeIds(statement, nodesById);

        var positions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        var nextAvailableColumnByDepth = new Dictionary<int, int>();

        var nextColumn = 0;

        foreach (var rootNodeId in rootNodeIds)
        {
            var assignedColumn = LayoutSubtree(rootNodeId, depth: 0, preferredColumn: nextColumn, childrenByParent, positions, nextAvailableColumnByDepth);
            nextColumn = Math.Max(nextColumn, assignedColumn + 1);
        }

        foreach (var node in statement.Nodes.Where(node => !positions.ContainsKey(node.NodeId)))
        {
            var assignedColumn = LayoutSubtree(node.NodeId, depth: 0, preferredColumn: nextColumn, childrenByParent, positions, nextAvailableColumnByDepth);
            nextColumn = Math.Max(nextColumn, assignedColumn + 1);
        }

        var maxCost = statement.Nodes.Max(node => node.EstimatedSubtreeCost ?? 0);

        var nodeLayouts = statement.Nodes
            .Select(node =>
            {
                var position = positions[node.NodeId];
                return new GraphNodeLayout(
                    NodeId: node.NodeId,
                    PhysicalOp: node.PhysicalOp,
                    LogicalOp: node.LogicalOp,
                    ObjectName: PlanDisplayFormatter.FormatObjectName(node.ObjectReference),
                    PrimaryLabel: node.PhysicalOp,
                    SecondaryLabel: node.ObjectReference is null
                        ? node.LogicalOp
                        : PlanDisplayFormatter.FormatObjectName(node.ObjectReference),
                    X: position.X,
                    Y: position.Y,
                    Width: NodeWidth,
                    Height: NodeHeight,
                    CostRatio: maxCost <= 0 ? 0 : (node.EstimatedSubtreeCost ?? 0) / maxCost,
                    HasWarnings: node.HasWarnings);
            })
            .ToArray();

        var edgeLayouts = statement.Edges
            .Where(edge => positions.ContainsKey(edge.FromNodeId) && positions.ContainsKey(edge.ToNodeId))
            .Select(edge =>
            {
                var parent = positions[edge.FromNodeId];
                var child = positions[edge.ToNodeId];

                return new GraphEdgeLayout(
                    FromNodeId: edge.FromNodeId,
                    ToNodeId: edge.ToNodeId,
                    X1: parent.X + (NodeWidth / 2),
                    Y1: parent.Y + NodeHeight,
                    X2: child.X + (NodeWidth / 2),
                    Y2: child.Y);
            })
            .ToArray();

        var width = nodeLayouts.Max(node => node.X + node.Width) + Margin;
        var height = nodeLayouts.Max(node => node.Y + node.Height) + Margin;

        return new StatementGraphLayout(statement.StatementId, width, height, nodeLayouts, edgeLayouts);
    }

    private static int LayoutSubtree(
        string nodeId,
        int depth,
        int preferredColumn,
        IReadOnlyDictionary<string, string[]> childrenByParent,
        IDictionary<string, (double X, double Y)> positions,
        IDictionary<int, int> nextAvailableColumnByDepth,
        ISet<string>? stack = null)
    {
        stack ??= new HashSet<string>(StringComparer.Ordinal);

        if (positions.TryGetValue(nodeId, out var existingPosition))
        {
            return (int)Math.Round((existingPosition.X - Margin) / (NodeWidth + HorizontalSpacing));
        }

        var nextAvailableColumn = nextAvailableColumnByDepth.TryGetValue(depth, out var reservedColumn)
            ? reservedColumn
            : 0;
        var assignedColumn = Math.Max(preferredColumn, nextAvailableColumn);

        if (!stack.Add(nodeId))
        {
            var fallbackX = Margin + (assignedColumn * (NodeWidth + HorizontalSpacing));
            var fallbackY = Margin + (depth * (NodeHeight + VerticalSpacing));
            positions[nodeId] = (fallbackX, fallbackY);
            nextAvailableColumnByDepth[depth] = assignedColumn + 1;
            return assignedColumn;
        }

        var currentX = Margin + (assignedColumn * (NodeWidth + HorizontalSpacing));
        var currentY = Margin + (depth * (NodeHeight + VerticalSpacing));
        positions[nodeId] = (currentX, currentY);
        nextAvailableColumnByDepth[depth] = assignedColumn + 1;

        if (!childrenByParent.TryGetValue(nodeId, out var children) || children.Length == 0)
        {
            stack.Remove(nodeId);
            return assignedColumn;
        }

        var nextSiblingPreferredColumn = assignedColumn + 1;
        for (var childIndex = 0; childIndex < children.Length; childIndex++)
        {
            var childNodeId = children[childIndex];
            var childPreferredColumn = childIndex == 0 ? assignedColumn : nextSiblingPreferredColumn;
            var childAssignedColumn = LayoutSubtree(childNodeId, depth + 1, childPreferredColumn, childrenByParent, positions, nextAvailableColumnByDepth, stack);
            nextSiblingPreferredColumn = Math.Max(nextSiblingPreferredColumn, childAssignedColumn + 1);
        }

        stack.Remove(nodeId);
        return assignedColumn;
    }
}

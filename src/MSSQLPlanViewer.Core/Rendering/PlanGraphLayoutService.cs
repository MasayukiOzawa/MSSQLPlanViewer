using MSSQLPlanViewer.Core.Formatting;
using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Rendering;

public sealed class PlanGraphLayoutService : IPlanGraphLayoutService
{
    private const double NodeWidth = 252;
    private const double NodeHeight = 112;
    private const double StatementNodeWidth = 252;
    private const double StatementNodeHeight = 88;
    private const double HorizontalSpacing = 56;
    private const double VerticalSpacing = 28;
    private const double Margin = 24;

    public StatementGraphLayout CreateLayout(
        StatementPlan statement,
        decimal? statementCostRatio = null,
        GraphLayoutDirection direction = GraphLayoutDirection.Vertical)
    {
        if (statement.Nodes.Count == 0)
        {
            return new StatementGraphLayout(
                statement.StatementId,
                StatementNode: null,
                Width: 0,
                Height: 0,
                Nodes: Array.Empty<GraphNodeLayout>(),
                StatementEdges: Array.Empty<GraphEdgeLayout>(),
                Edges: Array.Empty<GraphEdgeLayout>(),
                Direction: direction);
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

        var positions = direction == GraphLayoutDirection.HorizontalSsms
            ? BuildHorizontalPositions(statement, childrenByParent, rootNodeIds)
            : BuildVerticalPositions(statement, childrenByParent, rootNodeIds);

        var maxCost = statement.Nodes.Max(node => node.EstimatedSubtreeCost ?? 0);

        var criticalPath = ComputeCriticalPath(nodesById, childrenByParent, rootNodeIds, maxCost);

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
                    EstimatedRows: node.EstimatedRows,
                    ActualRows: node.RuntimeMetrics.ActualRows,
                    HasWarnings: node.HasWarnings,
                    IsOnCriticalPath: criticalPath.Nodes.Contains(node.NodeId));
            })
            .ToArray();

        var statementNode = BuildStatementNode(statement, nodeLayouts, maxCost, statementCostRatio, direction);
        var statementEdges = BuildStatementEdges(statementNode, nodeLayouts, rootNodeIds, nodesById, direction);
        var edgeLayouts = BuildEdgeLayouts(statement.Edges, nodeLayouts, criticalPath.Edges, direction);

        var width = Math.Max(statementNode.X + statementNode.Width, nodeLayouts.Max(node => node.X + node.Width)) + Margin;
        var height = Math.Max(statementNode.Y + statementNode.Height, nodeLayouts.Max(node => node.Y + node.Height)) + Margin;

        return new StatementGraphLayout(statement.StatementId, statementNode, width, height, nodeLayouts, statementEdges, edgeLayouts, direction);
    }

    /// <summary>
    /// Traces the dominant estimated-cost chain: starting from the root operator with
    /// the highest <c>EstimatedSubtreeCost</c>, it repeatedly descends into the child
    /// with the highest positive subtree cost until reaching a leaf. Subtree cost is
    /// cumulative, so this chain represents the branch that contributes most to the
    /// estimated plan cost. Returns empty sets when no usable cost data exists.
    /// Ties are broken by ordinal node id for deterministic output, and cycle-closing
    /// edges are intentionally left unmarked.
    /// </summary>
    private static (HashSet<string> Nodes, HashSet<(string From, string To)> Edges) ComputeCriticalPath(
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

    private static Dictionary<string, (double X, double Y)> BuildVerticalPositions(
        StatementPlan statement,
        IReadOnlyDictionary<string, string[]> childrenByParent,
        IReadOnlyList<string> rootNodeIds)
    {
        var positions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        var nextAvailableColumnByDepth = new Dictionary<int, int>();
        var nextColumn = 0;

        foreach (var rootNodeId in rootNodeIds)
        {
            var assignedColumn = LayoutSubtreeVertical(rootNodeId, depth: 0, preferredColumn: nextColumn, childrenByParent, positions, nextAvailableColumnByDepth);
            nextColumn = Math.Max(nextColumn, assignedColumn + 1);
        }

        foreach (var node in statement.Nodes.Where(node => !positions.ContainsKey(node.NodeId)))
        {
            var assignedColumn = LayoutSubtreeVertical(node.NodeId, depth: 0, preferredColumn: nextColumn, childrenByParent, positions, nextAvailableColumnByDepth);
            nextColumn = Math.Max(nextColumn, assignedColumn + 1);
        }

        return positions;
    }

    private static Dictionary<string, (double X, double Y)> BuildHorizontalPositions(
        StatementPlan statement,
        IReadOnlyDictionary<string, string[]> childrenByParent,
        IReadOnlyList<string> rootNodeIds)
    {
        var positions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        var nextAvailableRowByDepth = new Dictionary<int, int>();
        var nextRow = 0;

        foreach (var rootNodeId in rootNodeIds)
        {
            var assignedRow = LayoutSubtreeHorizontal(rootNodeId, depth: 0, preferredRow: nextRow, childrenByParent, positions, nextAvailableRowByDepth);
            nextRow = Math.Max(nextRow, assignedRow + 1);
        }

        foreach (var node in statement.Nodes.Where(node => !positions.ContainsKey(node.NodeId)))
        {
            var assignedRow = LayoutSubtreeHorizontal(node.NodeId, depth: 0, preferredRow: nextRow, childrenByParent, positions, nextAvailableRowByDepth);
            nextRow = Math.Max(nextRow, assignedRow + 1);
        }

        return positions;
    }

    private static int LayoutSubtreeVertical(
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
            var fallbackY = Margin + StatementNodeHeight + VerticalSpacing + (depth * (NodeHeight + VerticalSpacing));
            positions[nodeId] = (fallbackX, fallbackY);
            nextAvailableColumnByDepth[depth] = assignedColumn + 1;
            return assignedColumn;
        }

        var currentX = Margin + (assignedColumn * (NodeWidth + HorizontalSpacing));
        var currentY = Margin + StatementNodeHeight + VerticalSpacing + (depth * (NodeHeight + VerticalSpacing));
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
            var childAssignedColumn = LayoutSubtreeVertical(childNodeId, depth + 1, childPreferredColumn, childrenByParent, positions, nextAvailableColumnByDepth, stack);
            nextSiblingPreferredColumn = Math.Max(nextSiblingPreferredColumn, childAssignedColumn + 1);
        }

        stack.Remove(nodeId);
        return assignedColumn;
    }

    private static int LayoutSubtreeHorizontal(
        string nodeId,
        int depth,
        int preferredRow,
        IReadOnlyDictionary<string, string[]> childrenByParent,
        IDictionary<string, (double X, double Y)> positions,
        IDictionary<int, int> nextAvailableRowByDepth,
        ISet<string>? stack = null)
    {
        stack ??= new HashSet<string>(StringComparer.Ordinal);

        if (positions.TryGetValue(nodeId, out var existingPosition))
        {
            return (int)Math.Round((existingPosition.Y - Margin) / (NodeHeight + VerticalSpacing));
        }

        var nextAvailableRow = nextAvailableRowByDepth.TryGetValue(depth, out var reservedRow)
            ? reservedRow
            : 0;
        var assignedRow = Math.Max(preferredRow, nextAvailableRow);

        if (!stack.Add(nodeId))
        {
            positions[nodeId] = (
                Margin + StatementNodeWidth + HorizontalSpacing + (depth * (NodeWidth + HorizontalSpacing)),
                Margin + (assignedRow * (NodeHeight + VerticalSpacing)));
            nextAvailableRowByDepth[depth] = assignedRow + 1;
            return assignedRow;
        }

        positions[nodeId] = (
            Margin + StatementNodeWidth + HorizontalSpacing + (depth * (NodeWidth + HorizontalSpacing)),
            Margin + (assignedRow * (NodeHeight + VerticalSpacing)));
        nextAvailableRowByDepth[depth] = assignedRow + 1;

        if (!childrenByParent.TryGetValue(nodeId, out var children) || children.Length == 0)
        {
            stack.Remove(nodeId);
            return assignedRow;
        }

        var nextSiblingPreferredRow = assignedRow + 1;
        for (var childIndex = 0; childIndex < children.Length; childIndex++)
        {
            var childNodeId = children[childIndex];
            var childPreferredRow = childIndex == 0 ? assignedRow : nextSiblingPreferredRow;
            var childAssignedRow = LayoutSubtreeHorizontal(childNodeId, depth + 1, childPreferredRow, childrenByParent, positions, nextAvailableRowByDepth, stack);
            nextSiblingPreferredRow = Math.Max(nextSiblingPreferredRow, childAssignedRow + 1);
        }

        stack.Remove(nodeId);
        return assignedRow;
    }

    private static StatementGraphNodeLayout BuildStatementNode(
        StatementPlan statement,
        IReadOnlyList<GraphNodeLayout> nodeLayouts,
        decimal maxCost,
        decimal? statementCostRatio,
        GraphLayoutDirection direction)
    {
        var x = direction == GraphLayoutDirection.HorizontalSsms
            ? Margin
            : BuildVerticalStatementX(nodeLayouts);
        var y = direction == GraphLayoutDirection.HorizontalSsms
            ? nodeLayouts.Min(node => node.Y)
            : Margin;

        var primaryLabel = BuildStatementPrimaryLabel(statement);
        var hasStatementCost = (statement.Summary.EstimatedSubtreeCost ?? maxCost) > 0;
        var costRatio = statementCostRatio.HasValue
            ? ClampCostRatio(statementCostRatio.Value)
            : hasStatementCost ? 1m : 0m;

        return new StatementGraphNodeLayout(
            StatementId: statement.StatementId,
            StatementType: statement.StatementType,
            StatementText: statement.StatementText,
            PrimaryLabel: primaryLabel,
            SecondaryLabel: string.IsNullOrWhiteSpace(statement.StatementText) ? $"Statement #{statement.StatementId}" : statement.StatementText,
            X: x,
            Y: y,
            Width: StatementNodeWidth,
            Height: StatementNodeHeight,
            CostRatio: costRatio);
    }

    private static double BuildVerticalStatementX(IReadOnlyList<GraphNodeLayout> nodeLayouts)
    {
        var minRootY = nodeLayouts.Min(node => node.Y);
        var topNodes = nodeLayouts
            .Where(node => Math.Abs(node.Y - minRootY) < 0.001d)
            .ToArray();
        var left = topNodes.Min(node => node.X);
        var right = topNodes.Max(node => node.X + node.Width);
        var centerX = left + ((right - left) / 2);
        return Math.Max(Margin, centerX - (StatementNodeWidth / 2));
    }

    private static IReadOnlyList<GraphEdgeLayout> BuildStatementEdges(
        StatementGraphNodeLayout statementNode,
        IReadOnlyList<GraphNodeLayout> nodeLayouts,
        IReadOnlyList<string> rootNodeIds,
        IReadOnlyDictionary<string, PlanNode> nodesById,
        GraphLayoutDirection direction) =>
        rootNodeIds
            .Where(rootNodeId => nodesById.ContainsKey(rootNodeId))
            .Distinct(StringComparer.Ordinal)
            .Select(rootNodeId => nodeLayouts.FirstOrDefault(node => string.Equals(node.NodeId, rootNodeId, StringComparison.Ordinal)))
            .Where(node => node is not null)
            .Cast<GraphNodeLayout>()
            .Select(node => direction == GraphLayoutDirection.HorizontalSsms
                ? new GraphEdgeLayout(
                    FromNodeId: statementNode.StatementId,
                    ToNodeId: node.NodeId,
                    X1: node.X,
                    Y1: node.Y + (node.Height / 2),
                    X2: statementNode.X + statementNode.Width,
                    Y2: statementNode.Y + (statementNode.Height / 2),
                    IsOnCriticalPath: false)
                : new GraphEdgeLayout(
                    FromNodeId: statementNode.StatementId,
                    ToNodeId: node.NodeId,
                    X1: statementNode.X + (statementNode.Width / 2),
                    Y1: statementNode.Y + statementNode.Height,
                    X2: node.X + (node.Width / 2),
                    Y2: node.Y,
                    IsOnCriticalPath: false))
            .ToArray();

    private static IReadOnlyList<GraphEdgeLayout> BuildEdgeLayouts(
        IEnumerable<PlanEdge> edges,
        IReadOnlyList<GraphNodeLayout> nodeLayouts,
        IReadOnlySet<(string From, string To)> criticalPathEdges,
        GraphLayoutDirection direction)
    {
        var layoutsByNodeId = nodeLayouts.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        return edges
            .Where(edge => layoutsByNodeId.ContainsKey(edge.FromNodeId) && layoutsByNodeId.ContainsKey(edge.ToNodeId))
            .Select(edge =>
            {
                var parent = layoutsByNodeId[edge.FromNodeId];
                var child = layoutsByNodeId[edge.ToNodeId];
                return direction == GraphLayoutDirection.HorizontalSsms
                    ? new GraphEdgeLayout(
                        FromNodeId: edge.FromNodeId,
                        ToNodeId: edge.ToNodeId,
                        X1: child.X,
                        Y1: child.Y + (child.Height / 2),
                        X2: parent.X + parent.Width,
                        Y2: parent.Y + (parent.Height / 2),
                        IsOnCriticalPath: criticalPathEdges.Contains((edge.FromNodeId, edge.ToNodeId)))
                    : new GraphEdgeLayout(
                        FromNodeId: edge.FromNodeId,
                        ToNodeId: edge.ToNodeId,
                        X1: parent.X + (parent.Width / 2),
                        Y1: parent.Y + parent.Height,
                        X2: child.X + (child.Width / 2),
                        Y2: child.Y,
                        IsOnCriticalPath: criticalPathEdges.Contains((edge.FromNodeId, edge.ToNodeId)));
            })
            .ToArray();
    }

    private static string BuildStatementPrimaryLabel(StatementPlan statement)
    {
        if (string.IsNullOrWhiteSpace(statement.StatementType))
        {
            return "Statement";
        }

        if (statement.StatementType.StartsWith("Stmt", StringComparison.OrdinalIgnoreCase)
            && statement.StatementType.Length > 4)
        {
            var suffix = statement.StatementType[4..];
            return suffix.Equals("Simple", StringComparison.OrdinalIgnoreCase) ? "Statement" : suffix;
        }

        return statement.StatementType;
    }

    private static decimal ClampCostRatio(decimal value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value >= 1 ? 1 : value;
    }
}

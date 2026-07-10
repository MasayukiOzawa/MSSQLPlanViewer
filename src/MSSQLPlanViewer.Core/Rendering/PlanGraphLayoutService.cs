using System.Globalization;
using MSSQLPlanViewer.Core.Formatting;
using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Rendering;

public sealed class PlanGraphLayoutService : IPlanGraphLayoutService
{
    private const double NodeWidth = 252;
    private const double NodeHeight = 310;
    private const double StatementNodeWidth = 252;
    private const double StatementNodeHeight = 88;
    private const double HorizontalSpacing = 196;
    private const double VerticalSpacing = 128;
    private const double Margin = 24;
    private const double DefaultEdgeStrokeWidth = 2.2d;
    private const double MinFlowEdgeStrokeWidth = 1.6d;
    private const double MaxFlowEdgeStrokeWidth = 12d;

    /// <summary>Maps a (depth, cross-axis index) pair to canvas coordinates, and back.</summary>
    private sealed record AxisMapper(
        Func<int, int, (double X, double Y)> ResolvePosition,
        Func<(double X, double Y), int> ResolveIndex);

    private static readonly AxisMapper VerticalAxis = new(
        ResolvePosition: (depth, index) => (
            Margin + (index * (NodeWidth + HorizontalSpacing)),
            Margin + StatementNodeHeight + VerticalSpacing + (depth * (NodeHeight + VerticalSpacing))),
        ResolveIndex: position => (int)Math.Round((position.X - Margin) / (NodeWidth + HorizontalSpacing)));

    private static readonly AxisMapper HorizontalAxis = new(
        ResolvePosition: (depth, index) => (
            Margin + StatementNodeWidth + HorizontalSpacing + (depth * (NodeWidth + HorizontalSpacing)),
            Margin + (index * (NodeHeight + VerticalSpacing))),
        ResolveIndex: position => (int)Math.Round((position.Y - Margin) / (NodeHeight + VerticalSpacing)));

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
                    .OrderBy(nodeId => nodeId, NodeIdLayoutComparer.Instance)
                    .ToArray(),
                StringComparer.Ordinal);

        var rootNodeIds = PlanTreeNavigator.ResolveRootNodeIds(statement, nodesById);

        var isHorizontal = direction == GraphLayoutDirection.HorizontalSsms;
        var positions = BuildPositions(
            statement,
            childrenByParent,
            rootNodeIds,
            orderByNodeId: isHorizontal,
            isHorizontal ? HorizontalAxis : VerticalAxis);

        var maxCost = statement.Nodes.Max(node => node.EstimatedSubtreeCost ?? 0);
        var displayCostBasis = PlanCostDisplay.ResolveDisplayCostBasis(statement);

        var criticalPath = PlanGraphCriticalPathFinder.Compute(nodesById, childrenByParent, rootNodeIds, maxCost);

        var implicitConversionNodeIds = statement.Summary.ImplicitConversionEntries
            .Select(entry => entry.NodeId)
            .ToHashSet(StringComparer.Ordinal);

        var nodeLayouts = statement.Nodes
            .Select(node => BuildNodeLayout(node, positions[node.NodeId], displayCostBasis, criticalPath.Nodes, implicitConversionNodeIds))
            .ToArray();

        var statementNode = BuildStatementNode(statement, nodeLayouts, maxCost, statementCostRatio, direction);
        var statementEdges = BuildStatementEdges(statementNode, nodeLayouts, rootNodeIds, nodesById, direction);
        var edgeLayouts = BuildEdgeLayouts(statement.Edges, nodeLayouts, criticalPath.Edges, direction);

        var width = Math.Max(statementNode.X + statementNode.Width, nodeLayouts.Max(node => node.X + node.Width)) + Margin;
        var height = Math.Max(statementNode.Y + statementNode.Height, nodeLayouts.Max(node => node.Y + node.Height)) + Margin;

        return new StatementGraphLayout(statement.StatementId, statementNode, width, height, nodeLayouts, statementEdges, edgeLayouts, direction);
    }

    private static GraphNodeLayout BuildNodeLayout(
        PlanNode node,
        (double X, double Y) position,
        decimal displayCostBasis,
        IReadOnlySet<string> criticalPathNodes,
        IReadOnlySet<string> implicitConversionNodeIds) =>
        new GraphNodeLayout(
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
            CostRatio: PlanCostDisplay.CalculateCostRatio(node, displayCostBasis),
            EstimatedRows: node.EstimatedRows,
            ActualRows: node.RuntimeMetrics.ActualRows,
            HasWarnings: node.HasWarnings,
            IsOnCriticalPath: criticalPathNodes.Contains(node.NodeId),
            IsParallel: node.IsParallel)
        {
            EstimatedExecutions = ResolveEstimatedExecutions(node),
            EstimatedExecutionMode = GetPropertyValue(node.XmlAttributes, "RelOp.EstimatedExecutionMode"),
            EstimatedCpuCost = node.EstimatedCpuCost,
            EstimatedCpuMs = GetPropertyDouble(
                node.XmlAttributes,
                "RelOp.EstimatedCPUms",
                "RelOp.EstimatedCpuMs",
                "RelOp.EstimateCPUms",
                "RelOp.EstimateCpuMs"),
            EstimatedElapsedMs = GetPropertyDouble(
                node.XmlAttributes,
                "RelOp.EstimatedElapsedms",
                "RelOp.EstimatedElapsedMs",
                "RelOp.EstimateElapsedms",
                "RelOp.EstimateElapsedMs"),
            ActualExecutions = node.RuntimeMetrics.ActualExecutions,
            ActualExecutionMode = GetPropertyValue(node.Properties, "ActualExecutionMode"),
            ActualElapsedMs = node.RuntimeMetrics.ActualElapsedMs,
            ActualCpuMs = node.RuntimeMetrics.ActualCpuMs,
            ActualLogicalReads = node.RuntimeMetrics.ActualLogicalReads,
            ActualPhysicalReads = node.RuntimeMetrics.ActualPhysicalReads,
            AverageRowSize = node.AverageRowSize,
            HasImplicitConversion = implicitConversionNodeIds.Contains(node.NodeId)
        };

    private static Dictionary<string, (double X, double Y)> BuildPositions(
        StatementPlan statement,
        IReadOnlyDictionary<string, string[]> childrenByParent,
        IReadOnlyList<string> rootNodeIds,
        bool orderByNodeId,
        AxisMapper axis)
    {
        var positions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
        var nextAvailableIndexByDepth = new Dictionary<int, int>();
        var nextIndex = 0;

        var orderedRootNodeIds = orderByNodeId
            ? rootNodeIds.OrderBy(nodeId => nodeId, NodeIdLayoutComparer.Instance)
            : rootNodeIds.AsEnumerable();

        foreach (var rootNodeId in orderedRootNodeIds)
        {
            var assignedIndex = LayoutSubtree(rootNodeId, depth: 0, preferredIndex: nextIndex, childrenByParent, positions, nextAvailableIndexByDepth, axis);
            nextIndex = Math.Max(nextIndex, assignedIndex + 1);
        }

        var remainingNodes = statement.Nodes.Where(node => !positions.ContainsKey(node.NodeId));
        if (orderByNodeId)
        {
            remainingNodes = remainingNodes.OrderBy(node => node.NodeId, NodeIdLayoutComparer.Instance);
        }

        foreach (var node in remainingNodes)
        {
            var assignedIndex = LayoutSubtree(node.NodeId, depth: 0, preferredIndex: nextIndex, childrenByParent, positions, nextAvailableIndexByDepth, axis);
            nextIndex = Math.Max(nextIndex, assignedIndex + 1);
        }

        return positions;
    }

    private static int LayoutSubtree(
        string nodeId,
        int depth,
        int preferredIndex,
        IReadOnlyDictionary<string, string[]> childrenByParent,
        IDictionary<string, (double X, double Y)> positions,
        IDictionary<int, int> nextAvailableIndexByDepth,
        AxisMapper axis,
        ISet<string>? stack = null)
    {
        stack ??= new HashSet<string>(StringComparer.Ordinal);

        if (positions.TryGetValue(nodeId, out var existingPosition))
        {
            return axis.ResolveIndex(existingPosition);
        }

        var nextAvailableIndex = nextAvailableIndexByDepth.TryGetValue(depth, out var reservedIndex)
            ? reservedIndex
            : 0;
        var assignedIndex = Math.Max(preferredIndex, nextAvailableIndex);

        positions[nodeId] = axis.ResolvePosition(depth, assignedIndex);
        nextAvailableIndexByDepth[depth] = assignedIndex + 1;

        // Cycle guard: place the node but do not descend into its children again.
        if (!stack.Add(nodeId))
        {
            return assignedIndex;
        }

        if (childrenByParent.TryGetValue(nodeId, out var children) && children.Length > 0)
        {
            var nextSiblingPreferredIndex = assignedIndex + 1;
            for (var childIndex = 0; childIndex < children.Length; childIndex++)
            {
                var childPreferredIndex = childIndex == 0 ? assignedIndex : nextSiblingPreferredIndex;
                var childAssignedIndex = LayoutSubtree(children[childIndex], depth + 1, childPreferredIndex, childrenByParent, positions, nextAvailableIndexByDepth, axis, stack);
                nextSiblingPreferredIndex = Math.Max(nextSiblingPreferredIndex, childAssignedIndex + 1);
            }
        }

        stack.Remove(nodeId);
        return assignedIndex;
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
        var edgeItems = edges
            .Where(edge => layoutsByNodeId.ContainsKey(edge.FromNodeId) && layoutsByNodeId.ContainsKey(edge.ToNodeId))
            .Select(edge => new
            {
                Edge = edge,
                Child = layoutsByNodeId[edge.ToNodeId],
                FlowRows = ResolveEdgeFlowRows(layoutsByNodeId[edge.ToNodeId])
            })
            .ToArray();

        var maxFlowRows = edgeItems
            .Select(item => item.FlowRows.GetValueOrDefault())
            .DefaultIfEmpty(0d)
            .Max();

        return edgeItems
            .Select(edge =>
            {
                var parent = layoutsByNodeId[edge.Edge.FromNodeId];
                var child = layoutsByNodeId[edge.Edge.ToNodeId];
                var strokeWidth = CalculateFlowStrokeWidth(edge.FlowRows, maxFlowRows);
                return direction == GraphLayoutDirection.HorizontalSsms
                    ? new GraphEdgeLayout(
                        FromNodeId: edge.Edge.FromNodeId,
                        ToNodeId: edge.Edge.ToNodeId,
                        X1: child.X,
                        Y1: child.Y + (child.Height / 2),
                        X2: parent.X + parent.Width,
                        Y2: parent.Y + (parent.Height / 2),
                        IsOnCriticalPath: criticalPathEdges.Contains((edge.Edge.FromNodeId, edge.Edge.ToNodeId)),
                        FlowRows: edge.FlowRows,
                        StrokeWidth: strokeWidth,
                        EstimatedRows: edge.Child.EstimatedRows,
                        ActualRows: edge.Child.ActualRows)
                    : new GraphEdgeLayout(
                        FromNodeId: edge.Edge.FromNodeId,
                        ToNodeId: edge.Edge.ToNodeId,
                        X1: parent.X + (parent.Width / 2),
                        Y1: parent.Y + parent.Height,
                        X2: child.X + (child.Width / 2),
                        Y2: child.Y,
                        IsOnCriticalPath: criticalPathEdges.Contains((edge.Edge.FromNodeId, edge.Edge.ToNodeId)),
                        FlowRows: edge.FlowRows,
                        StrokeWidth: strokeWidth,
                        EstimatedRows: edge.Child.EstimatedRows,
                        ActualRows: edge.Child.ActualRows);
            })
            .ToArray();
    }

    private static double? ResolveEdgeFlowRows(GraphNodeLayout childNode)
    {
        if (childNode.ActualRows.HasValue)
        {
            return Math.Max(0d, childNode.ActualRows.Value);
        }

        return childNode.EstimatedRows.HasValue
            ? Math.Max(0d, childNode.EstimatedRows.Value)
            : null;
    }

    private static double CalculateFlowStrokeWidth(double? flowRows, double maxFlowRows)
    {
        if (!flowRows.HasValue)
        {
            return DefaultEdgeStrokeWidth;
        }

        if (flowRows.Value <= 0d || maxFlowRows <= 0d)
        {
            return MinFlowEdgeStrokeWidth;
        }

        var ratio = Math.Log10(flowRows.Value + 1d) / Math.Log10(maxFlowRows + 1d);
        var clampedRatio = Math.Clamp(ratio, 0d, 1d);
        return MinFlowEdgeStrokeWidth + ((MaxFlowEdgeStrokeWidth - MinFlowEdgeStrokeWidth) * clampedRatio);
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

    private sealed class NodeIdLayoutComparer : IComparer<string>
    {
        public static NodeIdLayoutComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left is null)
            {
                return -1;
            }

            if (right is null)
            {
                return 1;
            }

            var leftIsNumeric = long.TryParse(left, out var leftNumber);
            var rightIsNumeric = long.TryParse(right, out var rightNumber);

            if (leftIsNumeric && rightIsNumeric)
            {
                var numericComparison = leftNumber.CompareTo(rightNumber);
                return numericComparison != 0
                    ? numericComparison
                    : string.CompareOrdinal(left, right);
            }

            if (leftIsNumeric != rightIsNumeric)
            {
                return leftIsNumeric ? -1 : 1;
            }

            return string.CompareOrdinal(left, right);
        }
    }

    private static decimal ClampCostRatio(decimal value)
    {
        if (value <= 0)
        {
            return 0;
        }

        return value >= 1 ? 1 : value;
    }

    private static double ResolveEstimatedExecutions(PlanNode node)
    {
        var explicitEstimate = GetPropertyDouble(node.XmlAttributes, "RelOp.EstimateExecutions", "RelOp.EstimatedExecutions");
        if (explicitEstimate.HasValue)
        {
            return explicitEstimate.Value;
        }

        return (GetPropertyDouble(node.XmlAttributes, "RelOp.EstimateRebinds") ?? 0d)
            + (GetPropertyDouble(node.XmlAttributes, "RelOp.EstimateRewinds") ?? 0d)
            + 1d;
    }

    private static string? GetPropertyValue(IEnumerable<PlanProperty> properties, string name) =>
        properties.FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static double? GetPropertyDouble(IEnumerable<PlanProperty> properties, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetPropertyValue(properties, name);
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            {
                return result;
            }
        }

        return null;
    }
}

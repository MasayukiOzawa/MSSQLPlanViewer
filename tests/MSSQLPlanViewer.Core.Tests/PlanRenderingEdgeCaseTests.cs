using MSSQLPlanViewer.Core.Models;
using MSSQLPlanViewer.Core.Rendering;
using static MSSQLPlanViewer.Core.Tests.TestPlanFactory;

namespace MSSQLPlanViewer.Core.Tests;

/// <summary>
/// Exercises the table projector and graph layout services against malformed or
/// unusual plan topologies (missing roots, cycles, disconnected nodes, missing
/// cost data, dangling edges) to confirm they degrade gracefully and never loop.
/// </summary>
public sealed class PlanRenderingEdgeCaseTests
{
    private readonly PlanTableProjector _projector = new();
    private readonly PlanGraphLayoutService _layoutService = new();

    [Fact]
    public void Project_WithoutRootNodeIds_InfersRootFromEdges()
    {
        var statement = Statement(
            nodes: new[] { Node("0", subtreeCost: 1m), Node("1", subtreeCost: 0.5m) },
            edges: new[] { Edge("0", "1") });

        var rows = _projector.Project(statement);

        Assert.Equal(2, rows.Count);
        Assert.Equal("0", rows[0].NodeId);
        Assert.Null(rows[0].ParentNodeId);
        Assert.Equal("1", rows[1].NodeId);
        Assert.Equal("0", rows[1].ParentNodeId);
        Assert.Equal(1, rows[1].Depth);
    }

    [Fact]
    public void Project_CyclicGraphWithNoNaturalRoot_FallsBackToFirstNodeWithoutLooping()
    {
        var statement = Statement(
            nodes: new[] { Node("0"), Node("1") },
            edges: new[] { Edge("0", "1"), Edge("1", "0") });

        var rows = _projector.Project(statement);

        Assert.Equal(2, rows.Count);
        Assert.Equal("0", rows[0].NodeId);
        Assert.Equal("1", rows[1].NodeId);
        Assert.Equal("0", rows[1].ParentNodeId);
    }

    [Fact]
    public void Project_DisconnectedNode_IsProjectedAsAdditionalRoot()
    {
        var statement = Statement(
            nodes: new[] { Node("0"), Node("1"), Node("2") },
            edges: new[] { Edge("0", "1") },
            rootNodeIds: new[] { "0" });

        var rows = _projector.Project(statement);

        Assert.Equal(3, rows.Count);
        var disconnected = Assert.Single(rows, row => row.NodeId == "2");
        Assert.Null(disconnected.ParentNodeId);
        Assert.Equal(0, disconnected.Depth);
        Assert.False(disconnected.HasChildren);
    }

    [Fact]
    public void Project_WhenNoCostInformation_SetsZeroCostRatioAndNotAvailableSummary()
    {
        var statement = Statement(nodes: new[] { Node("0", subtreeCost: null) });

        var rows = _projector.Project(statement);

        var row = Assert.Single(rows);
        Assert.Equal(0m, row.CostRatio);
        Assert.Equal("n/a", row.Summary);
    }

    [Fact]
    public void Project_StatementWarningsReplaceNotAvailableSummaryOnFirstRow()
    {
        var statement = Statement(
            nodes: new[] { Node("0", subtreeCost: null) },
            warnings: new[] { Warning("MissingIndex") });

        var rows = _projector.Project(statement);

        var row = Assert.Single(rows);
        Assert.Equal("MissingIndex", row.Summary);
        Assert.Equal(1, row.WarningCount);
    }

    [Fact]
    public void CreateLayout_EmptyStatement_ReturnsZeroSizedLayout()
    {
        var statement = Statement(nodes: Array.Empty<PlanNode>(), statementId: "7");

        var layout = _layoutService.CreateLayout(statement);

        Assert.Equal("7", layout.StatementId);
        Assert.Equal(0, layout.Width);
        Assert.Equal(0, layout.Height);
        Assert.Null(layout.StatementNode);
        Assert.Empty(layout.Nodes);
        Assert.Empty(layout.StatementEdges);
        Assert.Empty(layout.Edges);
    }

    [Fact]
    public void CreateLayout_WithoutRootNodeIds_PositionsAllNodesTopDown()
    {
        var statement = Statement(
            nodes: new[] { Node("0", subtreeCost: 1m), Node("1", subtreeCost: 0.5m) },
            edges: new[] { Edge("0", "1") });

        var layout = _layoutService.CreateLayout(statement);
        var nodesById = layout.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);

        Assert.Equal(2, layout.Nodes.Count);
        Assert.True(nodesById["0"].Y < nodesById["1"].Y);
        Assert.True(layout.Width > 0);
        Assert.True(layout.Height > 0);
    }

    [Fact]
    public void CreateLayout_CyclicGraph_PositionsEveryNodeWithoutInfiniteRecursion()
    {
        var statement = Statement(
            nodes: new[] { Node("0"), Node("1") },
            edges: new[] { Edge("0", "1"), Edge("1", "0") },
            rootNodeIds: new[] { "0" });

        var layout = _layoutService.CreateLayout(statement);

        Assert.Equal(2, layout.Nodes.Count);
        Assert.All(layout.Nodes, node => Assert.True(node.X >= 0 && node.Y >= 0));
        Assert.True(double.IsFinite(layout.Width));
        Assert.True(double.IsFinite(layout.Height));
    }

    [Fact]
    public void CreateLayout_HorizontalSsms_CyclicGraph_PositionsEveryNodeWithoutInfiniteRecursion()
    {
        var statement = Statement(
            nodes: new[] { Node("0"), Node("1") },
            edges: new[] { Edge("0", "1"), Edge("1", "0") },
            rootNodeIds: new[] { "0" });

        var layout = _layoutService.CreateLayout(statement, direction: GraphLayoutDirection.HorizontalSsms);

        Assert.Equal(GraphLayoutDirection.HorizontalSsms, layout.Direction);
        Assert.Equal(2, layout.Nodes.Count);
        Assert.All(layout.Nodes, node => Assert.True(node.X >= 0 && node.Y >= 0));
        Assert.True(double.IsFinite(layout.Width));
        Assert.True(double.IsFinite(layout.Height));
    }

    [Fact]
    public void CreateLayout_HorizontalSsms_PositionsSiblingNodesByAscendingNumericNodeId()
    {
        var statement = Statement(
            nodes: new[]
            {
                Node("6", subtreeCost: 100m),
                Node("8", subtreeCost: 85m),
                Node("7", subtreeCost: 13m),
                Node("10", subtreeCost: 1m),
            },
            edges: new[] { Edge("6", "8"), Edge("6", "7"), Edge("6", "10") },
            rootNodeIds: new[] { "6" });

        var layout = _layoutService.CreateLayout(statement, direction: GraphLayoutDirection.HorizontalSsms);
        var nodesById = layout.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);

        Assert.True(nodesById["7"].Y < nodesById["8"].Y);
        Assert.True(nodesById["8"].Y < nodesById["10"].Y);
    }

    [Fact]
    public void CreateLayout_DropsEdgesReferencingUnknownNodes()
    {
        var statement = Statement(
            nodes: new[] { Node("0", subtreeCost: 1m), Node("1", subtreeCost: 0.5m) },
            edges: new[] { Edge("0", "1"), Edge("0", "99") },
            rootNodeIds: new[] { "0" });

        var layout = _layoutService.CreateLayout(statement);

        var edge = Assert.Single(layout.Edges);
        Assert.Equal("0", edge.FromNodeId);
        Assert.Equal("1", edge.ToNodeId);
    }

    [Fact]
    public void CreateLayout_WhenNoCostInformation_SetsZeroCostRatioForEveryNode()
    {
        var statement = Statement(
            nodes: new[] { Node("0", subtreeCost: null), Node("1", subtreeCost: null) },
            edges: new[] { Edge("0", "1") },
            rootNodeIds: new[] { "0" });

        var layout = _layoutService.CreateLayout(statement);

        Assert.All(layout.Nodes, node => Assert.Equal(0m, node.CostRatio));
    }

    [Fact]
    public void CreateLayout_UsesNodeZeroSubtreeCostForDisplayedNodeCostRatio()
    {
        var statement = Statement(
            nodes: new[]
            {
                Node("0", subtreeCost: 10m),
                Node("11", subtreeCost: 3m, estimatedCpuCost: 300m, estimatedIoCost: 0m),
                Node("19", subtreeCost: 7m, estimatedCpuCost: 0m, estimatedIoCost: 0m),
                Node("28", subtreeCost: 7m, estimatedCpuCost: 700m, estimatedIoCost: 0m)
            },
            edges: new[] { Edge("0", "11"), Edge("0", "19"), Edge("19", "28") },
            rootNodeIds: new[] { "0" },
            summary: EmptySummary with { EstimatedSubtreeCost = 100m });

        var layout = _layoutService.CreateLayout(statement);
        var nodesById = layout.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);

        Assert.Equal(1m, nodesById["0"].CostRatio);
        Assert.Equal(0.3m, nodesById["11"].CostRatio);
        Assert.Equal(0.7m, nodesById["19"].CostRatio);
        Assert.Equal(0.7m, nodesById["28"].CostRatio);
    }

    [Fact]
    public void Project_UsesNodeZeroSubtreeCostForDisplayedNodeCostRatio()
    {
        var statement = Statement(
            nodes: new[]
            {
                Node("0", subtreeCost: 10m),
                Node("11", subtreeCost: 3m, estimatedCpuCost: 300m, estimatedIoCost: 0m),
                Node("19", subtreeCost: 7m, estimatedCpuCost: 0m, estimatedIoCost: 0m),
                Node("28", subtreeCost: 7m, estimatedCpuCost: 700m, estimatedIoCost: 0m)
            },
            edges: new[] { Edge("0", "11"), Edge("0", "19"), Edge("19", "28") },
            rootNodeIds: new[] { "0" },
            summary: EmptySummary with { EstimatedSubtreeCost = 100m });

        var rowsById = _projector.Project(statement)
            .ToDictionary(row => row.NodeId, StringComparer.Ordinal);

        Assert.Equal(1m, rowsById["0"].CostRatio);
        Assert.Equal(0.3m, rowsById["11"].CostRatio);
        Assert.Equal(0.7m, rowsById["19"].CostRatio);
        Assert.Equal(0.7m, rowsById["28"].CostRatio);
        Assert.Contains("Cost 70%", rowsById["19"].Summary);
        Assert.Contains("Cost 70%", rowsById["28"].Summary);
    }

    [Fact]
    public void CreateLayout_LinearPlan_MarksEveryNodeAndEdgeOnCriticalPath()
    {
        var statement = Statement(
            nodes: new[] { Node("0", subtreeCost: 3m), Node("1", subtreeCost: 2m), Node("2", subtreeCost: 1m) },
            edges: new[] { Edge("0", "1"), Edge("1", "2") },
            rootNodeIds: new[] { "0" });

        var layout = _layoutService.CreateLayout(statement);

        Assert.All(layout.Nodes, node => Assert.True(node.IsOnCriticalPath));
        Assert.All(layout.Edges, edge => Assert.True(edge.IsOnCriticalPath));
    }

    [Fact]
    public void CreateLayout_BranchingPlan_MarksOnlyHighestSubtreeCostBranch()
    {
        var statement = Statement(
            nodes: new[]
            {
                Node("0", subtreeCost: 10m),
                Node("1", subtreeCost: 8m),
                Node("2", subtreeCost: 2m),
            },
            edges: new[] { Edge("0", "1"), Edge("0", "2") },
            rootNodeIds: new[] { "0" });

        var layout = _layoutService.CreateLayout(statement);
        var nodesById = layout.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);

        Assert.True(nodesById["0"].IsOnCriticalPath);
        Assert.True(nodesById["1"].IsOnCriticalPath);
        Assert.False(nodesById["2"].IsOnCriticalPath);

        var criticalEdge = Assert.Single(layout.Edges, edge => edge.IsOnCriticalPath);
        Assert.Equal("0", criticalEdge.FromNodeId);
        Assert.Equal("1", criticalEdge.ToNodeId);
    }

    [Fact]
    public void CreateLayout_StartsCriticalPathFromHighestCostRoot()
    {
        var statement = Statement(
            nodes: new[]
            {
                Node("0", subtreeCost: 1m),
                Node("10", subtreeCost: 5m),
                Node("11", subtreeCost: 4m),
            },
            edges: new[] { Edge("10", "11") },
            rootNodeIds: new[] { "0", "10" });

        var layout = _layoutService.CreateLayout(statement);
        var nodesById = layout.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);

        Assert.False(nodesById["0"].IsOnCriticalPath);
        Assert.True(nodesById["10"].IsOnCriticalPath);
        Assert.True(nodesById["11"].IsOnCriticalPath);
    }

    [Fact]
    public void CreateLayout_WhenNoCostInformation_LeavesCriticalPathEmpty()
    {
        var statement = Statement(
            nodes: new[] { Node("0", subtreeCost: null), Node("1", subtreeCost: null) },
            edges: new[] { Edge("0", "1") },
            rootNodeIds: new[] { "0" });

        var layout = _layoutService.CreateLayout(statement);

        Assert.All(layout.Nodes, node => Assert.False(node.IsOnCriticalPath));
        Assert.All(layout.Edges, edge => Assert.False(edge.IsOnCriticalPath));
    }

    [Fact]
    public void CreateLayout_CriticalPath_StopsAtChildrenWithoutPositiveCost()
    {
        var statement = Statement(
            nodes: new[] { Node("0", subtreeCost: 4m), Node("1", subtreeCost: 0m) },
            edges: new[] { Edge("0", "1") },
            rootNodeIds: new[] { "0" });

        var layout = _layoutService.CreateLayout(statement);
        var nodesById = layout.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);

        Assert.True(nodesById["0"].IsOnCriticalPath);
        Assert.False(nodesById["1"].IsOnCriticalPath);
        Assert.DoesNotContain(layout.Edges, edge => edge.IsOnCriticalPath);
    }

    [Fact]
    public void CreateLayout_CriticalPath_DoesNotMarkCycleClosingEdge()
    {
        var statement = Statement(
            nodes: new[] { Node("0", subtreeCost: 3m), Node("1", subtreeCost: 2m) },
            edges: new[] { Edge("0", "1"), Edge("1", "0") },
            rootNodeIds: new[] { "0" });

        var layout = _layoutService.CreateLayout(statement);
        var nodesById = layout.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);

        Assert.True(nodesById["0"].IsOnCriticalPath);
        Assert.True(nodesById["1"].IsOnCriticalPath);

        var criticalEdge = Assert.Single(layout.Edges, edge => edge.IsOnCriticalPath);
        Assert.Equal("0", criticalEdge.FromNodeId);
        Assert.Equal("1", criticalEdge.ToNodeId);
    }
}

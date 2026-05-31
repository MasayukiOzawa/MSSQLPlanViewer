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
        Assert.Empty(layout.Nodes);
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

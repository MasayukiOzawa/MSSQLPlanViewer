using System.Xml.Linq;
using MSSQLPlanViewer.Core.Rendering;

namespace MSSQLPlanViewer.Core.Tests;

public sealed class PlanGraphSvgRendererTests
{
    private readonly PlanGraphSvgRenderer _renderer = new();

    [Fact]
    public void Render_ProducesValidInlineSvgWithEscapedLabels()
    {
        var statementNode = new StatementGraphNodeLayout(
            "stmt<1>",
            "SELECT",
            "SELECT <value> FROM dbo.[T&1]",
            "SELECT",
            "SELECT <value> FROM dbo.[T&1]",
            16,
            16,
            240,
            88,
            1m);

        var layout = new StatementGraphLayout(
            "stmt<1>",
            statementNode,
            360,
            280,
            new[]
            {
                new GraphNodeLayout(
                    "1",
                    "Index Seek",
                    "Index Seek",
                    "dbo.[T&1]",
                    "Seek <Expr>",
                    "dbo.[T&1] > @p",
                    16,
                    132,
                    240,
                    92,
                    0.72m,
                    EstimatedRows: 1200,
                    ActualRows: 1188,
                    HasWarnings: false,
                    IsOnCriticalPath: true)
            },
            new[]
            {
                new GraphEdgeLayout("stmt<1>", "1", 136, 104, 136, 132, IsOnCriticalPath: false)
            },
            new[]
            {
                new GraphEdgeLayout("0", "1", 120, 100, 120, 132, IsOnCriticalPath: true)
            });

        var svg = _renderer.Render(layout, new GraphRenderOptions(20, true));

        XDocument.Parse(svg);
        Assert.Contains("data-statement-id=\"stmt&lt;1&gt;\"", svg);
        Assert.Contains("SELECT &lt;value&gt; FROM dbo.[T&amp;1]", svg);
        Assert.Contains("Query cost 100%", svg);
        Assert.Contains("Seek &lt;Expr&gt;", svg);
        Assert.Contains("dbo.[T&amp;1] &gt; @p", svg);
        Assert.Contains("Est rows 1,200 | Actual 1,188", svg);
        Assert.Contains("url(#arrow-critical)", svg);
        Assert.Contains("stroke-dasharray=\"4 4\"", svg);
        Assert.Contains("stroke-dasharray=\"5 4\"", svg);
        Assert.DoesNotContain("class=", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DisablesCriticalPathAndThresholdWhenRequested()
    {
        var layout = new StatementGraphLayout(
            StatementId: "stmt2",
            StatementNode: null,
            Width: 360,
            Height: 220,
            Nodes: new[]
            {
                new GraphNodeLayout(
                    "2",
                    "Hash Match",
                    "Inner Join",
                    string.Empty,
                    "Hash Match",
                    "Inner Join",
                    16,
                    16,
                    240,
                    92,
                    0.72m,
                    EstimatedRows: 250.5,
                    ActualRows: null,
                    HasWarnings: true,
                    IsOnCriticalPath: true)
            },
            StatementEdges: Array.Empty<GraphEdgeLayout>(),
            Edges: new[]
            {
                new GraphEdgeLayout("0", "2", 120, 0, 120, 16, IsOnCriticalPath: true)
            });

        var svg = _renderer.Render(layout, new GraphRenderOptions(90, false));

        Assert.DoesNotContain("url(#arrow-critical)", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("stroke-dasharray=\"5 4\"", svg, StringComparison.Ordinal);
        Assert.Contains("stroke=\"#f59e0b\"", svg);
    }

    [Fact]
    public void Render_HorizontalSsms_UsesRightToLeftEdgePaths()
    {
        var layout = new StatementGraphLayout(
            StatementId: "stmt3",
            StatementNode: null,
            Width: 420,
            Height: 180,
            Nodes: new[]
            {
                new GraphNodeLayout(
                    "0",
                    "Nested Loops",
                    "Inner Join",
                    string.Empty,
                    "Nested Loops",
                    "Inner Join",
                    100,
                    40,
                    240,
                    92,
                    1m,
                    EstimatedRows: 42,
                    ActualRows: 42,
                    HasWarnings: false,
                    IsOnCriticalPath: true)
            },
            StatementEdges: Array.Empty<GraphEdgeLayout>(),
            Edges: new[]
            {
                new GraphEdgeLayout("0", "1", 300, 80, 100, 80, IsOnCriticalPath: true)
            },
            Direction: GraphLayoutDirection.HorizontalSsms);

        var svg = _renderer.Render(layout, new GraphRenderOptions(90, true));

        XDocument.Parse(svg);
        Assert.Contains("M 300 80 C 264 80, 136 80, 100 80", svg);
        Assert.Contains("url(#arrow-critical)", svg);
        Assert.DoesNotContain("class=", svg, StringComparison.Ordinal);
    }
}

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
}

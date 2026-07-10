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
                    IsOnCriticalPath: true,
                    IsParallel: false)
                {
                    AverageRowSize = 88,
                    EstimatedExecutions = 2,
                    EstimatedExecutionMode = "Row",
                    EstimatedCpuCost = 0.004m,
                    EstimatedElapsedMs = 21,
                    ActualExecutions = 3,
                    ActualExecutionMode = "Batch",
                    ActualCpuMs = 18,
                    ActualLogicalReads = 9,
                    ActualPhysicalReads = 1,
                    ActualElapsedMs = 27
                }
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

        var document = XDocument.Parse(svg);
        Assert.Contains("data-statement-id=\"stmt&lt;1&gt;\"", svg);
        Assert.Contains("SELECT &lt;value&gt; FROM dbo.[T&amp;1]", svg);
        Assert.DoesNotContain("Query cost", svg);
        Assert.Contains("Seek &lt;Expr&gt;", svg);
        Assert.Contains("dbo.[T&amp;1] &gt; @p", svg);
        Assert.Contains(">Node 1</text>", svg);
        Assert.Contains(">Cost</text>", svg);
        Assert.Contains(">72%</text>", svg);
        Assert.Contains(">Average Row Size</text>", svg);
        Assert.Contains(">88</text>", svg);
        Assert.Contains(">Estimated Rows</text>", svg);
        Assert.Contains(">1,200</text>", svg);
        Assert.Contains(">Estimated Executions</text>", svg);
        Assert.Contains(">2</text>", svg);
        Assert.Contains(">Estimated Mode</text>", svg);
        Assert.Contains(">Row</text>", svg);
        Assert.Contains(">Actual Rows</text>", svg);
        Assert.Contains(">1,188</text>", svg);
        Assert.Contains(">Actual Executions</text>", svg);
        Assert.Contains(">3</text>", svg);
        Assert.Contains(">Actual Mode</text>", svg);
        Assert.Contains(">Batch</text>", svg);
        Assert.Contains(">Actual Logical Reads</text>", svg);
        Assert.Contains(">9</text>", svg);
        Assert.Contains(">Actual Physical Reads</text>", svg);
        Assert.Contains(">1</text>", svg);
        Assert.Contains(">Actual CPU</text>", svg);
        Assert.Contains(">18 ms</text>", svg);
        Assert.Contains(">Actual Elapsed</text>", svg);
        Assert.Contains(">27 ms</text>", svg);
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "text"),
            element => element.Value == "1,200" && element.Attribute("text-anchor")?.Value == "end");
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "text"),
            element => element.Value == "88" && element.Attribute("text-anchor")?.Value == "end");
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "text"),
            element => element.Value == "18 ms" && element.Attribute("text-anchor")?.Value == "end");
        Assert.Equal(
            4,
            document.Descendants().Count(element =>
                element.Name.LocalName == "line"
                && element.Attribute("data-metric-separator")?.Value == "true"));
        Assert.DoesNotContain("CPU Est", svg);
        Assert.DoesNotContain("Elapsed Est", svg);
        Assert.Contains("data-edge-kind=\"critical-outline\"", svg);
        Assert.Contains("markerUnits=\"userSpaceOnUse\"", svg);
        Assert.Contains("markerWidth=\"8\"", svg);
        Assert.Contains("M 0 0 L 8 4 L 0 8 z", svg);
        Assert.Contains("stroke-dasharray=\"4 4\"", svg);
        Assert.Contains("stroke-dasharray=\"5 4\"", svg);
        Assert.Contains("data-cost-emphasis=\"Critical\"", svg);
        Assert.Contains("fill=\"#fef2f2\"", svg);
        Assert.Contains("fill=\"#f5f3ff\"", svg);
        Assert.Contains("fill=\"#ede9fe\"", svg);
        Assert.Contains("stroke=\"#c4b5fd\"", svg);
        Assert.Contains("stroke-width=\"7\"", svg);
        Assert.Contains("height=\"7\"", svg);
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
                    IsOnCriticalPath: true,
                    IsParallel: false)
            },
            StatementEdges: Array.Empty<GraphEdgeLayout>(),
            Edges: new[]
            {
                new GraphEdgeLayout("0", "2", 120, 0, 120, 16, IsOnCriticalPath: true)
            });

        var svg = _renderer.Render(layout, new GraphRenderOptions(90, false));

        Assert.DoesNotContain("url(#arrow-critical)", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("data-edge-kind=\"critical-outline\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("stroke-dasharray=\"5 4\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("data-cost-emphasis", svg, StringComparison.Ordinal);
        Assert.Contains("stroke=\"#f59e0b\"", svg);
        Assert.DoesNotContain("n/a", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Actual Rows", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Actual Logical Reads", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Actual Physical Reads", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Actual CPU", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Actual Elapsed", svg, StringComparison.Ordinal);
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
                    IsOnCriticalPath: true,
                    IsParallel: true)
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
        Assert.Contains("data-edge-kind=\"critical-outline\"", svg);
        Assert.Contains("data-parallel-badge-for=\"0\"", svg);
        Assert.Contains("fill=\"#fde68a\"", svg);
        Assert.Contains("stroke=\"#f59e0b\"", svg);
        Assert.DoesNotContain("class=", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DrawsImplicitConversionBadge()
    {
        var layout = new StatementGraphLayout(
            StatementId: "stmt-implicit",
            StatementNode: null,
            Width: 360,
            Height: 260,
            Nodes: new[]
            {
                new GraphNodeLayout(
                    "5",
                    "Clustered Index Scan",
                    "Clustered Index Scan",
                    "[tpch].[dbo].[REGION]",
                    "Clustered Index Scan",
                    "[tpch].[dbo].[REGION]",
                    40,
                    32,
                    252,
                    214,
                    0.01m,
                    EstimatedRows: 1,
                    ActualRows: 1,
                    HasWarnings: false,
                    IsOnCriticalPath: false,
                    IsParallel: true)
                {
                    HasImplicitConversion = true
                }
            },
            StatementEdges: Array.Empty<GraphEdgeLayout>(),
            Edges: Array.Empty<GraphEdgeLayout>());

        var svg = _renderer.Render(layout, new GraphRenderOptions(20, true));

        XDocument.Parse(svg);
        Assert.Contains("data-parallel-badge-for=\"5\"", svg);
        Assert.Contains("data-implicit-conversion-for=\"5\"", svg);
        Assert.Contains("Implicit conversion", svg);
        Assert.Contains("fill=\"#fef3c7\"", svg);
        Assert.Contains("fill=\"#92400e\"", svg);
    }

    [Fact]
    public void Render_UsesFlowScaledOperatorEdgeStrokeWidth()
    {
        var layout = new StatementGraphLayout(
            StatementId: "stmt-flow",
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
                    IsOnCriticalPath: false,
                    IsParallel: false)
            },
            StatementEdges: Array.Empty<GraphEdgeLayout>(),
            Edges: new[]
            {
                new GraphEdgeLayout("0", "1", 300, 80, 100, 80, IsOnCriticalPath: false, FlowRows: 1000, StrokeWidth: 8.25, EstimatedRows: 1000, ActualRows: 900)
            },
            Direction: GraphLayoutDirection.HorizontalSsms);

        var svg = _renderer.Render(layout, new GraphRenderOptions(90, true));

        var document = XDocument.Parse(svg);
        Assert.Contains("stroke-width=\"8.25\"", svg);
        Assert.Contains("font-size=\"12\"", svg);
        Assert.Contains("font-weight=\"400\"", svg);
        Assert.Contains(">Est rows</text>", svg);
        Assert.Contains(">1,000</text>", svg);
        Assert.Contains(">Act rows</text>", svg);
        Assert.Contains(">900.00</text>", svg);
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "text"),
            element => element.Value == "1,000" && element.Attribute("text-anchor")?.Value == "end");
        Assert.Contains(
            document.Descendants().Where(element => element.Name.LocalName == "text"),
            element => element.Value == "900.00" && element.Attribute("text-anchor")?.Value == "end");
    }

    [Fact]
    public void Render_DrawsFlowLabelsAboveAllOperatorEdges()
    {
        var layout = new StatementGraphLayout(
            StatementId: "stmt-flow-order",
            StatementNode: null,
            Width: 520,
            Height: 220,
            Nodes: Array.Empty<GraphNodeLayout>(),
            StatementEdges: Array.Empty<GraphEdgeLayout>(),
            Edges: new[]
            {
                new GraphEdgeLayout("0", "1", 420, 80, 240, 80, IsOnCriticalPath: false, FlowRows: 100, StrokeWidth: 8, EstimatedRows: 100, ActualRows: 90),
                new GraphEdgeLayout("0", "2", 420, 120, 240, 120, IsOnCriticalPath: false, FlowRows: 200, StrokeWidth: 10, EstimatedRows: 200, ActualRows: 180)
            },
            Direction: GraphLayoutDirection.HorizontalSsms);

        var svg = _renderer.Render(layout, new GraphRenderOptions(90, true));

        var lastEdgeIndex = svg.LastIndexOf("marker-end=\"url(#arrow)\"", StringComparison.Ordinal);
        var firstLabelIndex = svg.IndexOf(">Est rows</text>", StringComparison.Ordinal);
        Assert.True(firstLabelIndex > lastEdgeIndex);
    }

    [Fact]
    public void Render_ColorsEdgesByRowSourceAndLayersCriticalOutline()
    {
        var layout = new StatementGraphLayout(
            StatementId: "stmt-edge-colors",
            StatementNode: null,
            Width: 520,
            Height: 220,
            Nodes: Array.Empty<GraphNodeLayout>(),
            StatementEdges: Array.Empty<GraphEdgeLayout>(),
            Edges: new[]
            {
                new GraphEdgeLayout("0", "1", 420, 60, 240, 60, IsOnCriticalPath: true, FlowRows: 0, StrokeWidth: 1.6, EstimatedRows: 1_000, ActualRows: 0),
                new GraphEdgeLayout("0", "2", 420, 100, 240, 100, IsOnCriticalPath: false, FlowRows: 100, StrokeWidth: 8, EstimatedRows: 100, ActualRows: null),
                new GraphEdgeLayout("0", "3", 420, 140, 240, 140, IsOnCriticalPath: false, FlowRows: null, StrokeWidth: 2.2, EstimatedRows: null, ActualRows: null)
            },
            Direction: GraphLayoutDirection.HorizontalSsms);

        var svg = _renderer.Render(layout, new GraphRenderOptions(90, true));
        var paths = XDocument.Parse(svg).Descendants()
            .Where(element => element.Name.LocalName == "path")
            .ToArray();
        var flowEdges = paths
            .Where(element => element.Attribute("data-edge-kind")?.Value == "flow")
            .ToDictionary(element => element.Attribute("data-row-source")!.Value, StringComparer.Ordinal);
        var criticalOutline = Assert.Single(paths, element => element.Attribute("data-edge-kind")?.Value == "critical-outline");

        Assert.Equal("#0f766e", flowEdges["actual"].Attribute("stroke")?.Value);
        Assert.Equal("url(#arrow-actual)", flowEdges["actual"].Attribute("marker-end")?.Value);
        Assert.Equal("#94a3b8", flowEdges["estimated"].Attribute("stroke")?.Value);
        Assert.Equal("url(#arrow-estimated)", flowEdges["estimated"].Attribute("marker-end")?.Value);
        Assert.Equal("#94a3b8", flowEdges["none"].Attribute("stroke")?.Value);
        Assert.Equal("url(#arrow)", flowEdges["none"].Attribute("marker-end")?.Value);
        Assert.Equal("#7c3aed", criticalOutline.Attribute("stroke")?.Value);
        Assert.Equal("7", criticalOutline.Attribute("stroke-width")?.Value);
        Assert.Null(criticalOutline.Attribute("marker-end"));
        Assert.True(Array.IndexOf(paths, criticalOutline) < Array.IndexOf(paths, flowEdges["actual"]));
    }

    [Fact]
    public void Render_SplitsTableAndIndexLabelsIntoSeparateTextRows()
    {
        var layout = new StatementGraphLayout(
            StatementId: "stmt4",
            StatementNode: null,
            Width: 360,
            Height: 220,
            Nodes: new[]
            {
                new GraphNodeLayout(
                    "4",
                    "Clustered Index Scan",
                    "Clustered Index Scan",
                    "[tpch].[dbo].[REGION] / [PK_REGION]",
                    "Clustered Index Scan",
                    "[tpch].[dbo].[REGION] / [PK_REGION]",
                    16,
                    32,
                    252,
                    152,
                    0.04m,
                    EstimatedRows: 1,
                    ActualRows: 1,
                    HasWarnings: false,
                    IsOnCriticalPath: false,
                    IsParallel: false)
            },
            StatementEdges: Array.Empty<GraphEdgeLayout>(),
            Edges: Array.Empty<GraphEdgeLayout>());

        var svg = _renderer.Render(layout, new GraphRenderOptions(20, true));
        var document = XDocument.Parse(svg);
        var textElements = document.Descendants()
            .Where(element => element.Name.LocalName == "text")
            .ToArray();

        var nodeIdLabel = Assert.Single(textElements, element => element.Value == "Node 4");
        var tableLabel = Assert.Single(textElements, element => element.Value == "[tpch].[dbo].[REGION]");
        var indexLabel = Assert.Single(textElements, element => element.Value == "[PK_REGION]");
        var nodeHeader = Assert.Single(
            document.Descendants(),
            element => element.Name.LocalName == "rect"
                && element.Attribute("x")?.Value == "17"
                && element.Attribute("y")?.Value == "33"
                && element.Attribute("height")?.Value == "24");

        Assert.Equal("142", nodeIdLabel.Attribute("x")?.Value);
        Assert.Equal("49", nodeIdLabel.Attribute("y")?.Value);
        Assert.Equal("14", nodeIdLabel.Attribute("font-size")?.Value);
        Assert.Equal("#ffffff", nodeHeader.Attribute("fill")?.Value);
        Assert.Equal("100", tableLabel.Attribute("y")?.Value);
        Assert.Equal("115", indexLabel.Attribute("y")?.Value);
        Assert.DoesNotContain("[tpch].[dbo].[REGION] / [PK_REGION]", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_CostEmphasisKeepsWarningAndCriticalPathStyling()
    {
        var layout = new StatementGraphLayout(
            StatementId: "stmt5",
            StatementNode: null,
            Width: 360,
            Height: 220,
            Nodes: new[]
            {
                new GraphNodeLayout(
                    "4",
                    "Index Scan",
                    "Index Scan",
                    "dbo.Orders",
                    "Index Scan",
                    "dbo.Orders",
                    16,
                    32,
                    240,
                    92,
                    0.45m,
                    EstimatedRows: 25000,
                    ActualRows: 30000,
                    HasWarnings: true,
                    IsOnCriticalPath: true,
                    IsParallel: false)
            },
            StatementEdges: Array.Empty<GraphEdgeLayout>(),
            Edges: new[]
            {
                new GraphEdgeLayout("0", "4", 120, 0, 120, 32, IsOnCriticalPath: true)
            });

        var svg = _renderer.Render(layout, new GraphRenderOptions(20, true));

        Assert.Contains("data-cost-emphasis=\"High\"", svg);
        Assert.Contains(">Cost</text>", svg);
        Assert.Contains(">45%</text>", svg);
        Assert.Contains("fill=\"#fff7ed\"", svg);
        Assert.Contains("fill=\"#fffbeb\"", svg);
        Assert.Contains("stroke=\"#ea580c\"", svg);
        Assert.Contains("stroke=\"#f59e0b\"", svg);
        Assert.Contains("stroke=\"#7c3aed\"", svg);
        Assert.Contains("data-edge-kind=\"critical-outline\"", svg);
    }
}

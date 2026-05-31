using MSSQLPlanViewer.Core.Rendering;

namespace MSSQLPlanViewer.Core.Tests;

public sealed class PlanTableMarkdownExporterTests
{
    [Fact]
    public void ToMarkdown_WritesHeaderSeparatorAndRow()
    {
        var rows = new[]
        {
            CreateRow(nodeId: "1", physicalOp: "Index Seek", objectName: "[dbo].[Orders]")
        };

        var markdown = PlanTableMarkdownExporter.ToMarkdown(rows);
        var lines = SplitLines(markdown);

        Assert.Equal(3, lines.Count);
        Assert.StartsWith("| NodeId | ParentNodeId | Depth | PhysicalOp", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("| --- |", lines[1], StringComparison.Ordinal);
        Assert.Contains("Index Seek", lines[2], StringComparison.Ordinal);
        Assert.Contains("[dbo].[Orders]", lines[2], StringComparison.Ordinal);
    }

    [Fact]
    public void ToMarkdown_EscapesPipesAndConvertsLineBreaks()
    {
        var rows = new[]
        {
            CreateRow(
                nodeId: "1",
                physicalOp: "Compute Scalar",
                objectName: "a|b",
                summary: "line1\r\nline2")
        };

        var markdown = PlanTableMarkdownExporter.ToMarkdown(rows);

        Assert.Contains("a\\|b", markdown, StringComparison.Ordinal);
        Assert.Contains("line1<br>line2", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void ToMarkdown_UsesInvariantCultureForNumbersAndEmptyForNulls()
    {
        var rows = new[]
        {
            CreateRow(
                nodeId: "1",
                physicalOp: "Index Seek",
                costRatio: 0.5m,
                estimatedSubtreeCost: 1234.567m,
                estimatedRows: 1000.25,
                actualRows: null)
        };

        var dataLine = SplitLines(PlanTableMarkdownExporter.ToMarkdown(rows))[2];

        Assert.Contains("0.5", dataLine, StringComparison.Ordinal);
        Assert.Contains("1234.567", dataLine, StringComparison.Ordinal);
        Assert.Contains("1000.25", dataLine, StringComparison.Ordinal);
        Assert.DoesNotContain("1,234", dataLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ToMarkdown_WithNoRows_WritesHeaderAndSeparatorOnly()
    {
        var markdown = PlanTableMarkdownExporter.ToMarkdown(Array.Empty<PlanTableRow>());
        var lines = SplitLines(markdown);

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("| NodeId", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("| --- |", lines[1], StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> SplitLines(string markdown) =>
        markdown.Split("\r\n", StringSplitOptions.None)
            .Where(line => line.Length > 0)
            .ToArray();

    private static PlanTableRow CreateRow(
        string nodeId,
        string physicalOp,
        string objectName = "",
        decimal costRatio = 0m,
        decimal? estimatedSubtreeCost = null,
        double? estimatedRows = null,
        double? actualRows = null,
        string summary = "") =>
        new(
            NodeId: nodeId,
            ParentNodeId: null,
            Depth: 0,
            HasChildren: false,
            PhysicalOp: physicalOp,
            LogicalOp: string.Empty,
            ObjectName: objectName,
            CostRatio: costRatio,
            EstimatedSubtreeCost: estimatedSubtreeCost,
            EstimatedCpuCost: null,
            EstimatedIoCost: null,
            EstimatedRows: estimatedRows,
            AverageRowSize: null,
            ActualRows: actualRows,
            ActualExecutions: null,
            ActualLogicalReads: null,
            ActualPhysicalReads: null,
            ActualCpuMs: null,
            ActualElapsedMs: null,
            WarningCount: 0,
            IsParallel: false,
            Summary: summary);
}

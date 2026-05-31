using MSSQLPlanViewer.Core.Rendering;

namespace MSSQLPlanViewer.Core.Tests;

public sealed class PlanTableCsvExporterTests
{
    [Fact]
    public void ToCsv_WritesHeaderAndRow()
    {
        var rows = new[]
        {
            CreateRow(nodeId: "1", physicalOp: "Index Seek", objectName: "[dbo].[Orders]")
        };

        var csv = PlanTableCsvExporter.ToCsv(rows);
        var lines = SplitLines(csv);

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("NodeId,ParentNodeId,Depth,PhysicalOp", lines[0], StringComparison.Ordinal);
        Assert.Contains("Index Seek", lines[1], StringComparison.Ordinal);
        Assert.Contains("[dbo].[Orders]", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void ToCsv_EscapesFieldsContainingCommasQuotesAndNewlines()
    {
        var rows = new[]
        {
            CreateRow(
                nodeId: "1",
                physicalOp: "Compute Scalar",
                objectName: "a,b",
                summary: "line1\r\nline2 \"quoted\"")
        };

        var csv = PlanTableCsvExporter.ToCsv(rows);

        Assert.Contains("\"a,b\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"line1\r\nline2 \"\"quoted\"\"\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void ToCsv_UsesInvariantCultureForNumbersAndEmptyForNulls()
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

        var csv = PlanTableCsvExporter.ToCsv(rows);
        var dataLine = SplitLines(csv)[1];

        Assert.Contains("0.5", dataLine, StringComparison.Ordinal);
        Assert.Contains("1234.567", dataLine, StringComparison.Ordinal);
        Assert.Contains("1000.25", dataLine, StringComparison.Ordinal);
        Assert.DoesNotContain("1,234", dataLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ToCsv_WithNoRows_WritesHeaderOnly()
    {
        var csv = PlanTableCsvExporter.ToCsv(Array.Empty<PlanTableRow>());
        var lines = SplitLines(csv);

        Assert.Single(lines);
        Assert.StartsWith("NodeId", lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ToCsv_NeutralizesFormulaInjectionInTextFields()
    {
        var rows = new[]
        {
            CreateRow(
                nodeId: "1",
                physicalOp: "Index Seek",
                objectName: "=danger",
                summary: "+1+2")
        };

        var csv = PlanTableCsvExporter.ToCsv(rows);
        var dataLine = SplitLines(csv)[1];

        Assert.Contains("'=danger", dataLine, StringComparison.Ordinal);
        Assert.Contains("'+1+2", dataLine, StringComparison.Ordinal);
        Assert.DoesNotContain(",=danger", dataLine, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> SplitLines(string csv) =>
        csv.Split("\r\n", StringSplitOptions.None)
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

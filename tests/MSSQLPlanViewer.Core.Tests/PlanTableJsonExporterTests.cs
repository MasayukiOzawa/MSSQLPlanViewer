using System.Text.Json;
using MSSQLPlanViewer.Core.Rendering;

namespace MSSQLPlanViewer.Core.Tests;

public sealed class PlanTableJsonExporterTests
{
    [Fact]
    public void ToJson_WritesCamelCaseRows()
    {
        var rows = new[]
        {
            CreateRow(
                nodeId: "1",
                parentNodeId: "0",
                hasChildren: true,
                physicalOp: "Index Seek",
                objectName: "[dbo].[Orders]")
        };

        using var document = JsonDocument.Parse(PlanTableJsonExporter.ToJson(rows));
        var root = document.RootElement;
        var row = root.EnumerateArray().Single();

        Assert.Equal(JsonValueKind.Array, root.ValueKind);
        Assert.Equal("1", row.GetProperty("nodeId").GetString());
        Assert.Equal("0", row.GetProperty("parentNodeId").GetString());
        Assert.True(row.GetProperty("hasChildren").GetBoolean());
        Assert.Equal("Index Seek", row.GetProperty("physicalOp").GetString());
        Assert.Equal("[dbo].[Orders]", row.GetProperty("objectName").GetString());
        Assert.False(row.TryGetProperty("NodeId", out _));
    }

    [Fact]
    public void ToJson_PreservesJsonTypesAndNulls()
    {
        var rows = new[]
        {
            CreateRow(
                nodeId: "1",
                physicalOp: "Index Seek",
                costRatio: 0.5m,
                estimatedSubtreeCost: null,
                estimatedRows: 1000.25,
                isParallel: true)
        };

        using var document = JsonDocument.Parse(PlanTableJsonExporter.ToJson(rows));
        var row = document.RootElement.EnumerateArray().Single();

        Assert.Equal(JsonValueKind.Number, row.GetProperty("depth").ValueKind);
        Assert.Equal(0, row.GetProperty("depth").GetInt32());
        Assert.Equal(JsonValueKind.Number, row.GetProperty("costRatio").ValueKind);
        Assert.Equal(0.5m, row.GetProperty("costRatio").GetDecimal());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("estimatedSubtreeCost").ValueKind);
        Assert.Equal(JsonValueKind.Number, row.GetProperty("estimatedRows").ValueKind);
        Assert.Equal(1000.25, row.GetProperty("estimatedRows").GetDouble());
        Assert.Equal(JsonValueKind.True, row.GetProperty("isParallel").ValueKind);
    }

    [Fact]
    public void ToJson_WithNoRows_WritesEmptyArray()
    {
        var json = PlanTableJsonExporter.ToJson(Array.Empty<PlanTableRow>());

        Assert.Equal("[]", json);
    }

    private static PlanTableRow CreateRow(
        string nodeId,
        string physicalOp,
        string? parentNodeId = null,
        bool hasChildren = false,
        string objectName = "",
        decimal costRatio = 0m,
        decimal? estimatedSubtreeCost = null,
        double? estimatedRows = null,
        bool isParallel = false,
        string summary = "") =>
        new(
            NodeId: nodeId,
            ParentNodeId: parentNodeId,
            Depth: 0,
            HasChildren: hasChildren,
            PhysicalOp: physicalOp,
            LogicalOp: string.Empty,
            ObjectName: objectName,
            CostRatio: costRatio,
            EstimatedSubtreeCost: estimatedSubtreeCost,
            EstimatedCpuCost: null,
            EstimatedIoCost: null,
            EstimatedRows: estimatedRows,
            AverageRowSize: null,
            ActualRows: null,
            ActualExecutions: null,
            ActualLogicalReads: null,
            ActualPhysicalReads: null,
            ActualCpuMs: null,
            ActualElapsedMs: null,
            WarningCount: 0,
            IsParallel: isParallel,
            Summary: summary);
}

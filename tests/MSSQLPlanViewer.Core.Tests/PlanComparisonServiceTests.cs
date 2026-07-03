using MSSQLPlanViewer.Core.Comparison;
using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Tests;

public sealed class PlanComparisonServiceTests
{
    private readonly PlanComparisonService _service = new();

    [Fact]
    public void Compare_ComputesDeltaAndPercentForCounts()
    {
        var planA = CreateDocument(
            CreateStatement("1", nodeCount: 3, warningCount: 1, subtreeCost: 10m, estimatedRows: 100));
        var planB = CreateDocument(
            CreateStatement("1", nodeCount: 6, warningCount: 0, subtreeCost: 25m, estimatedRows: 250));

        var result = _service.Compare(planA, planB);

        var operators = GetMetric(result, "Operators");
        Assert.Equal(3d, operators.ValueA);
        Assert.Equal(6d, operators.ValueB);
        Assert.Equal(3d, operators.Delta);
        Assert.Equal(100d, operators.DeltaPercent);
        Assert.True(operators.IsInteger);
    }

    [Fact]
    public void Compare_SumsStatementSubtreeCostsAcrossStatements()
    {
        var planA = CreateDocument(
            CreateStatement("1", subtreeCost: 10m),
            CreateStatement("2", subtreeCost: 5m));
        var planB = CreateDocument(
            CreateStatement("1", subtreeCost: 30m));

        var result = _service.Compare(planA, planB);

        var cost = GetMetric(result, "Sum of statement estimated subtree costs");
        Assert.Equal(15d, cost.ValueA);
        Assert.Equal(30d, cost.ValueB);
        Assert.Equal(15d, cost.Delta);
        Assert.False(cost.IsInteger);
    }

    [Fact]
    public void Compare_ReturnsNullValueWhenNoSubtreeCostAvailable()
    {
        var planA = CreateDocument(CreateStatement("1", subtreeCost: null));
        var planB = CreateDocument(CreateStatement("1", subtreeCost: 5m));

        var result = _service.Compare(planA, planB);

        var cost = GetMetric(result, "Sum of statement estimated subtree costs");
        Assert.Null(cost.ValueA);
        Assert.Equal(5d, cost.ValueB);
        Assert.Null(cost.Delta);
        Assert.Null(cost.DeltaPercent);
    }

    [Fact]
    public void Compare_DeltaPercentIsNullWhenBaselineIsZero()
    {
        var planA = CreateDocument(CreateStatement("1", warningCount: 0));
        var planB = CreateDocument(CreateStatement("1", warningCount: 2));

        var result = _service.Compare(planA, planB);

        var warnings = GetMetric(result, "Warnings");
        Assert.Equal(0d, warnings.ValueA);
        Assert.Equal(2d, warnings.ValueB);
        Assert.Equal(2d, warnings.Delta);
        Assert.Null(warnings.DeltaPercent);
    }

    [Fact]
    public void Compare_SumsQueryTimeStatsAcrossStatements()
    {
        var planA = CreateDocument(
            CreateStatement("1", cpuTime: 15, elapsedTime: 19),
            CreateStatement("2", cpuTime: 5, elapsedTime: 6));
        var planB = CreateDocument(
            CreateStatement("1", cpuTime: 40, elapsedTime: 50));

        var result = _service.Compare(planA, planB);

        var cpu = GetMetric(result, "Sum of query CPU time (ms)");
        Assert.Equal(20d, cpu.ValueA);
        Assert.Equal(40d, cpu.ValueB);
        Assert.Equal(20d, cpu.Delta);
        Assert.False(cpu.IsInteger);

        var elapsed = GetMetric(result, "Sum of query elapsed time (ms)");
        Assert.Equal(25d, elapsed.ValueA);
        Assert.Equal(50d, elapsed.ValueB);
        Assert.Equal(25d, elapsed.Delta);
    }

    [Fact]
    public void Compare_ReturnsNullQueryTimeStatsWhenAbsent()
    {
        var planA = CreateDocument(CreateStatement("1"));
        var planB = CreateDocument(CreateStatement("1", cpuTime: 10, elapsedTime: 12));

        var result = _service.Compare(planA, planB);

        var cpu = GetMetric(result, "Sum of query CPU time (ms)");
        Assert.Null(cpu.ValueA);
        Assert.Equal(10d, cpu.ValueB);
        Assert.Null(cpu.Delta);
        Assert.Null(cpu.DeltaPercent);
    }

    private static PlanComparisonMetric GetMetric(PlanComparisonResult result, string name) =>
        result.Metrics.Single(metric => metric.MetricName == name);

    private static ShowplanDocument CreateDocument(params StatementPlan[] statements) =>
        new(new ShowplanMetadata("urn:test", ShowplanSchemaVersion.SqlServer2022, "1.0", null), statements);

    private static StatementPlan CreateStatement(
        string statementId,
        int nodeCount = 0,
        int warningCount = 0,
        decimal? subtreeCost = null,
        double? estimatedRows = null,
        double? cpuTime = null,
        double? elapsedTime = null)
    {
        var nodes = Enumerable.Range(0, nodeCount)
            .Select(index => CreateNode($"{statementId}-{index}"))
            .ToArray();

        var warnings = Enumerable.Range(0, warningCount)
            .Select(_ => new PlanWarning("SpillToTempDb", null, null))
            .ToArray();

        var queryTimeStats = new List<PlanProperty>();
        if (cpuTime.HasValue)
        {
            queryTimeStats.Add(new PlanProperty("CpuTime", cpuTime.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        if (elapsedTime.HasValue)
        {
            queryTimeStats.Add(new PlanProperty("ElapsedTime", elapsedTime.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        var summary = new StatementPlanSummary(
            EstimatedSubtreeCost: subtreeCost,
            EstimatedRows: estimatedRows,
            CachedPlanSizeKb: null,
            CompileTimeMs: null,
            CompileCpuMs: null,
            CompileMemoryKb: null,
            EstimatedAvailableMemoryGrantKb: null,
            EstimatedMemoryGrantKb: null,
            QueryPlanProperties: Array.Empty<PlanProperty>(),
            QueryTimeStatsProperties: queryTimeStats.ToArray(),
            MemoryGrantInfoProperties: Array.Empty<PlanProperty>(),
            OptimizerHardwareDependentProperties: Array.Empty<PlanProperty>(),
            OptimizerStatsUsageEntries: Array.Empty<OptimizerStatsUsageEntry>(),
            MissingIndexesEntries: Array.Empty<MissingIndexEntry>(),
            WaitStatsEntries: Array.Empty<WaitStatEntry>(),
            AccessedObjectEntries: Array.Empty<AccessedObjectEntry>(),
            AccessedIndexEntries: Array.Empty<AccessedIndexEntry>(),
            SeekScanPredicateEntries: Array.Empty<SeekScanPredicateEntry>(),
            ParameterListEntries: Array.Empty<ParameterListEntry>());

        return new StatementPlan(
            statementId,
            "SELECT",
            "SELECT 1",
            summary,
            nodes,
            Array.Empty<PlanEdge>(),
            warnings,
            Array.Empty<string>());
    }

    private static PlanNode CreateNode(string nodeId) =>
        new(
            NodeId: nodeId,
            PhysicalOp: "Index Seek",
            LogicalOp: "Index Seek",
            EstimatedSubtreeCost: null,
            EstimatedCpuCost: null,
            EstimatedIoCost: null,
            EstimatedRows: null,
            AverageRowSize: null,
            IsParallel: false,
            ObjectReference: null,
            RuntimeMetrics: new PlanRuntimeMetrics(null, null, null, null, null, null, null, null),
            Warnings: Array.Empty<PlanWarning>(),
            Properties: Array.Empty<PlanProperty>(),
            XmlAttributes: Array.Empty<PlanProperty>(),
            DetailXmlAttributes: Array.Empty<PlanProperty>());
}

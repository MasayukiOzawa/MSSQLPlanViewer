using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Tests;

/// <summary>
/// Builds in-memory plan models so rendering services can be exercised with
/// synthetic, edge-case topologies (cycles, missing roots, disconnected nodes)
/// without depending on parsed sample files.
/// </summary>
internal static class TestPlanFactory
{
    public static PlanRuntimeMetrics NoMetrics { get; } =
        new(null, null, null, null, null, null, null, null);

    public static StatementPlanSummary EmptySummary { get; } =
        new(
            EstimatedSubtreeCost: null,
            EstimatedRows: null,
            CachedPlanSizeKb: null,
            CompileTimeMs: null,
            CompileCpuMs: null,
            CompileMemoryKb: null,
            EstimatedAvailableMemoryGrantKb: null,
            EstimatedMemoryGrantKb: null,
            QueryPlanProperties: Array.Empty<PlanProperty>(),
            QueryTimeStatsProperties: Array.Empty<PlanProperty>(),
            MemoryGrantInfoProperties: Array.Empty<PlanProperty>(),
            OptimizerHardwareDependentProperties: Array.Empty<PlanProperty>(),
            OptimizerStatsUsageEntries: Array.Empty<OptimizerStatsUsageEntry>(),
            MissingIndexesEntries: Array.Empty<MissingIndexEntry>(),
            WaitStatsEntries: Array.Empty<WaitStatEntry>(),
            AccessedObjectEntries: Array.Empty<AccessedObjectEntry>(),
            AccessedIndexEntries: Array.Empty<AccessedIndexEntry>(),
            SeekScanPredicateEntries: Array.Empty<SeekScanPredicateEntry>(),
            ParameterListEntries: Array.Empty<ParameterListEntry>());

    public static PlanNode Node(
        string nodeId,
        decimal? subtreeCost = null,
        string physicalOp = "Index Seek",
        string logicalOp = "Index Seek",
        bool isParallel = false,
        PlanObjectReference? objectReference = null,
        PlanRuntimeMetrics? runtimeMetrics = null,
        double? estimatedRows = null,
        decimal? estimatedCpuCost = null,
        decimal? estimatedIoCost = null,
        IReadOnlyList<PlanProperty>? properties = null,
        IReadOnlyList<PlanProperty>? xmlAttributes = null,
        IReadOnlyList<PlanProperty>? detailXmlAttributes = null,
        params PlanWarning[] warnings) =>
        new(
            NodeId: nodeId,
            PhysicalOp: physicalOp,
            LogicalOp: logicalOp,
            EstimatedSubtreeCost: subtreeCost,
            EstimatedCpuCost: estimatedCpuCost,
            EstimatedIoCost: estimatedIoCost,
            EstimatedRows: estimatedRows,
            AverageRowSize: null,
            IsParallel: isParallel,
            ObjectReference: objectReference,
            RuntimeMetrics: runtimeMetrics ?? NoMetrics,
            Warnings: warnings.Length == 0 ? Array.Empty<PlanWarning>() : warnings,
            Properties: properties ?? Array.Empty<PlanProperty>(),
            XmlAttributes: xmlAttributes ?? Array.Empty<PlanProperty>(),
            DetailXmlAttributes: detailXmlAttributes ?? Array.Empty<PlanProperty>());

    public static PlanEdge Edge(string fromNodeId, string toNodeId) =>
        new(fromNodeId, toNodeId);

    public static PlanWarning Warning(string name) =>
        new(name, null, null);

    public static StatementPlan Statement(
        IReadOnlyList<PlanNode> nodes,
        IReadOnlyList<PlanEdge>? edges = null,
        IReadOnlyList<string>? rootNodeIds = null,
        IReadOnlyList<PlanWarning>? warnings = null,
        StatementPlanSummary? summary = null,
        string statementId = "1",
        int batchNumber = 1,
        int statementOrdinal = 1) =>
        new(
            StatementId: statementId,
            StatementType: "SELECT",
            StatementText: "SELECT 1",
            Summary: summary ?? EmptySummary,
            Nodes: nodes,
            Edges: edges ?? Array.Empty<PlanEdge>(),
            Warnings: warnings ?? Array.Empty<PlanWarning>(),
            RootNodeIds: rootNodeIds ?? Array.Empty<string>())
        {
            BatchNumber = batchNumber,
            StatementOrdinal = statementOrdinal
        };
}

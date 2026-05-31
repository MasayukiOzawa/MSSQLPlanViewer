namespace MSSQLPlanViewer.Core.Models;

public sealed record StatementPlanSummary(
    decimal? EstimatedSubtreeCost,
    double? EstimatedRows,
    int? CachedPlanSizeKb,
    int? CompileTimeMs,
    int? CompileCpuMs,
    int? CompileMemoryKb,
    double? EstimatedAvailableMemoryGrantKb,
    double? EstimatedMemoryGrantKb,
    IReadOnlyList<PlanProperty> QueryPlanProperties,
    IReadOnlyList<PlanProperty> QueryTimeStatsProperties,
    IReadOnlyList<PlanProperty> MemoryGrantInfoProperties,
    IReadOnlyList<PlanProperty> OptimizerHardwareDependentProperties,
    IReadOnlyList<OptimizerStatsUsageEntry> OptimizerStatsUsageEntries,
    IReadOnlyList<MissingIndexEntry> MissingIndexesEntries,
    IReadOnlyList<WaitStatEntry> WaitStatsEntries);

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
    IReadOnlyList<WaitStatEntry> WaitStatsEntries,
    IReadOnlyList<AccessedObjectEntry> AccessedObjectEntries,
    IReadOnlyList<AccessedIndexEntry> AccessedIndexEntries,
    IReadOnlyList<SeekScanPredicateEntry> SeekScanPredicateEntries,
    IReadOnlyList<ParameterListEntry> ParameterListEntries)
{
    public IReadOnlyList<IReadOnlyList<PlanProperty>> ThreadStatProperties { get; init; } = Array.Empty<IReadOnlyList<PlanProperty>>();

    public IReadOnlyList<ImplicitConversionEntry> ImplicitConversionEntries { get; init; } = Array.Empty<ImplicitConversionEntry>();
}

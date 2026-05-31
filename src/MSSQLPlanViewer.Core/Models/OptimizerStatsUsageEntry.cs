namespace MSSQLPlanViewer.Core.Models;

public sealed record OptimizerStatsUsageEntry(
    string? Database,
    string? Schema,
    string? Table,
    string? Statistics,
    string? LastUpdate,
    string? StatisticsModificationCount,
    string? LastSample,
    string? SamplingPercent,
    string? Steps,
    string? Rows,
    string? UnfilteredRows,
    string? PersistedSamplePercent);

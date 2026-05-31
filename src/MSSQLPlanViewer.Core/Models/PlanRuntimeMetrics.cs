namespace MSSQLPlanViewer.Core.Models;

public sealed record PlanRuntimeMetrics(
    double? ActualRows,
    double? ActualExecutions,
    double? ActualLogicalReads,
    double? ActualPhysicalReads,
    double? ActualCpuMs,
    double? ActualElapsedMs,
    double? ActualRebinds,
    double? ActualRewinds);

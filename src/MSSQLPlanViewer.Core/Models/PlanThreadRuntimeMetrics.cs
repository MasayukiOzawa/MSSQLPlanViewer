namespace MSSQLPlanViewer.Core.Models;

public sealed record PlanThreadRuntimeMetrics(
    int ThreadId,
    double? ActualRows,
    double? ActualRowsRead,
    double? ActualExecutions,
    double? ActualLogicalReads,
    double? ActualPhysicalReads,
    double? ActualCpuMs,
    double? ActualElapsedMs,
    double? ActualRebinds,
    double? ActualRewinds);

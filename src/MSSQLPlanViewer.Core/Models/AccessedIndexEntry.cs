namespace MSSQLPlanViewer.Core.Models;

public sealed record AccessedIndexEntry(
    string NodeId,
    string PhysicalOp,
    string LogicalOp,
    string? Database,
    string? Schema,
    string? Table,
    string Index,
    string? IndexKind,
    double? EstimatedRows,
    decimal? EstimatedIoCost,
    double? ActualRows,
    double? ActualLogicalReads,
    double? ActualPhysicalReads);

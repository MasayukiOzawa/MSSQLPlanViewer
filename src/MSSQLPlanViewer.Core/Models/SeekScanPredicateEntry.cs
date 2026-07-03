namespace MSSQLPlanViewer.Core.Models;

public sealed record SeekScanPredicateEntry(
    string NodeId,
    string PhysicalOp,
    string LogicalOp,
    string? Database,
    string? Schema,
    string? Table,
    string? Index,
    string? IndexKind,
    string? Predicate,
    string? SeekPredicate);

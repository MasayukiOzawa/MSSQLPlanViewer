namespace MSSQLPlanViewer.Core.Models;

public sealed record ImplicitConversionEntry(
    string NodeId,
    string PhysicalOp,
    string LogicalOp,
    string? Database,
    string? Schema,
    string? Table,
    string? Index,
    string? IndexKind,
    string Source,
    string Expression);

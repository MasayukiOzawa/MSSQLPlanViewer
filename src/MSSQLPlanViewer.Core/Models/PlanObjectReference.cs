namespace MSSQLPlanViewer.Core.Models;

public sealed record PlanObjectReference(
    string? Database,
    string? Schema,
    string? Table,
    string? Index,
    string? Alias,
    string? IndexKind,
    string? Storage);

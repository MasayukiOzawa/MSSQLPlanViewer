namespace MSSQLPlanViewer.Core.Models;

public sealed record AccessedObjectEntry(
    string? Database,
    string? Schema,
    string Table);

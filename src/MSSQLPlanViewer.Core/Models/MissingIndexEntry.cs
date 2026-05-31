namespace MSSQLPlanViewer.Core.Models;

public sealed record MissingIndexEntry(
    string ObjectName,
    string? Impact,
    string? EqualityColumns,
    string? InequalityColumns,
    string? IncludeColumns);

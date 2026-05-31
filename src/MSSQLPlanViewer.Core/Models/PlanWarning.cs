namespace MSSQLPlanViewer.Core.Models;

public sealed record PlanWarning(
    string Name,
    string? Value,
    string? Details);

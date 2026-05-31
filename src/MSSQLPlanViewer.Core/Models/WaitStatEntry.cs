namespace MSSQLPlanViewer.Core.Models;

public sealed record WaitStatEntry(
    string WaitType,
    double? WaitTimeMs,
    double? WaitCount);

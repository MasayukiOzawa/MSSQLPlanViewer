namespace MSSQLPlanViewer.Core.Models;

public sealed record ParameterListEntry(
    string Parameter,
    string? DataType,
    string? CompiledValue,
    string? RuntimeValue,
    string? IsNullable);

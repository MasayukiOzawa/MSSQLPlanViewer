namespace MSSQLPlanViewer.Core.Models;

public sealed record ShowplanMetadata(
    string NamespaceUri,
    ShowplanSchemaVersion SchemaVersion,
    string? Version,
    string? Build);

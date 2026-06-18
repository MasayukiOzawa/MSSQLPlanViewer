using System.Text.Json;

namespace MSSQLPlanViewer.Core.Rendering;

/// <summary>
/// Exports projected plan table rows as a typed JSON array.
/// </summary>
public static class PlanTableJsonExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string ToJson(IReadOnlyList<PlanTableRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        return JsonSerializer.Serialize(rows, JsonOptions);
    }
}

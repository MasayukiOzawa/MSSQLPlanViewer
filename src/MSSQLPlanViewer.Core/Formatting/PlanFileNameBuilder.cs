namespace MSSQLPlanViewer.Core.Formatting;

public static class PlanFileNameBuilder
{
    public static string BuildBaseName(string baseName, string? statementId, string fallbackBaseName)
    {
        var raw = string.IsNullOrWhiteSpace(statementId)
            ? baseName
            : $"{baseName}-stmt{statementId}";
        var safe = Sanitize(raw);

        return string.IsNullOrWhiteSpace(safe)
            ? fallbackBaseName
            : safe;
    }

    public static string BuildFileName(string baseName, string? statementId, string extension, string fallbackBaseName) =>
        $"{BuildBaseName(baseName, statementId, fallbackBaseName)}.{extension}";

    private static string Sanitize(string value) =>
        new string(value.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray())
            .Trim('-');
}

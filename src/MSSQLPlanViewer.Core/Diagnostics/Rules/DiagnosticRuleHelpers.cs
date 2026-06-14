using System.Globalization;
using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Diagnostics.Rules;

internal static class DiagnosticRuleHelpers
{
    public static bool NameEquals(string? value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    public static bool IsNamedProperty(PlanProperty property, string name) =>
        NameEquals(property.Name, name);

    public static string FormatNumber(double? value) =>
        value.HasValue
            ? value.Value.ToString("#,0.###", CultureInfo.InvariantCulture)
            : "n/a";

    public static string FormatRatio(double value) =>
        value.ToString("#,0.##", CultureInfo.InvariantCulture);

    public static double? ParseDouble(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Replace(",", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("%", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return double.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    public static double? GetPropertyDouble(IReadOnlyList<PlanProperty> properties, string name) =>
        ParseDouble(GetPropertyValue(properties, name));

    public static string? GetPropertyValue(IReadOnlyList<PlanProperty> properties, string name) =>
        properties.FirstOrDefault(property => IsNamedProperty(property, name))?.Value;

    public static double? GetXmlAttributeDouble(IReadOnlyList<PlanProperty> properties, string suffix) =>
        ParseDouble(GetXmlAttributeValue(properties, suffix));

    public static string? GetXmlAttributeValue(IReadOnlyList<PlanProperty> properties, string suffix) =>
        properties.FirstOrDefault(property =>
            property.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))?.Value;

    public static bool HasWarning(IEnumerable<PlanWarning> warnings, string name) =>
        warnings.Any(warning => NameEquals(warning.Name, name));

    public static bool HasPlanAffectingConvert(StatementPlan statement) =>
        HasWarning(statement.Warnings, "PlanAffectingConvert")
        || statement.Nodes.Any(node => HasWarning(node.Warnings, "PlanAffectingConvert"));

    public static double? ExtractNamedDouble(string? text, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var equalsIndex = part.IndexOf('=');
            if (equalsIndex <= 0)
            {
                continue;
            }

            var key = part[..equalsIndex].Trim();
            if (!NameEquals(key, name))
            {
                continue;
            }

            return ParseDouble(part[(equalsIndex + 1)..]);
        }

        return null;
    }

    public static PlanProperty Evidence(string name, double? value) =>
        new(name, FormatNumber(value));

    public static PlanProperty Evidence(string name, string? value) =>
        new(name, string.IsNullOrWhiteSpace(value) ? "n/a" : value);

    public static bool IsScan(string physicalOp) =>
        NameEquals(physicalOp, "Table Scan")
        || NameEquals(physicalOp, "Clustered Index Scan")
        || NameEquals(physicalOp, "Index Scan");

    public static bool IsScanOrSeek(string physicalOp) =>
        IsScan(physicalOp)
        || physicalOp.Contains("Seek", StringComparison.OrdinalIgnoreCase);
}

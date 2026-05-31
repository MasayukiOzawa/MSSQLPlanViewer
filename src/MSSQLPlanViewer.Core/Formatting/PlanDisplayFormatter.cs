using System.Globalization;
using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Formatting;

public static class PlanDisplayFormatter
{
    public static string FormatCost(decimal? value) =>
        value.HasValue ? value.Value.ToString("#,0.####", CultureInfo.InvariantCulture) : "n/a";

    public static string FormatPercent(decimal value) =>
        $"{Math.Round(value * 100m, 0, MidpointRounding.AwayFromZero):0}%";

    public static string FormatNumber(double? value)
    {
        if (!value.HasValue)
        {
            return "n/a";
        }

        var absoluteValue = Math.Abs(value.Value);

        return absoluteValue switch
        {
            >= 1000 => value.Value.ToString("N0", CultureInfo.InvariantCulture),
            >= 100 => value.Value.ToString("N2", CultureInfo.InvariantCulture),
            _ => value.Value.ToString("0.###", CultureInfo.InvariantCulture)
        };
    }

    public static string FormatObjectName(PlanObjectReference? objectReference)
    {
        if (objectReference is null)
        {
            return "n/a";
        }

        var pathParts = new[]
        {
            objectReference.Database,
            objectReference.Schema,
            objectReference.Table
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => $"[{value}]")
        .ToList();

        if (pathParts.Count == 0 && !string.IsNullOrWhiteSpace(objectReference.Alias))
        {
            pathParts.Add(objectReference.Alias!);
        }

        var objectName = pathParts.Count > 0 ? string.Join(".", pathParts) : "n/a";

        if (!string.IsNullOrWhiteSpace(objectReference.Index))
        {
            objectName += $" / [{objectReference.Index}]";
        }

        if (!string.IsNullOrWhiteSpace(objectReference.IndexKind))
        {
            objectName += $" ({objectReference.IndexKind})";
        }

        return objectName;
    }

    public static string FormatWarningSummary(IEnumerable<PlanWarning> warnings)
    {
        var names = warnings
            .Select(warning => warning.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return names.Length == 0 ? "None" : string.Join(", ", names);
    }

    public static bool TryGetSafeHttpUrl(string? value, out string safeUrl)
    {
        safeUrl = string.Empty;

        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        safeUrl = uri.AbsoluteUri;
        return true;
    }
}

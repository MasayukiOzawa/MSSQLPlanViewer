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

    public static string FormatNumericText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var trimmed = value.Trim();
        if (!decimal.TryParse(
            trimmed,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out var numericValue))
        {
            return value;
        }

        if (trimmed.Contains('e', StringComparison.OrdinalIgnoreCase))
        {
            return numericValue.ToString("#,0.#############################", CultureInfo.InvariantCulture);
        }

        var sign = string.Empty;
        if (trimmed[0] is '-' or '+')
        {
            sign = trimmed[0] == '-' ? "-" : string.Empty;
            trimmed = trimmed[1..];
        }

        var decimalPointIndex = trimmed.IndexOf('.');
        var integerPart = decimalPointIndex >= 0 ? trimmed[..decimalPointIndex] : trimmed;
        var fractionalPart = decimalPointIndex >= 0 ? trimmed[decimalPointIndex..] : string.Empty;
        integerPart = integerPart.Replace(",", string.Empty, StringComparison.Ordinal);

        if (integerPart.Length == 0)
        {
            integerPart = "0";
        }
        else
        {
            integerPart = integerPart.TrimStart('0');
            if (integerPart.Length == 0)
            {
                integerPart = "0";
            }
        }

        if (numericValue == 0)
        {
            sign = string.Empty;
        }

        return sign + GroupIntegerDigits(integerPart) + fractionalPart;
    }

    private static string GroupIntegerDigits(string digits)
    {
        var firstGroupLength = digits.Length % 3;
        if (firstGroupLength == 0)
        {
            firstGroupLength = 3;
        }

        var groups = new List<string> { digits[..firstGroupLength] };
        for (var index = firstGroupLength; index < digits.Length; index += 3)
        {
            groups.Add(digits.Substring(index, 3));
        }

        return string.Join(",", groups);
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

    public static string FormatQualifiedTableName(string? database, string? schema, string? table)
    {
        var parts = new[] { database, schema, table }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        return parts.Length == 0 ? "n/a" : string.Join(".", parts);
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

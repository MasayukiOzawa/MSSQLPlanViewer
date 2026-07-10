using System.Globalization;
using System.Xml.Linq;
using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Parsing;

/// <summary>
/// Low-level helpers for reading namespace-agnostic attributes and elements from Showplan XML.
/// </summary>
internal static class ShowplanXml
{
    public static bool HasLocalName(XElement element, string localName) =>
        string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    public static XElement? GetChild(XElement element, string localName) =>
        element.Elements().FirstOrDefault(child => HasLocalName(child, localName));

    public static IEnumerable<XElement> GetChildren(XElement element, string localName) =>
        element.Elements().Where(child => HasLocalName(child, localName));

    public static string? GetAttribute(XElement element, string name) =>
        element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    public static decimal? GetDecimalAttribute(XElement element, string name) =>
        decimal.TryParse(GetAttribute(element, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public static double? GetDoubleAttribute(XElement element, string name) =>
        double.TryParse(GetAttribute(element, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public static int? GetIntAttribute(XElement element, string name) =>
        int.TryParse(GetAttribute(element, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    public static double? GetFirstDoubleAttribute(XElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (double.TryParse(GetAttribute(element, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }
        }

        return null;
    }

    public static double? SumAttributes(IEnumerable<XElement> elements, params string[] names)
    {
        double total = 0;
        var found = false;

        foreach (var element in elements)
        {
            foreach (var name in names)
            {
                if (double.TryParse(GetAttribute(element, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                {
                    total += value;
                    found = true;
                    break;
                }
            }
        }

        return found ? total : null;
    }

    /// <summary>
    /// Returns descendants with the given local name that belong to <paramref name="ownerRelOp"/>
    /// itself, i.e. are not nested inside a child RelOp.
    /// </summary>
    public static IEnumerable<XElement> GetOwnedDescendants(XElement ownerRelOp, string localName) =>
        ownerRelOp
            .Descendants()
            .Where(element =>
                HasLocalName(element, localName) &&
                ReferenceEquals(GetNearestAncestorByLocalName(element, "RelOp"), ownerRelOp));

    public static XElement? GetNearestAncestorByLocalName(XElement element, string localName)
    {
        var current = element.Parent;
        while (current is not null)
        {
            if (HasLocalName(current, localName))
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
    }

    public static string FormatColumnReference(XElement columnReferenceElement)
    {
        var alias = GetAttribute(columnReferenceElement, "Alias");
        var table = GetAttribute(columnReferenceElement, "Table");
        var column = GetAttribute(columnReferenceElement, "Column");

        var prefix = alias ?? table;
        if (!string.IsNullOrWhiteSpace(prefix) && !string.IsNullOrWhiteSpace(column))
        {
            return $"{prefix}.{column}";
        }

        return column ?? string.Empty;
    }

    /// <summary>
    /// Formats every ColumnReference descendant of <paramref name="container"/>, skipping blanks.
    /// </summary>
    public static IEnumerable<string> SelectColumnReferenceTexts(XElement container) =>
        container
            .Descendants()
            .Where(element => HasLocalName(element, "ColumnReference"))
            .Select(FormatColumnReference)
            .Where(value => !string.IsNullOrWhiteSpace(value));

    /// <summary>
    /// Returns the ScalarString attribute of every ScalarOperator descendant, skipping blanks.
    /// </summary>
    public static IEnumerable<string> SelectScalarStrings(XElement container) =>
        container
            .Descendants()
            .Where(element => HasLocalName(element, "ScalarOperator"))
            .Select(scalarElement => GetAttribute(scalarElement, "ScalarString"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>();

    public static string ExtractScalarStringOrDetails(XElement element)
    {
        var scalarString = element.Descendants()
            .FirstOrDefault(descendant => HasLocalName(descendant, "ScalarOperator"))
            ?.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, "ScalarString", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return !string.IsNullOrWhiteSpace(scalarString)
            ? scalarString
            : BuildDetails(element);
    }

    public static string BuildDetails(XElement element)
    {
        var attributeText = FormatAttributes(element);

        if (!string.IsNullOrWhiteSpace(attributeText))
        {
            return attributeText;
        }

        return string.IsNullOrWhiteSpace(element.Value) ? "true" : element.Value.Trim();
    }

    public static string FormatAttributes(XElement element) =>
        string.Join(
            ", ",
            element.Attributes().Select(attribute => $"{attribute.Name.LocalName}={attribute.Value}"));

    public static IReadOnlyList<PlanProperty> BuildAttributeProperties(XElement? element)
    {
        if (element is null)
        {
            return Array.Empty<PlanProperty>();
        }

        return element.Attributes()
            .Select(attribute => new PlanProperty(attribute.Name.LocalName, attribute.Value))
            .ToArray();
    }
}

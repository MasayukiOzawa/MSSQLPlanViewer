using System.Xml.Linq;

namespace MSSQLPlanViewer.Core.Parsing;

internal static class ShowplanXmlElement
{
    public static bool HasLocalName(XElement element, string localName) =>
        string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    public static string? GetAttribute(XElement element, string name) =>
        element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value;
}

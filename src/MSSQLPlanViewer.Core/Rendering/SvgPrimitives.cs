using System.Globalization;
using System.Xml.Linq;

namespace MSSQLPlanViewer.Core.Rendering;

/// <summary>
/// Factory helpers for basic SVG elements shared by the plan graph renderers.
/// </summary>
internal static class SvgPrimitives
{
    public static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    public static XElement Text(XNamespace ns, double x, double y, string value, string fontSize, string fill, string fontWeight) =>
        new(
            ns + "text",
            new XAttribute("x", Format(x)),
            new XAttribute("y", Format(y)),
            new XAttribute("font-size", fontSize),
            new XAttribute("font-weight", fontWeight),
            new XAttribute("fill", fill),
            value);

    public static XElement Rect(XNamespace ns, double x, double y, double width, double height, double radius) =>
        new(
            ns + "rect",
            new XAttribute("x", Format(x)),
            new XAttribute("y", Format(y)),
            new XAttribute("width", Format(width)),
            new XAttribute("height", Format(height)),
            new XAttribute("rx", Format(radius)),
            new XAttribute("ry", Format(radius)));

    public static XElement Circle(XNamespace ns, double cx, double cy, double r, string? fill = null, string? stroke = null)
    {
        var element = new XElement(
            ns + "circle",
            new XAttribute("cx", Format(cx)),
            new XAttribute("cy", Format(cy)),
            new XAttribute("r", Format(r)));
        if (fill is not null)
        {
            element.SetAttributeValue("fill", fill);
        }

        if (stroke is not null)
        {
            element.SetAttributeValue("stroke", stroke);
        }

        return element;
    }

    public static XElement Ellipse(XNamespace ns, double cx, double cy, double rx, double ry) =>
        new(
            ns + "ellipse",
            new XAttribute("cx", Format(cx)),
            new XAttribute("cy", Format(cy)),
            new XAttribute("rx", Format(rx)),
            new XAttribute("ry", Format(ry)));

    public static XElement Line(XNamespace ns, double x1, double y1, double x2, double y2) =>
        new(
            ns + "line",
            new XAttribute("x1", Format(x1)),
            new XAttribute("y1", Format(y1)),
            new XAttribute("x2", Format(x2)),
            new XAttribute("y2", Format(y2)));

    public static XElement Path(XNamespace ns, string d) =>
        new(
            ns + "path",
            new XAttribute("d", d));
}

using System.Xml.Linq;
using static MSSQLPlanViewer.Core.Rendering.SvgPrimitives;

namespace MSSQLPlanViewer.Core.Rendering;

/// <summary>
/// Draws the operator glyphs, statement icon and parallelism badge used in plan graph SVGs.
/// </summary>
internal static class OperatorIconSvgBuilder
{
    public static IEnumerable<XElement> BuildIcon(XNamespace ns, OperatorIconKind kind, double x, double y, string accentFill)
    {
        switch (kind)
        {
            case OperatorIconKind.Seek:
                yield return Circle(ns, x + 7, y + 7, 6);
                yield return Line(ns, x + 12, y + 12, x + 19, y + 19);
                yield return Line(ns, x + 3, y + 7, x + 11, y + 7);
                yield return Line(ns, x + 7, y + 3, x + 7, y + 11);
                yield break;
            case OperatorIconKind.Scan:
                yield return Rect(ns, x, y, 20, 18, 3);
                yield return Line(ns, x + 7, y, x + 7, y + 18);
                yield return Line(ns, x + 14, y, x + 14, y + 18);
                yield return Line(ns, x, y + 6, x + 20, y + 6);
                yield return Line(ns, x, y + 12, x + 20, y + 12);
                yield break;
            case OperatorIconKind.NestedLoops:
                yield return Circle(ns, x + 7, y + 9, 5);
                yield return Circle(ns, x + 15, y + 9, 5);
                yield return Path(ns, $"M {Format(x + 3)} {Format(y + 18)} C {Format(x + 6)} {Format(y + 13)}, {Format(x + 16)} {Format(y + 13)}, {Format(x + 19)} {Format(y + 18)}");
                yield break;
            case OperatorIconKind.MergeJoin:
                yield return Path(ns, $"M {Format(x)} {Format(y + 4)} L {Format(x + 8)} {Format(y + 12)} L {Format(x)} {Format(y + 20)}");
                yield return Path(ns, $"M {Format(x + 20)} {Format(y + 4)} L {Format(x + 12)} {Format(y + 12)} L {Format(x + 20)} {Format(y + 20)}");
                yield return Line(ns, x + 8, y + 12, x + 12, y + 12);
                yield break;
            case OperatorIconKind.HashMatch:
                yield return Path(ns, $"M {Format(x + 10)} {Format(y)} L {Format(x + 20)} {Format(y + 6)} L {Format(x + 20)} {Format(y + 18)} L {Format(x + 10)} {Format(y + 24)} L {Format(x)} {Format(y + 18)} L {Format(x)} {Format(y + 6)} Z");
                yield return Line(ns, x + 6, y + 6, x + 14, y + 18);
                yield return Line(ns, x + 14, y + 6, x + 6, y + 18);
                yield break;
            case OperatorIconKind.Sort:
                yield return Line(ns, x, y + 5, x + 17, y + 5);
                yield return Line(ns, x, y + 12, x + 12, y + 12);
                yield return Line(ns, x, y + 19, x + 7, y + 19);
                yield return Path(ns, $"M {Format(x + 19)} {Format(y + 3)} L {Format(x + 23)} {Format(y + 5)} L {Format(x + 19)} {Format(y + 7)}");
                yield return Path(ns, $"M {Format(x + 14)} {Format(y + 10)} L {Format(x + 18)} {Format(y + 12)} L {Format(x + 14)} {Format(y + 14)}");
                yield return Path(ns, $"M {Format(x + 9)} {Format(y + 17)} L {Format(x + 13)} {Format(y + 19)} L {Format(x + 9)} {Format(y + 21)}");
                yield break;
            case OperatorIconKind.Filter:
                yield return Path(ns, $"M {Format(x)} {Format(y + 2)} L {Format(x + 20)} {Format(y + 2)} L {Format(x + 12)} {Format(y + 12)} L {Format(x + 12)} {Format(y + 20)} L {Format(x + 8)} {Format(y + 18)} L {Format(x + 8)} {Format(y + 12)} Z");
                yield break;
            case OperatorIconKind.ComputeScalar:
                yield return Rect(ns, x, y, 20, 20, 4);
                yield return Line(ns, x + 6, y + 10, x + 14, y + 10);
                yield return Line(ns, x + 10, y + 6, x + 10, y + 14);
                yield return Circle(ns, x + 16, y + 16, 1.5, accentFill, "none");
                yield break;
            case OperatorIconKind.Parallelism:
                yield return Path(ns, $"M {Format(x + 10)} {Format(y + 2)} L {Format(x + 10)} {Format(y + 10)}");
                yield return Path(ns, $"M {Format(x + 10)} {Format(y + 10)} L {Format(x + 4)} {Format(y + 18)}");
                yield return Path(ns, $"M {Format(x + 10)} {Format(y + 10)} L {Format(x + 16)} {Format(y + 18)}");
                yield return Path(ns, $"M {Format(x + 10)} {Format(y + 2)} L {Format(x + 7)} {Format(y + 5)}");
                yield return Path(ns, $"M {Format(x + 10)} {Format(y + 2)} L {Format(x + 13)} {Format(y + 5)}");
                yield break;
            case OperatorIconKind.Aggregate:
                yield return Rect(ns, x, y + 12, 4, 8, 1);
                yield return Rect(ns, x + 7, y + 8, 4, 12, 1);
                yield return Rect(ns, x + 14, y + 4, 4, 16, 1);
                yield break;
            case OperatorIconKind.KeyLookup:
                yield return Circle(ns, x + 7, y + 9, 4.5);
                yield return Line(ns, x + 11, y + 9, x + 20, y + 9);
                yield return Line(ns, x + 16, y + 9, x + 16, y + 14);
                yield return Line(ns, x + 19, y + 9, x + 19, y + 12);
                yield break;
            case OperatorIconKind.Spool:
                yield return Ellipse(ns, x + 10, y + 4, 8, 3.5);
                yield return Path(ns, $"M {Format(x + 2)} {Format(y + 4)} V {Format(y + 18)} C {Format(x + 2)} {Format(y + 22)}, {Format(x + 18)} {Format(y + 22)}, {Format(x + 18)} {Format(y + 18)} V {Format(y + 4)}");
                yield return Ellipse(ns, x + 10, y + 18, 8, 3.5);
                yield break;
            case OperatorIconKind.ConstantScan:
                yield return Rect(ns, x, y, 20, 20, 4);
                yield return Line(ns, x + 5, y + 7, x + 15, y + 7);
                yield return Line(ns, x + 5, y + 12, x + 15, y + 12);
                yield break;
            default:
                yield return Rect(ns, x, y, 20, 20, 4);
                yield return Path(ns, $"M {Format(x + 5)} {Format(y + 14)} L {Format(x + 10)} {Format(y + 5)} L {Format(x + 15)} {Format(y + 14)} Z");
                yield break;
        }
    }

    public static XElement BuildStatementIcon(XNamespace ns, double x, double y, string accentFill)
    {
        var group = new XElement(
            ns + "g",
            new XAttribute("stroke", accentFill),
            new XAttribute("fill", "none"),
            new XAttribute("stroke-linecap", "round"),
            new XAttribute("stroke-linejoin", "round"),
            new XAttribute("stroke-width", "2"));
        group.Add(Rect(ns, x, y, 20, 20, 3));
        group.Add(Line(ns, x + 5, y + 6, x + 15, y + 6));
        group.Add(Line(ns, x + 5, y + 10, x + 15, y + 10));
        group.Add(Line(ns, x + 5, y + 14, x + 12, y + 14));
        return group;
    }

    public static XElement BuildParallelBadge(XNamespace ns, double x, double y, string nodeId) =>
        new(
            ns + "g",
            new XAttribute("data-parallel-badge-for", nodeId),
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(x)),
                new XAttribute("y", Format(y)),
                new XAttribute("width", "27"),
                new XAttribute("height", "25"),
                new XAttribute("rx", "7"),
                new XAttribute("ry", "7"),
                new XAttribute("fill", "#fde68a"),
                new XAttribute("stroke", "#f59e0b"),
                new XAttribute("stroke-width", "1.2")),
            ParallelBadgeLine(ns, x + 7, y + 6, x + 7, y + 16),
            ParallelBadgeLine(ns, x + 13.5, y + 4, x + 13.5, y + 16),
            ParallelBadgeLine(ns, x + 20, y + 6, x + 20, y + 16),
            new XElement(
                ns + "path",
                new XAttribute("d", $"M {Format(x + 5)} {Format(y + 18)} C {Format(x + 10)} {Format(y + 22)}, {Format(x + 17)} {Format(y + 22)}, {Format(x + 22)} {Format(y + 18)}"),
                new XAttribute("fill", "none"),
                new XAttribute("stroke", "#d97706"),
                new XAttribute("stroke-width", "1.8"),
                new XAttribute("stroke-linecap", "round"),
                new XAttribute("stroke-linejoin", "round")),
            new XElement(
                ns + "path",
                new XAttribute("d", $"M {Format(x + 19)} {Format(y + 16)} L {Format(x + 23)} {Format(y + 18)} L {Format(x + 19)} {Format(y + 20)}"),
                new XAttribute("fill", "none"),
                new XAttribute("stroke", "#d97706"),
                new XAttribute("stroke-width", "1.8"),
                new XAttribute("stroke-linecap", "round"),
                new XAttribute("stroke-linejoin", "round")));

    public static XElement BuildImplicitConversionBadge(XNamespace ns, double x, double y, string nodeId) =>
        new(
            ns + "g",
            new XAttribute("data-implicit-conversion-for", nodeId),
            new XElement(ns + "title", "Implicit conversion"),
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(x)),
                new XAttribute("y", Format(y)),
                new XAttribute("width", "27"),
                new XAttribute("height", "25"),
                new XAttribute("rx", "7"),
                new XAttribute("ry", "7"),
                new XAttribute("fill", "#fef3c7"),
                new XAttribute("stroke", "#f59e0b"),
                new XAttribute("stroke-width", "1.2")),
            new XElement(
                ns + "path",
                new XAttribute("d", $"M {Format(x + 7)} {Format(y + 8)} H {Format(x + 18)} L {Format(x + 15)} {Format(y + 5)} M {Format(x + 18)} {Format(y + 8)} L {Format(x + 15)} {Format(y + 11)}"),
                new XAttribute("fill", "none"),
                new XAttribute("stroke", "#d97706"),
                new XAttribute("stroke-width", "1.8"),
                new XAttribute("stroke-linecap", "round"),
                new XAttribute("stroke-linejoin", "round")),
            new XElement(
                ns + "path",
                new XAttribute("d", $"M {Format(x + 20)} {Format(y + 16)} H {Format(x + 9)} L {Format(x + 12)} {Format(y + 13)} M {Format(x + 9)} {Format(y + 16)} L {Format(x + 12)} {Format(y + 19)}"),
                new XAttribute("fill", "none"),
                new XAttribute("stroke", "#d97706"),
                new XAttribute("stroke-width", "1.8"),
                new XAttribute("stroke-linecap", "round"),
                new XAttribute("stroke-linejoin", "round")),
            new XElement(
                ns + "text",
                new XAttribute("x", Format(x + 13.5)),
                new XAttribute("y", Format(y + 15)),
                new XAttribute("font-size", "9"),
                new XAttribute("font-weight", "800"),
                new XAttribute("fill", "#92400e"),
                new XAttribute("text-anchor", "middle"),
                "T"));

    private static XElement ParallelBadgeLine(XNamespace ns, double x1, double y1, double x2, double y2) =>
        new(
            ns + "line",
            new XAttribute("x1", Format(x1)),
            new XAttribute("y1", Format(y1)),
            new XAttribute("x2", Format(x2)),
            new XAttribute("y2", Format(y2)),
            new XAttribute("stroke", "#2563eb"),
            new XAttribute("stroke-width", "1.8"),
            new XAttribute("stroke-linecap", "round"));
}

using System.Globalization;
using System.Xml.Linq;
using MSSQLPlanViewer.Core.Formatting;

namespace MSSQLPlanViewer.Core.Rendering;

public sealed class PlanGraphSvgRenderer : IPlanGraphSvgRenderer
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    public string Render(StatementGraphLayout layout, GraphRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(layout);

        var effectiveOptions = options ?? new GraphRenderOptions();
        var width = Math.Max(1d, layout.Width);
        var height = Math.Max(1d, layout.Height);
        var svg = BuildSvg(layout, width, height, effectiveOptions);

        var document = new XDocument(new XDeclaration("1.0", "utf-8", null), svg);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement BuildSvg(StatementGraphLayout layout, double width, double height, GraphRenderOptions options)
    {
        var ns = XNamespace.Get(SvgNamespace);
        var root = new XElement(
            ns + "svg",
            new XAttribute("xmlns", SvgNamespace),
            new XAttribute("width", Format(width)),
            new XAttribute("height", Format(height)),
            new XAttribute("viewBox", $"0 0 {Format(width)} {Format(height)}"),
            new XAttribute("preserveAspectRatio", "xMinYMin meet"),
            new XAttribute("role", "img"),
            new XAttribute("aria-label", "Execution plan graph"),
            BuildDefinitions(ns),
            new XElement(
                ns + "rect",
                new XAttribute("x", "0"),
                new XAttribute("y", "0"),
                new XAttribute("width", Format(width)),
                new XAttribute("height", Format(height)),
                new XAttribute("fill", "#ffffff")));

        foreach (var edge in layout.StatementEdges)
        {
            root.Add(BuildEdge(ns, edge, options, isDashed: true));
        }

        foreach (var edge in layout.Edges)
        {
            root.Add(BuildEdge(ns, edge, options));
        }

        if (layout.StatementNode is not null)
        {
            root.Add(BuildStatementNode(ns, layout.StatementNode));
        }

        foreach (var node in layout.Nodes)
        {
            root.Add(BuildNode(ns, node, options));
        }

        return root;
    }

    private static XElement BuildDefinitions(XNamespace ns) =>
        new(
            ns + "defs",
            BuildMarker(ns, "arrow", "#94a3b8"),
            BuildMarker(ns, "arrow-critical", "#7c3aed"));

    private static XElement BuildMarker(XNamespace ns, string id, string fill) =>
        new(
            ns + "marker",
            new XAttribute("id", id),
            new XAttribute("markerWidth", "10"),
            new XAttribute("markerHeight", "10"),
            new XAttribute("refX", "5"),
            new XAttribute("refY", "5"),
            new XAttribute("orient", "auto"),
            new XElement(
                ns + "path",
                new XAttribute("d", "M 0 0 L 10 5 L 0 10 z"),
                new XAttribute("fill", fill)));

    private static XElement BuildEdge(XNamespace ns, GraphEdgeLayout edge, GraphRenderOptions options, bool isDashed = false)
    {
        var isCritical = options.ShowCriticalPath && edge.IsOnCriticalPath;
        var element = new XElement(
            ns + "path",
            new XAttribute("d", BuildEdgePath(edge)),
            new XAttribute("fill", "none"),
            new XAttribute("stroke", isCritical ? "#7c3aed" : "#94a3b8"),
            new XAttribute("stroke-width", isCritical ? "3.4" : "2.2"),
            new XAttribute("marker-end", isCritical ? "url(#arrow-critical)" : "url(#arrow)"));

        if (isDashed)
        {
            element.SetAttributeValue("stroke-dasharray", "4 4");
        }

        return element;
    }

    private static XElement BuildNode(XNamespace ns, GraphNodeLayout node, GraphRenderOptions options)
    {
        var icon = OperatorIconRegistry.Resolve(node.PhysicalOp, node.LogicalOp);
        var isScan = IsScanOperator(node, icon);
        var hasDashedOutline = IsAboveCostThreshold(node, options);
        var isCriticalNode = options.ShowCriticalPath && node.IsOnCriticalPath;
        var bodyX = node.X;
        var bodyY = node.Y;
        var iconTileX = bodyX + 16;
        var iconTileY = bodyY + 18;
        var iconOriginX = iconTileX + 8;
        var iconOriginY = iconTileY + 8;
        var contentX = bodyX + 66;
        var contentY = bodyY + 29;
        var meterRatio = (double)Math.Max(0.08m, node.CostRatio);
        var meterWidth = Math.Max(18d, (node.Width - 32) * meterRatio);
        var accentFill = GetAccentFill(node, icon);

        var group = new XElement(
            ns + "g",
            new XAttribute("data-node-id", node.NodeId));

        if (isScan)
        {
            group.Add(
                new XElement(
                    ns + "rect",
                    new XAttribute("x", Format(bodyX - 9)),
                    new XAttribute("y", Format(bodyY - 9)),
                    new XAttribute("width", Format(node.Width + 18)),
                    new XAttribute("height", Format(node.Height + 18)),
                    new XAttribute("rx", "27"),
                    new XAttribute("ry", "27"),
                    new XAttribute("fill", "none"),
                    new XAttribute("stroke", "#ef4444"),
                    new XAttribute("stroke-width", "5")));
        }

        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(bodyX)),
                new XAttribute("y", Format(bodyY)),
                new XAttribute("width", Format(node.Width)),
                new XAttribute("height", Format(node.Height)),
                new XAttribute("rx", "18"),
                new XAttribute("ry", "18"),
                new XAttribute("fill", GetCardFill(node)),
                new XAttribute("stroke", GetCardStroke(node)),
                new XAttribute("stroke-width", "2")));

        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(bodyX)),
                new XAttribute("y", Format(bodyY)),
                new XAttribute("width", Format(node.Width)),
                new XAttribute("height", "6"),
                new XAttribute("rx", "18"),
                new XAttribute("ry", "18"),
                new XAttribute("fill", accentFill)));

        if (hasDashedOutline)
        {
            group.Add(
                new XElement(
                    ns + "rect",
                    new XAttribute("x", Format(bodyX - 6)),
                    new XAttribute("y", Format(bodyY - 6)),
                    new XAttribute("width", Format(node.Width + 12)),
                    new XAttribute("height", Format(node.Height + 12)),
                    new XAttribute("rx", "24"),
                    new XAttribute("ry", "24"),
                    new XAttribute("fill", "none"),
                    new XAttribute("stroke", "#0f766e"),
                    new XAttribute("stroke-width", "3"),
                    new XAttribute("stroke-dasharray", "5 4")));
        }

        if (isCriticalNode)
        {
            group.Add(
                new XElement(
                    ns + "rect",
                    new XAttribute("x", Format(bodyX - 3)),
                    new XAttribute("y", Format(bodyY - 3)),
                    new XAttribute("width", Format(node.Width + 6)),
                    new XAttribute("height", Format(node.Height + 6)),
                    new XAttribute("rx", "21"),
                    new XAttribute("ry", "21"),
                    new XAttribute("fill", "none"),
                    new XAttribute("stroke", "#7c3aed"),
                    new XAttribute("stroke-width", "2.5")));
        }

        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(iconTileX)),
                new XAttribute("y", Format(iconTileY)),
                new XAttribute("width", "34"),
                new XAttribute("height", "34"),
                new XAttribute("rx", "10"),
                new XAttribute("ry", "10"),
                new XAttribute("fill", "#f8fafc"),
                new XAttribute("stroke", "#cbd5e1"),
                new XAttribute("stroke-opacity", "0.28"),
                new XAttribute("stroke-width", "1")));

        var iconGroup = new XElement(
            ns + "g",
            new XAttribute("stroke", accentFill),
            new XAttribute("fill", "none"),
            new XAttribute("stroke-linecap", "round"),
            new XAttribute("stroke-linejoin", "round"),
            new XAttribute("stroke-width", "2"));
        foreach (var element in BuildIcon(ns, icon.Kind, iconOriginX, iconOriginY, accentFill))
        {
            iconGroup.Add(element);
        }

        group.Add(iconGroup);
        group.Add(BuildText(ns, contentX, contentY, Trim(node.PrimaryLabel, 30), "14", "#0f172a", "700"));
        group.Add(BuildText(ns, contentX, contentY + 21, Trim(node.SecondaryLabel, 30), "11.5", "#334155", "400"));
        group.Add(BuildText(
            ns,
            contentX,
            contentY + 42,
            $"Node {node.NodeId} | Cost {PlanDisplayFormatter.FormatPercent(node.CostRatio)}",
            "11",
            "#64748b",
            "400"));
        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(bodyX + 16)),
                new XAttribute("y", Format(bodyY + node.Height - 12)),
                new XAttribute("width", Format(node.Width - 32)),
                new XAttribute("height", "4"),
                new XAttribute("rx", "2"),
                new XAttribute("ry", "2"),
                new XAttribute("fill", "#e2e8f0")));
        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(bodyX + 16)),
                new XAttribute("y", Format(bodyY + node.Height - 12)),
                new XAttribute("width", Format(meterWidth)),
                new XAttribute("height", "4"),
                new XAttribute("rx", "2"),
                new XAttribute("ry", "2"),
                new XAttribute("fill", accentFill)));

        return group;
    }

    private static XElement BuildStatementNode(XNamespace ns, StatementGraphNodeLayout node)
    {
        var bodyX = node.X;
        var bodyY = node.Y;
        var iconTileX = bodyX + 16;
        var iconTileY = bodyY + 16;
        var contentX = bodyX + 66;
        var contentY = bodyY + 28;
        var accentFill = "#2563eb";

        var group = new XElement(ns + "g", new XAttribute("data-statement-id", node.StatementId));
        group.Add(new XElement(ns + "title", node.StatementText));
        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(bodyX)),
                new XAttribute("y", Format(bodyY)),
                new XAttribute("width", Format(node.Width)),
                new XAttribute("height", Format(node.Height)),
                new XAttribute("rx", "18"),
                new XAttribute("ry", "18"),
                new XAttribute("fill", "#eff6ff"),
                new XAttribute("stroke", "#93c5fd"),
                new XAttribute("stroke-width", "2")));
        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(bodyX)),
                new XAttribute("y", Format(bodyY)),
                new XAttribute("width", Format(node.Width)),
                new XAttribute("height", "6"),
                new XAttribute("rx", "18"),
                new XAttribute("ry", "18"),
                new XAttribute("fill", accentFill)));
        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(iconTileX)),
                new XAttribute("y", Format(iconTileY)),
                new XAttribute("width", "34"),
                new XAttribute("height", "34"),
                new XAttribute("rx", "10"),
                new XAttribute("ry", "10"),
                new XAttribute("fill", "#dbeafe"),
                new XAttribute("stroke", "#bfdbfe"),
                new XAttribute("stroke-width", "1")));
        group.Add(BuildStatementIcon(ns, iconTileX + 8, iconTileY + 8, accentFill));
        group.Add(BuildText(ns, contentX, contentY, Trim(node.PrimaryLabel, 30), "14", "#0f172a", "700"));
        group.Add(BuildText(ns, contentX, contentY + 21, Trim(node.SecondaryLabel, 30), "11.5", "#334155", "400"));
        group.Add(BuildText(
            ns,
            contentX,
            contentY + 42,
            $"Statement {node.StatementId} | Query cost {PlanDisplayFormatter.FormatPercent(node.CostRatio)}",
            "11",
            "#64748b",
            "400"));
        return group;
    }

    private static XElement BuildStatementIcon(XNamespace ns, double x, double y, string accentFill)
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

    private static IEnumerable<XElement> BuildIcon(XNamespace ns, OperatorIconKind kind, double x, double y, string accentFill)
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

    private static XElement BuildText(XNamespace ns, double x, double y, string value, string fontSize, string fill, string fontWeight) =>
        new(
            ns + "text",
            new XAttribute("x", Format(x)),
            new XAttribute("y", Format(y)),
            new XAttribute("font-size", fontSize),
            new XAttribute("font-weight", fontWeight),
            new XAttribute("fill", fill),
            value);

    private static XElement Rect(XNamespace ns, double x, double y, double width, double height, double radius) =>
        new(
            ns + "rect",
            new XAttribute("x", Format(x)),
            new XAttribute("y", Format(y)),
            new XAttribute("width", Format(width)),
            new XAttribute("height", Format(height)),
            new XAttribute("rx", Format(radius)),
            new XAttribute("ry", Format(radius)));

    private static XElement Circle(XNamespace ns, double cx, double cy, double r, string? fill = null, string? stroke = null)
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

    private static XElement Ellipse(XNamespace ns, double cx, double cy, double rx, double ry) =>
        new(
            ns + "ellipse",
            new XAttribute("cx", Format(cx)),
            new XAttribute("cy", Format(cy)),
            new XAttribute("rx", Format(rx)),
            new XAttribute("ry", Format(ry)));

    private static XElement Line(XNamespace ns, double x1, double y1, double x2, double y2) =>
        new(
            ns + "line",
            new XAttribute("x1", Format(x1)),
            new XAttribute("y1", Format(y1)),
            new XAttribute("x2", Format(x2)),
            new XAttribute("y2", Format(y2)));

    private static XElement Path(XNamespace ns, string d) =>
        new(
            ns + "path",
            new XAttribute("d", d));

    private static string BuildEdgePath(GraphEdgeLayout edge)
    {
        var controlY1 = edge.Y1 + 36;
        var controlY2 = edge.Y2 - 36;
        return $"M {Format(edge.X1)} {Format(edge.Y1)} C {Format(edge.X1)} {Format(controlY1)}, {Format(edge.X2)} {Format(controlY2)}, {Format(edge.X2)} {Format(edge.Y2)}";
    }

    private static string GetCardFill(GraphNodeLayout node) =>
        node.HasWarnings ? "#fffbeb" : "#ffffff";

    private static string GetCardStroke(GraphNodeLayout node) =>
        node.HasWarnings ? "#f59e0b" : "#cbd5e1";

    private static string GetAccentFill(GraphNodeLayout node, OperatorIconDescriptor icon)
    {
        if (node.HasWarnings)
        {
            return "#f59e0b";
        }

        return node.CostRatio switch
        {
            >= 0.60m => icon.AccentColor,
            >= 0.30m => "#38bdf8",
            _ => "#94a3b8"
        };
    }

    private static bool IsAboveCostThreshold(GraphNodeLayout node, GraphRenderOptions options) =>
        node.CostRatio * 100m > options.ClampedCostHighlightThresholdPercent;

    private static bool IsScanOperator(GraphNodeLayout node, OperatorIconDescriptor icon) =>
        icon.Kind is OperatorIconKind.Scan or OperatorIconKind.ConstantScan
        || node.PhysicalOp.Contains("Scan", StringComparison.OrdinalIgnoreCase)
        || node.LogicalOp.Contains("Scan", StringComparison.OrdinalIgnoreCase);

    private static string Trim(string text, int maxLength) =>
        text.Length <= maxLength ? text : $"{text[..(maxLength - 1)]}…";

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}

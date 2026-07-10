using System.Xml.Linq;
using MSSQLPlanViewer.Core.Formatting;
using static MSSQLPlanViewer.Core.Rendering.SvgPrimitives;

namespace MSSQLPlanViewer.Core.Rendering;

public sealed class PlanGraphSvgRenderer : IPlanGraphSvgRenderer
{
    private const string SvgNamespace = "http://www.w3.org/2000/svg";

    private const string MetricFontSize = "11";
    private const string EdgeFlowFontSize = "12";
    private const string MetricFill = "#64748b";
    private const string CriticalColor = "#7c3aed";
    private const double CriticalEdgeStrokeWidth = 5d;
    private const double MetricSeparatorBottomMargin = 15d;

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
            root.Add(BuildEdge(ns, edge, options, layout.Direction, isDashed: true));
        }

        foreach (var edge in layout.Edges)
        {
            root.Add(BuildEdge(ns, edge, options, layout.Direction));
        }

        foreach (var edge in layout.Edges)
        {
            var edgeLabel = BuildEdgeFlowLabel(ns, edge, layout.Direction);
            if (edgeLabel is not null)
            {
                root.Add(edgeLabel);
            }
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
            BuildMarker(ns, "arrow-critical", CriticalColor));

    private static XElement BuildMarker(XNamespace ns, string id, string fill) =>
        new(
            ns + "marker",
            new XAttribute("id", id),
            new XAttribute("markerWidth", "8"),
            new XAttribute("markerHeight", "8"),
            new XAttribute("refX", "7"),
            new XAttribute("refY", "4"),
            new XAttribute("orient", "auto"),
            new XAttribute("markerUnits", "userSpaceOnUse"),
            new XElement(
                ns + "path",
                new XAttribute("d", "M 0 0 L 8 4 L 0 8 z"),
                new XAttribute("fill", fill)));

    private static XElement BuildEdge(
        XNamespace ns,
        GraphEdgeLayout edge,
        GraphRenderOptions options,
        GraphLayoutDirection direction,
        bool isDashed = false)
    {
        var isCritical = options.ShowCriticalPath && edge.IsOnCriticalPath;
        var strokeWidth = ResolveEdgeStrokeWidth(edge, isCritical);
        var element = new XElement(
            ns + "path",
            new XAttribute("d", PlanGraphSvgPathBuilder.BuildEdgePath(edge, direction)),
            new XAttribute("fill", "none"),
            new XAttribute("stroke", isCritical ? CriticalColor : "#94a3b8"),
            new XAttribute("stroke-width", Format(strokeWidth)),
            new XAttribute("marker-end", isCritical ? "url(#arrow-critical)" : "url(#arrow)"));

        if (isDashed)
        {
            element.SetAttributeValue("stroke-dasharray", "4 4");
        }

        return element;
    }

    private static double ResolveEdgeStrokeWidth(GraphEdgeLayout edge, bool isCritical) =>
        isCritical
            ? Math.Max(edge.StrokeWidth, CriticalEdgeStrokeWidth)
            : edge.StrokeWidth;

    private static XElement? BuildEdgeFlowLabel(XNamespace ns, GraphEdgeLayout edge, GraphLayoutDirection direction)
    {
        if (!edge.EstimatedRows.HasValue && !edge.ActualRows.HasValue)
        {
            return null;
        }

        const string estimatedRowsName = "Est rows";
        const string actualRowsName = "Act rows";
        var estimatedRowsValue = PlanDisplayFormatter.FormatNumber(edge.EstimatedRows);
        var actualRowsValue = PlanDisplayFormatter.FormatNumber(edge.ActualRows);
        var maxNameLength = Math.Max(estimatedRowsName.Length, actualRowsName.Length);
        var maxValueLength = Math.Max(estimatedRowsValue.Length, actualRowsValue.Length);
        var width = Math.Clamp(((maxNameLength + maxValueLength) * 6.3d) + 34d, 112d, 192d);
        var height = 44d;
        var (x, y) = ResolveEdgeFlowLabelPosition(edge, direction, width);
        var labelX = x - (width / 2d) + 12d;
        var valueX = x + (width / 2d) - 12d;

        return new XElement(
            ns + "g",
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(x - (width / 2d))),
                new XAttribute("y", Format(y - 26d)),
                new XAttribute("width", Format(width)),
                new XAttribute("height", Format(height)),
                new XAttribute("rx", "6"),
                new XAttribute("ry", "6"),
                new XAttribute("fill", "#ffffff"),
                new XAttribute("fill-opacity", "0.92")),
            BuildEdgeFlowText(ns, labelX, y - 8d, estimatedRowsName, "start"),
            BuildEdgeFlowText(ns, valueX, y - 8d, estimatedRowsValue, "end"),
            BuildEdgeFlowText(ns, labelX, y + 11d, actualRowsName, "start"),
            BuildEdgeFlowText(ns, valueX, y + 11d, actualRowsValue, "end"));
    }

    private static (double X, double Y) ResolveEdgeFlowLabelPosition(GraphEdgeLayout edge, GraphLayoutDirection direction, double width)
    {
        var lineX = (edge.X1 + edge.X2) / 2d;
        var lineY = (edge.Y1 + edge.Y2) / 2d;
        var horizontalClearance = 30d + (edge.StrokeWidth / 2d);
        var verticalClearance = 18d + (edge.StrokeWidth / 2d);

        return direction == GraphLayoutDirection.HorizontalSsms
            ? (lineX, lineY - horizontalClearance)
            : (lineX + (width / 2d) + verticalClearance, lineY);
    }

    private static XElement BuildEdgeFlowText(XNamespace ns, double x, double y, string label, string textAnchor) =>
        new(
            ns + "text",
            new XAttribute("x", Format(x)),
            new XAttribute("y", Format(y)),
            new XAttribute("font-size", EdgeFlowFontSize),
            new XAttribute("font-weight", "400"),
            new XAttribute("fill", "#334155"),
            new XAttribute("text-anchor", textAnchor),
            label);

    private static XElement BuildNode(XNamespace ns, GraphNodeLayout node, GraphRenderOptions options)
    {
        var icon = OperatorIconRegistry.Resolve(node.PhysicalOp, node.LogicalOp);
        var isCriticalNode = options.ShowCriticalPath && node.IsOnCriticalPath;
        var costEmphasisLevel = GraphCostEmphasis.Resolve(node.CostRatio, options.CostHighlightThresholdPercent);
        var isCostEmphasized = GraphCostEmphasis.IsEmphasized(costEmphasisLevel);
        var accentFill = GetAccentFill(node, icon);

        var group = new XElement(
            ns + "g",
            new XAttribute("data-node-id", node.NodeId));

        if (isCostEmphasized)
        {
            group.SetAttributeValue("data-cost-emphasis", costEmphasisLevel.ToString());
            AddEmphasisHalo(ns, group, node, costEmphasisLevel);
        }

        if (IsScanOperator(node, icon))
        {
            AddOutlineRect(ns, group, node, inset: 9, radius: 27, stroke: "#ef4444", strokeWidth: "5");
        }

        AddCardChrome(ns, group, node, accentFill, isCriticalNode);

        if (isCostEmphasized)
        {
            AddDashedOutline(ns, group, node, costEmphasisLevel);
        }

        if (isCriticalNode)
        {
            AddOutlineRect(ns, group, node, inset: 3, radius: 21, stroke: CriticalColor, strokeWidth: "3.5");
        }

        AddIconArea(ns, group, node, icon, accentFill, isCriticalNode);
        AddNodeLabels(ns, group, node);
        AddCostMeter(ns, group, node, icon, costEmphasisLevel, isCostEmphasized);

        return group;
    }

    private static void AddEmphasisHalo(XNamespace ns, XElement group, GraphNodeLayout node, GraphCostEmphasisLevel costEmphasisLevel)
    {
        var style = GraphCostEmphasis.GetStyle(costEmphasisLevel);
        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(node.X - 13)),
                new XAttribute("y", Format(node.Y - 13)),
                new XAttribute("width", Format(node.Width + 26)),
                new XAttribute("height", Format(node.Height + 26)),
                new XAttribute("rx", "30"),
                new XAttribute("ry", "30"),
                new XAttribute("fill", style.HaloFill),
                new XAttribute("fill-opacity", "0.72"),
                new XAttribute("stroke", style.HaloStroke),
                new XAttribute("stroke-opacity", "0.5"),
                new XAttribute("stroke-width", "2")));
    }

    private static void AddOutlineRect(
        XNamespace ns,
        XElement group,
        GraphNodeLayout node,
        double inset,
        double radius,
        string stroke,
        string strokeWidth)
    {
        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(node.X - inset)),
                new XAttribute("y", Format(node.Y - inset)),
                new XAttribute("width", Format(node.Width + (inset * 2))),
                new XAttribute("height", Format(node.Height + (inset * 2))),
                new XAttribute("rx", Format(radius)),
                new XAttribute("ry", Format(radius)),
                new XAttribute("fill", "none"),
                new XAttribute("stroke", stroke),
                new XAttribute("stroke-width", strokeWidth)));
    }

    private static void AddDashedOutline(XNamespace ns, XElement group, GraphNodeLayout node, GraphCostEmphasisLevel costEmphasisLevel)
    {
        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(node.X - 6)),
                new XAttribute("y", Format(node.Y - 6)),
                new XAttribute("width", Format(node.Width + 12)),
                new XAttribute("height", Format(node.Height + 12)),
                new XAttribute("rx", "24"),
                new XAttribute("ry", "24"),
                new XAttribute("fill", "none"),
                new XAttribute("stroke", GraphCostEmphasis.GetStyle(costEmphasisLevel).OutlineStroke),
                new XAttribute("stroke-width", "3"),
                new XAttribute("stroke-dasharray", "5 4")));
    }

    private static void AddCardChrome(XNamespace ns, XElement group, GraphNodeLayout node, string accentFill, bool isCriticalNode)
    {
        const double headerBandHeight = 24d;
        var bodyX = node.X;
        var bodyY = node.Y;

        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(bodyX)),
                new XAttribute("y", Format(bodyY)),
                new XAttribute("width", Format(node.Width)),
                new XAttribute("height", Format(node.Height)),
                new XAttribute("rx", "18"),
                new XAttribute("ry", "18"),
                new XAttribute("fill", GetCardFill(node, isCriticalNode)),
                new XAttribute("stroke", GetCardStroke(node)),
                new XAttribute("stroke-width", "2")));

        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(bodyX + 1)),
                new XAttribute("y", Format(bodyY + 1)),
                new XAttribute("width", Format(node.Width - 2)),
                new XAttribute("height", Format(headerBandHeight)),
                new XAttribute("rx", "18"),
                new XAttribute("ry", "18"),
                new XAttribute("fill", "#ffffff")));
        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(bodyX + 1)),
                new XAttribute("y", Format(bodyY + 12)),
                new XAttribute("width", Format(node.Width - 2)),
                new XAttribute("height", "13"),
                new XAttribute("fill", "#ffffff")));
        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(bodyX)),
                new XAttribute("y", Format(bodyY + headerBandHeight)),
                new XAttribute("width", Format(node.Width)),
                new XAttribute("height", "5"),
                new XAttribute("fill", accentFill)));
        group.Add(BuildNodeIdLabel(ns, bodyX + (node.Width / 2d), bodyY + 17d, $"Node {node.NodeId}"));
    }

    private static void AddIconArea(
        XNamespace ns,
        XElement group,
        GraphNodeLayout node,
        OperatorIconDescriptor icon,
        string accentFill,
        bool isCriticalNode)
    {
        var iconTileX = node.X + 16;
        var iconTileY = node.Y + 40;

        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(iconTileX)),
                new XAttribute("y", Format(iconTileY)),
                new XAttribute("width", "34"),
                new XAttribute("height", "34"),
                new XAttribute("rx", "10"),
                new XAttribute("ry", "10"),
                new XAttribute("fill", GetIconTileFill(isCriticalNode)),
                new XAttribute("stroke", isCriticalNode ? "#c4b5fd" : "#cbd5e1"),
                new XAttribute("stroke-opacity", "0.28"),
                new XAttribute("stroke-width", "1")));

        var iconGroup = new XElement(
            ns + "g",
            new XAttribute("stroke", accentFill),
            new XAttribute("fill", "none"),
            new XAttribute("stroke-linecap", "round"),
            new XAttribute("stroke-linejoin", "round"),
            new XAttribute("stroke-width", "2"));
        foreach (var element in OperatorIconSvgBuilder.BuildIcon(ns, icon.Kind, iconTileX + 8, iconTileY + 8, accentFill))
        {
            iconGroup.Add(element);
        }

        group.Add(iconGroup);
        var accessoryBadgeY = iconTileY + 40d;
        if (node.IsParallel)
        {
            group.Add(OperatorIconSvgBuilder.BuildParallelBadge(ns, iconTileX + 3.5d, accessoryBadgeY, node.NodeId));
            accessoryBadgeY += 30d;
        }

        if (node.HasImplicitConversion)
        {
            group.Add(OperatorIconSvgBuilder.BuildImplicitConversionBadge(ns, iconTileX + 3.5d, accessoryBadgeY, node.NodeId));
        }
    }

    private static void AddNodeLabels(XNamespace ns, XElement group, GraphNodeLayout node)
    {
        var contentX = node.X + 66;
        var metricValueX = node.X + node.Width - 24;
        var contentY = node.Y + 47;
        var secondaryLabels = BuildSecondaryLabels(node);
        var hasIndexLabel = !string.IsNullOrWhiteSpace(secondaryLabels.IndexLabel);

        group.Add(Text(ns, contentX, contentY, Trim(node.PrimaryLabel, 30), "14", "#0f172a", "700"));
        group.Add(Text(ns, contentX, contentY + 21, secondaryLabels.TableLabel, "11.5", "#334155", "400"));
        if (hasIndexLabel)
        {
            group.Add(Text(ns, contentX, contentY + 36, secondaryLabels.IndexLabel!, "11.5", "#334155", "400"));
        }

        var metrics = BuildNodeMetricRows(node);
        var metricY = contentY + (hasIndexLabel ? 62 : 47);
        if (metrics.Count > 0 && HasVisibleSecondaryLabel(secondaryLabels))
        {
            group.Add(BuildMetricSeparator(ns, contentX, metricY - MetricSeparatorBottomMargin, node.X + node.Width - 18));
        }

        NodeMetricGroup? previousMetricGroup = null;
        foreach (var metric in metrics)
        {
            if (previousMetricGroup.HasValue && previousMetricGroup.Value != metric.Group)
            {
                var separatorY = ResolveMetricSeparatorY(metricY, metric.Spacing);
                group.Add(BuildMetricSeparator(ns, contentX, separatorY, node.X + node.Width - 18));
            }

            metricY += metric.Spacing;
            previousMetricGroup = metric.Group;
            group.Add(Text(ns, contentX, metricY, metric.Label, MetricFontSize, MetricFill, "400"));
            group.Add(BuildMetricValueText(ns, metricValueX, metricY, metric.Value));
        }
    }

    private static XElement BuildMetricValueText(XNamespace ns, double x, double y, string value) =>
        new(
            ns + "text",
            new XAttribute("x", Format(x)),
            new XAttribute("y", Format(y)),
            new XAttribute("font-size", MetricFontSize),
            new XAttribute("font-weight", "400"),
            new XAttribute("fill", MetricFill),
            new XAttribute("text-anchor", "end"),
            value);

    private static XElement BuildMetricSeparator(XNamespace ns, double x1, double y, double x2) =>
        new(
            ns + "line",
            new XAttribute("x1", Format(x1)),
            new XAttribute("y1", Format(y)),
            new XAttribute("x2", Format(x2)),
            new XAttribute("y2", Format(y)),
            new XAttribute("stroke", "#64748b"),
            new XAttribute("stroke-width", "1"),
            new XAttribute("stroke-opacity", "0.9"),
            new XAttribute("data-metric-separator", "true"));

    private static void AddCostMeter(
        XNamespace ns,
        XElement group,
        GraphNodeLayout node,
        OperatorIconDescriptor icon,
        GraphCostEmphasisLevel costEmphasisLevel,
        bool isCostEmphasized)
    {
        var meterRatio = (double)Math.Max(0.08m, node.CostRatio);
        var meterWidth = Math.Max(18d, (node.Width - 32) * meterRatio);
        var meterHeight = isCostEmphasized ? 7d : 4d;
        var meterY = node.Y + node.Height - (isCostEmphasized ? 9d : 7d);

        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(node.X + 16)),
                new XAttribute("y", Format(meterY)),
                new XAttribute("width", Format(node.Width - 32)),
                new XAttribute("height", Format(meterHeight)),
                new XAttribute("rx", Format(meterHeight / 2)),
                new XAttribute("ry", Format(meterHeight / 2)),
                new XAttribute("fill", isCostEmphasized ? "#fee2e2" : "#e2e8f0")));
        group.Add(
            new XElement(
                ns + "rect",
                new XAttribute("x", Format(node.X + 16)),
                new XAttribute("y", Format(meterY)),
                new XAttribute("width", Format(meterWidth)),
                new XAttribute("height", Format(meterHeight)),
                new XAttribute("rx", Format(meterHeight / 2)),
                new XAttribute("ry", Format(meterHeight / 2)),
                new XAttribute("fill", GetMeterFill(node, icon, costEmphasisLevel))));
    }

    private static XElement BuildNodeIdLabel(XNamespace ns, double x, double y, string label) =>
        new(
            ns + "text",
            new XAttribute("x", Format(x)),
            new XAttribute("y", Format(y)),
            new XAttribute("font-size", "14"),
            new XAttribute("font-weight", "700"),
            new XAttribute("fill", "#0f172a"),
            new XAttribute("text-anchor", "middle"),
            label);

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
                new XAttribute("x", Format(iconTileX)),
                new XAttribute("y", Format(iconTileY)),
                new XAttribute("width", "34"),
                new XAttribute("height", "34"),
                new XAttribute("rx", "10"),
                new XAttribute("ry", "10"),
                new XAttribute("fill", "#dbeafe"),
                new XAttribute("stroke", "#bfdbfe"),
                new XAttribute("stroke-width", "1")));
        group.Add(OperatorIconSvgBuilder.BuildStatementIcon(ns, iconTileX + 8, iconTileY + 8, accentFill));
        group.Add(Text(ns, contentX, contentY, Trim(node.PrimaryLabel, 30), "14", "#0f172a", "700"));
        return group;
    }

    private static string GetCardFill(GraphNodeLayout node, bool isCriticalNode)
    {
        if (node.HasWarnings)
        {
            return "#fffbeb";
        }

        return isCriticalNode ? "#f5f3ff" : "#ffffff";
    }

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

    private static string GetMeterFill(GraphNodeLayout node, OperatorIconDescriptor icon, GraphCostEmphasisLevel costEmphasisLevel) =>
        GraphCostEmphasis.IsEmphasized(costEmphasisLevel)
            ? GraphCostEmphasis.GetStyle(costEmphasisLevel).MeterFill
            : GetAccentFill(node, icon);

    private static string GetIconTileFill(bool isCriticalNode) =>
        isCriticalNode ? "#ede9fe" : "#f8fafc";

    private static bool IsScanOperator(GraphNodeLayout node, OperatorIconDescriptor icon) =>
        icon.Kind is OperatorIconKind.Scan or OperatorIconKind.ConstantScan
        || node.PhysicalOp.Contains("Scan", StringComparison.OrdinalIgnoreCase)
        || node.LogicalOp.Contains("Scan", StringComparison.OrdinalIgnoreCase);

    private static double ResolveMetricSeparatorY(double currentMetricY, double nextMetricSpacing) =>
        currentMetricY + Math.Max(4d, nextMetricSpacing - MetricSeparatorBottomMargin);

    private static IReadOnlyList<NodeMetricRow> BuildNodeMetricRows(GraphNodeLayout node)
    {
        var rows = new List<NodeMetricRow>
        {
            new("Cost", PlanDisplayFormatter.FormatPercent(node.CostRatio), 0, NodeMetricGroup.Cost)
        };

        AddNumberMetric(rows, "Average Row Size", node.AverageRowSize, 22, NodeMetricGroup.AverageRowSize);
        AddNumberMetric(rows, "Estimated Rows", node.EstimatedRows, 22, NodeMetricGroup.Estimated);
        AddNumberMetric(rows, "Estimated Executions", node.EstimatedExecutions, 14, NodeMetricGroup.Estimated);
        AddTextMetric(rows, "Estimated Mode", node.EstimatedExecutionMode, 14, NodeMetricGroup.Estimated);
        AddNumberMetric(rows, "Actual Rows", node.ActualRows, 22, NodeMetricGroup.Actual);
        AddNumberMetric(rows, "Actual Executions", node.ActualExecutions, 14, NodeMetricGroup.Actual);
        AddTextMetric(rows, "Actual Mode", node.ActualExecutionMode, 14, NodeMetricGroup.Actual);
        AddNumberMetric(rows, "Actual Logical Reads", node.ActualLogicalReads, 14, NodeMetricGroup.Actual);
        AddNumberMetric(rows, "Actual Physical Reads", node.ActualPhysicalReads, 14, NodeMetricGroup.Actual);
        AddMillisecondsMetric(rows, "Actual CPU", node.ActualCpuMs, 14, NodeMetricGroup.Actual);
        AddMillisecondsMetric(rows, "Actual Elapsed", node.ActualElapsedMs, 14, NodeMetricGroup.Actual);

        return rows;
    }

    private static void AddNumberMetric(ICollection<NodeMetricRow> rows, string label, double? value, double spacing, NodeMetricGroup group)
    {
        if (value.HasValue)
        {
            rows.Add(new NodeMetricRow(label, PlanDisplayFormatter.FormatNumber(value), spacing, group));
        }
    }

    private static void AddTextMetric(ICollection<NodeMetricRow> rows, string label, string? value, double spacing, NodeMetricGroup group)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            rows.Add(new NodeMetricRow(label, value, spacing, group));
        }
    }

    private static void AddMillisecondsMetric(ICollection<NodeMetricRow> rows, string label, double? value, double spacing, NodeMetricGroup group)
    {
        if (value.HasValue)
        {
            rows.Add(new NodeMetricRow(label, FormatMilliseconds(value.Value), spacing, group));
        }
    }

    private static string FormatMilliseconds(double value) =>
        $"{PlanDisplayFormatter.FormatNumber(value)} ms";

    private static NodeSecondaryLabels BuildSecondaryLabels(GraphNodeLayout node)
    {
        const string separator = " / ";
        var separatorIndex = node.SecondaryLabel.IndexOf(separator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return new NodeSecondaryLabels(Trim(node.SecondaryLabel, 30), null);
        }

        var tableLabel = node.SecondaryLabel[..separatorIndex];
        var indexLabel = node.SecondaryLabel[(separatorIndex + separator.Length)..];
        return new NodeSecondaryLabels(Trim(tableLabel, 30), Trim(indexLabel, 30));
    }

    private static bool HasVisibleSecondaryLabel(NodeSecondaryLabels labels) =>
        !string.IsNullOrWhiteSpace(labels.TableLabel) || !string.IsNullOrWhiteSpace(labels.IndexLabel);

    private sealed record NodeSecondaryLabels(string TableLabel, string? IndexLabel);

    private enum NodeMetricGroup
    {
        Cost,
        AverageRowSize,
        Estimated,
        Actual
    }

    private sealed record NodeMetricRow(string Label, string Value, double Spacing, NodeMetricGroup Group);

    private static string Trim(string text, int maxLength) =>
        text.Length <= maxLength ? text : $"{text[..(maxLength - 1)]}…";
}

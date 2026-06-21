namespace MSSQLPlanViewer.Core.Rendering;

public enum GraphCostEmphasisLevel
{
    None,
    Elevated,
    High,
    Critical
}

public sealed record GraphCostEmphasisStyle(
    string CssClass,
    string HaloFill,
    string HaloStroke,
    string BadgeFill,
    string BadgeStroke,
    string BadgeTextFill,
    string MeterFill,
    string OutlineStroke);

public static class GraphCostEmphasis
{
    private static readonly GraphCostEmphasisStyle NoneStyle = new(
        CssClass: string.Empty,
        HaloFill: "none",
        HaloStroke: "none",
        BadgeFill: "none",
        BadgeStroke: "none",
        BadgeTextFill: "none",
        MeterFill: "none",
        OutlineStroke: "#0f766e");

    private static readonly GraphCostEmphasisStyle ElevatedStyle = new(
        CssClass: "cost-elevated",
        HaloFill: "#fffbeb",
        HaloStroke: "#f59e0b",
        BadgeFill: "#f59e0b",
        BadgeStroke: "#b45309",
        BadgeTextFill: "#ffffff",
        MeterFill: "#f59e0b",
        OutlineStroke: "#d97706");

    private static readonly GraphCostEmphasisStyle HighStyle = new(
        CssClass: "cost-high",
        HaloFill: "#fff7ed",
        HaloStroke: "#f97316",
        BadgeFill: "#ea580c",
        BadgeStroke: "#c2410c",
        BadgeTextFill: "#ffffff",
        MeterFill: "#ea580c",
        OutlineStroke: "#ea580c");

    private static readonly GraphCostEmphasisStyle CriticalStyle = new(
        CssClass: "cost-critical",
        HaloFill: "#fef2f2",
        HaloStroke: "#ef4444",
        BadgeFill: "#dc2626",
        BadgeStroke: "#991b1b",
        BadgeTextFill: "#ffffff",
        MeterFill: "#dc2626",
        OutlineStroke: "#dc2626");

    public static GraphCostEmphasisLevel Resolve(decimal costRatio, int costHighlightThresholdPercent)
    {
        var costPercent = Math.Max(0m, costRatio) * 100m;
        var threshold = Math.Clamp(costHighlightThresholdPercent, 0, 100);

        if (costPercent <= threshold)
        {
            return GraphCostEmphasisLevel.None;
        }

        return costPercent switch
        {
            >= 60m => GraphCostEmphasisLevel.Critical,
            >= 30m => GraphCostEmphasisLevel.High,
            _ => GraphCostEmphasisLevel.Elevated
        };
    }

    public static bool IsEmphasized(GraphCostEmphasisLevel level) =>
        level != GraphCostEmphasisLevel.None;

    public static GraphCostEmphasisStyle GetStyle(GraphCostEmphasisLevel level) =>
        level switch
        {
            GraphCostEmphasisLevel.Elevated => ElevatedStyle,
            GraphCostEmphasisLevel.High => HighStyle,
            GraphCostEmphasisLevel.Critical => CriticalStyle,
            _ => NoneStyle
        };
}

namespace MSSQLPlanViewer.Core.Rendering;

public sealed record GraphRenderOptions(
    int CostHighlightThresholdPercent = 20,
    bool ShowCriticalPath = true)
{
    public int ClampedCostHighlightThresholdPercent => Math.Clamp(CostHighlightThresholdPercent, 0, 100);
}

using System.Globalization;

namespace MSSQLPlanViewer.Core.Rendering;

internal static class PlanGraphSvgPathBuilder
{
    public static string BuildEdgePath(GraphEdgeLayout edge, GraphLayoutDirection direction) =>
        direction == GraphLayoutDirection.HorizontalSsms
            ? BuildHorizontalEdgePath(edge)
            : BuildVerticalEdgePath(edge);

    private static string BuildVerticalEdgePath(GraphEdgeLayout edge)
    {
        var controlY1 = edge.Y1 + 36;
        var controlY2 = edge.Y2 - 36;
        return $"M {Format(edge.X1)} {Format(edge.Y1)} C {Format(edge.X1)} {Format(controlY1)}, {Format(edge.X2)} {Format(controlY2)}, {Format(edge.X2)} {Format(edge.Y2)}";
    }

    private static string BuildHorizontalEdgePath(GraphEdgeLayout edge)
    {
        var controlX1 = edge.X1 - 36;
        var controlX2 = edge.X2 + 36;
        return $"M {Format(edge.X1)} {Format(edge.Y1)} C {Format(controlX1)} {Format(edge.Y1)}, {Format(controlX2)} {Format(edge.Y2)}, {Format(edge.X2)} {Format(edge.Y2)}";
    }

    private static string Format(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);
}

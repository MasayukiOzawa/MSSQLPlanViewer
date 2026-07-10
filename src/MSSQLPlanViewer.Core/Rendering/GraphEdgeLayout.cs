namespace MSSQLPlanViewer.Core.Rendering;

public sealed record GraphEdgeLayout(
    string FromNodeId,
    string ToNodeId,
    double X1,
    double Y1,
    double X2,
    double Y2,
    bool IsOnCriticalPath,
    double? FlowRows = null,
    double StrokeWidth = 2.2d,
    double? EstimatedRows = null,
    double? ActualRows = null);

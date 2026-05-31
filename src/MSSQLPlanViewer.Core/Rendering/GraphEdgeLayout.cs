namespace MSSQLPlanViewer.Core.Rendering;

public sealed record GraphEdgeLayout(
    string FromNodeId,
    string ToNodeId,
    double X1,
    double Y1,
    double X2,
    double Y2);

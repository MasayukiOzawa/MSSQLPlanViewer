namespace MSSQLPlanViewer.Core.Rendering;

public sealed record StatementGraphLayout(
    string StatementId,
    double Width,
    double Height,
    IReadOnlyList<GraphNodeLayout> Nodes,
    IReadOnlyList<GraphEdgeLayout> Edges);

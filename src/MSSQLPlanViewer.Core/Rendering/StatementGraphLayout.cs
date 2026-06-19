namespace MSSQLPlanViewer.Core.Rendering;

public sealed record StatementGraphLayout(
    string StatementId,
    StatementGraphNodeLayout? StatementNode,
    double Width,
    double Height,
    IReadOnlyList<GraphNodeLayout> Nodes,
    IReadOnlyList<GraphEdgeLayout> StatementEdges,
    IReadOnlyList<GraphEdgeLayout> Edges,
    GraphLayoutDirection Direction = GraphLayoutDirection.Vertical);

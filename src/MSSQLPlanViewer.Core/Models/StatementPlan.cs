namespace MSSQLPlanViewer.Core.Models;

public sealed record StatementPlan(
    string StatementId,
    string StatementType,
    string StatementText,
    StatementPlanSummary Summary,
    IReadOnlyList<PlanNode> Nodes,
    IReadOnlyList<PlanEdge> Edges,
    IReadOnlyList<PlanWarning> Warnings,
    IReadOnlyList<string> RootNodeIds)
{
    public int WarningCount => Warnings.Count + Nodes.Sum(node => node.Warnings.Count);
}

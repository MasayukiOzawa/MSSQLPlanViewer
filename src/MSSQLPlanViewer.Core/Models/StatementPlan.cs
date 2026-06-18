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

    public string StatementElementName { get; init; } = "Statement";

    public IReadOnlyList<PlanProperty> StatementProperties { get; init; } = Array.Empty<PlanProperty>();

    public IReadOnlyList<PlanProperty> StatementSetOptionsProperties { get; init; } = Array.Empty<PlanProperty>();
}

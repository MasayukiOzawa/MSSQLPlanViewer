namespace MSSQLPlanViewer.Core.Models;

public sealed record ShowplanDocument(
    ShowplanMetadata Metadata,
    IReadOnlyList<StatementPlan> Statements)
{
    public int TotalNodeCount => Statements.Sum(statement => statement.Nodes.Count);

    public int TotalWarningCount => Statements.Sum(statement => statement.WarningCount);
}

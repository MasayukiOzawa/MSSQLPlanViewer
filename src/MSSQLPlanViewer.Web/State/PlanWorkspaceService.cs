using MSSQLPlanViewer.Core.Diagnostics;
using MSSQLPlanViewer.Core.Models;
using MSSQLPlanViewer.Core.Rendering;

namespace MSSQLPlanViewer.Web.State;

public sealed class PlanWorkspaceService(
    IPlanDiagnosticsService diagnosticsService,
    IPlanGraphLayoutService graphLayoutService,
    IPlanTableProjector tableProjector)
{
    public LoadedPlan CreateLoadedPlan(
        ShowplanDocument document,
        string label,
        GraphLayoutDirection layoutDirection)
    {
        var plan = new LoadedPlan
        {
            Label = label,
            Document = document,
            Diagnostics = diagnosticsService.Analyze(document)
        };

        var firstStatement = document.Statements.FirstOrDefault();
        if (firstStatement is not null)
        {
            SelectStatement(plan, firstStatement.StatementId, layoutDirection);
        }

        return plan;
    }

    public void SelectStatement(
        LoadedPlan plan,
        string statementId,
        GraphLayoutDirection layoutDirection)
    {
        var statement = plan.Document.Statements.FirstOrDefault(item => item.StatementId == statementId)
            ?? plan.Document.Statements.FirstOrDefault();
        if (statement is null)
        {
            return;
        }

        plan.SelectedStatementId = statement.StatementId;
        plan.SelectedLayout = graphLayoutService.CreateLayout(
            statement,
            CalculateStatementCostRatio(plan.Document, statement),
            layoutDirection);
        plan.CurrentRows = tableProjector.Project(statement);
        plan.SelectedNodeId = null;
        plan.HoveredNodeId = null;
        plan.IsStatementDetailsSelected = false;
    }

    public void RefreshLayout(
        LoadedPlan plan,
        StatementPlan statement,
        GraphLayoutDirection layoutDirection)
    {
        plan.SelectedLayout = graphLayoutService.CreateLayout(
            statement,
            CalculateStatementCostRatio(plan.Document, statement),
            layoutDirection);
    }

    public PlanCompareSelection EnsureCompareSelection(
        IReadOnlyList<LoadedPlan> plans,
        Guid? comparePlanAId,
        Guid? comparePlanBId)
    {
        if (comparePlanAId is null || plans.All(plan => plan.Id != comparePlanAId))
        {
            comparePlanAId = plans.Count > 0 ? plans[0].Id : null;
        }

        var requiresDistinctB = plans.Count > 1 && comparePlanBId == comparePlanAId;
        if (comparePlanBId is null || plans.All(plan => plan.Id != comparePlanBId) || requiresDistinctB)
        {
            comparePlanBId = plans.FirstOrDefault(plan => plan.Id != comparePlanAId)?.Id
                ?? (plans.Count > 0 ? plans[0].Id : null);
        }

        return new PlanCompareSelection(comparePlanAId, comparePlanBId);
    }

    public static decimal? CalculateStatementCostRatio(ShowplanDocument document, StatementPlan statement)
    {
        var totalCost = document.Statements.Sum(item => item.Summary.EstimatedSubtreeCost ?? 0);
        if (totalCost <= 0)
        {
            return null;
        }

        return (statement.Summary.EstimatedSubtreeCost ?? 0) / totalCost;
    }
}

public readonly record struct PlanCompareSelection(Guid? PlanAId, Guid? PlanBId);

using MSSQLPlanViewer.Core.Diagnostics;
using MSSQLPlanViewer.Core.Models;
using MSSQLPlanViewer.Core.Rendering;
using MSSQLPlanViewer.Web.State;

namespace MSSQLPlanViewer.Core.Tests;

public sealed class PlanWorkspaceServiceTests
{
    [Fact]
    public void CreateLoadedPlan_InitializesFirstStatementLayoutRowsAndDiagnostics()
    {
        var statement = TestPlanFactory.Statement(
            new[] { TestPlanFactory.Node("1", subtreeCost: 2m) },
            rootNodeIds: new[] { "1" });
        var document = CreateDocument(statement);
        var diagnostics = new[]
        {
            new PlanDiagnostic("rule", "Rule", PlanDiagnosticSeverity.Info, "1", null, "message", "recommendation", Array.Empty<PlanProperty>())
        };
        var service = new PlanWorkspaceService(
            new StubDiagnosticsService(diagnostics),
            new StubLayoutService(),
            new StubTableProjector());

        var plan = service.CreateLoadedPlan(document, "Plan A", GraphLayoutDirection.HorizontalSsms);

        Assert.Equal("Plan A", plan.Label);
        Assert.Equal("1", plan.SelectedStatementId);
        Assert.NotNull(plan.SelectedLayout);
        Assert.Single(plan.CurrentRows);
        Assert.Same(diagnostics, plan.Diagnostics);
    }

    [Fact]
    public void SelectStatement_UpdatesRowsAndClearsNodeState()
    {
        var first = TestPlanFactory.Statement(
            new[] { TestPlanFactory.Node("1") },
            rootNodeIds: new[] { "1" },
            statementId: "1");
        var second = TestPlanFactory.Statement(
            new[] { TestPlanFactory.Node("2") },
            rootNodeIds: new[] { "2" },
            statementId: "2");
        var service = new PlanWorkspaceService(
            new StubDiagnosticsService(),
            new StubLayoutService(),
            new StubTableProjector());
        var plan = service.CreateLoadedPlan(CreateDocument(first, second), "Plan A", GraphLayoutDirection.Vertical)
            .WithState("1");

        service.SelectStatement(plan, "2", GraphLayoutDirection.HorizontalSsms);

        Assert.Equal("2", plan.SelectedStatementId);
        Assert.Null(plan.SelectedNodeId);
        Assert.Null(plan.HoveredNodeId);
        Assert.False(plan.IsStatementDetailsSelected);
        Assert.Single(plan.CurrentRows);
    }

    [Fact]
    public void SelectStatementByKey_SelectsDuplicateStatementIdsAcrossBatches()
    {
        var first = TestPlanFactory.Statement(
            new[] { TestPlanFactory.Node("1") },
            rootNodeIds: new[] { "1" },
            statementId: "1",
            batchNumber: 1,
            statementOrdinal: 1);
        var second = TestPlanFactory.Statement(
            new[] { TestPlanFactory.Node("2") },
            rootNodeIds: new[] { "2" },
            statementId: "1",
            batchNumber: 2,
            statementOrdinal: 2);
        var service = new PlanWorkspaceService(
            new StubDiagnosticsService(),
            new StubLayoutService(),
            new StubTableProjector());
        var plan = service.CreateLoadedPlan(CreateDocument(first, second), "Plan A", GraphLayoutDirection.Vertical)
            .WithState("1");

        service.SelectStatementByKey(plan, second.StatementKey, GraphLayoutDirection.HorizontalSsms);

        Assert.Equal("1", plan.SelectedStatementId);
        Assert.Equal(second.StatementKey, plan.SelectedStatementKey);
        Assert.Equal("2", Assert.Single(plan.CurrentRows).NodeId);
        Assert.Null(plan.SelectedNodeId);
        Assert.Null(plan.HoveredNodeId);
        Assert.False(plan.IsStatementDetailsSelected);
    }

    [Fact]
    public void EnsureCompareSelection_ChoosesDistinctPlansWhenAvailable()
    {
        var service = new PlanWorkspaceService(
            new StubDiagnosticsService(),
            new StubLayoutService(),
            new StubTableProjector());
        var planA = service.CreateLoadedPlan(CreateDocument(TestPlanFactory.Statement(new[] { TestPlanFactory.Node("1") })), "A", GraphLayoutDirection.Vertical);
        var planB = service.CreateLoadedPlan(CreateDocument(TestPlanFactory.Statement(new[] { TestPlanFactory.Node("2") })), "B", GraphLayoutDirection.Vertical);

        var selection = service.EnsureCompareSelection(new[] { planA, planB }, null, null);

        Assert.Equal(planA.Id, selection.PlanAId);
        Assert.Equal(planB.Id, selection.PlanBId);
    }

    private static ShowplanDocument CreateDocument(params StatementPlan[] statements) =>
        new(
            new ShowplanMetadata(string.Empty, ShowplanSchemaVersion.Unknown, null, null),
            statements);

    private sealed class StubDiagnosticsService(IReadOnlyList<PlanDiagnostic>? diagnosticsResult = null) : IPlanDiagnosticsService
    {
        private readonly IReadOnlyList<PlanDiagnostic> diagnostics = diagnosticsResult ?? Array.Empty<PlanDiagnostic>();

        public IReadOnlyList<PlanDiagnostic> Analyze(ShowplanDocument document) => diagnostics;
    }

    private sealed class StubLayoutService : IPlanGraphLayoutService
    {
        public StatementGraphLayout CreateLayout(
            StatementPlan statement,
            decimal? statementCostRatio = null,
            GraphLayoutDirection direction = GraphLayoutDirection.Vertical) =>
            new(statement.StatementId, null, 1, 1, Array.Empty<GraphNodeLayout>(), Array.Empty<GraphEdgeLayout>(), Array.Empty<GraphEdgeLayout>(), direction);
    }

    private sealed class StubTableProjector : IPlanTableProjector
    {
        public IReadOnlyList<PlanTableRow> Project(StatementPlan statement) =>
            statement.Nodes.Select(node => new PlanTableRow(
                NodeId: node.NodeId,
                ParentNodeId: null,
                Depth: 0,
                HasChildren: false,
                PhysicalOp: node.PhysicalOp,
                LogicalOp: node.LogicalOp,
                ObjectName: string.Empty,
                CostRatio: 0,
                EstimatedSubtreeCost: null,
                EstimatedCpuCost: null,
                EstimatedIoCost: null,
                EstimatedRows: null,
                AverageRowSize: null,
                ActualRows: null,
                ActualExecutions: null,
                ActualLogicalReads: null,
                ActualPhysicalReads: null,
                ActualCpuMs: null,
                ActualElapsedMs: null,
                WarningCount: 0,
                IsParallel: false,
                Summary: string.Empty)).ToArray();
    }
}

file static class LoadedPlanTestExtensions
{
    public static LoadedPlan WithState(this LoadedPlan plan, string selectedNodeId)
    {
        plan.SelectedNodeId = selectedNodeId;
        plan.HoveredNodeId = selectedNodeId;
        plan.IsStatementDetailsSelected = true;
        return plan;
    }
}

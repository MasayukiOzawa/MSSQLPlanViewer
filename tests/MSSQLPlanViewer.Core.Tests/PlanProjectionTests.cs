using MSSQLPlanViewer.Core.Models;
using MSSQLPlanViewer.Core.Parsing;
using MSSQLPlanViewer.Core.Rendering;

namespace MSSQLPlanViewer.Core.Tests;

public sealed class PlanProjectionTests
{
    private readonly ShowplanParser parser = new();
    private readonly PlanGraphLayoutService graphLayoutService = new();
    private readonly PlanTableProjector tableProjector = new();

    [Fact]
    public void GraphLayout_AssignsCoordinatesToAllNodes()
    {
        var document = parser.Parse(SamplePlanLoader.Load("nested-loops-2022.sqlplan"));
        var statement = document.Statements[0];

        var layout = graphLayoutService.CreateLayout(statement);
        var nodesById = layout.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);

        Assert.Equal(statement.Nodes.Count, layout.Nodes.Count);
        Assert.Equal(statement.Edges.Count, layout.Edges.Count);
        Assert.True(layout.Width > 0);
        Assert.True(layout.Height > 0);
        Assert.All(layout.Nodes, node => Assert.True(node.Y >= 0));
        Assert.True(nodesById["0"].Y < nodesById["1"].Y);
        Assert.Equal(nodesById["1"].Y, nodesById["2"].Y);
        Assert.Equal(24, nodesById["0"].X);
        Assert.Equal(12, nodesById["0"].EstimatedRows);
        Assert.Equal(1, nodesById["0"].EstimatedExecutions);
        Assert.Equal(0.001m, nodesById["0"].EstimatedCpuCost);
        Assert.Equal(12, nodesById["0"].ActualRows);
        Assert.Equal(1, nodesById["0"].ActualExecutions);
        Assert.Equal(1, nodesById["0"].ActualCpuMs);
        Assert.Equal(2, nodesById["0"].ActualElapsedMs);
        Assert.Contains(nodesById["0"].X, new[] { nodesById["1"].X, nodesById["2"].X });
        Assert.True(nodesById["1"].X > nodesById["0"].X || nodesById["2"].X > nodesById["0"].X);
        Assert.NotEqual(nodesById["1"].X, nodesById["2"].X);
    }

    [Fact]
    public void GraphLayout_MarksNodesWithImplicitConversions()
    {
        var summary = TestPlanFactory.EmptySummary with
        {
            ImplicitConversionEntries = new[]
            {
                new ImplicitConversionEntry(
                    NodeId: "2",
                    PhysicalOp: "Compute Scalar",
                    LogicalOp: "Compute Scalar",
                    Database: null,
                    Schema: null,
                    Table: null,
                    Index: null,
                    IndexKind: null,
                    Source: "Defined values",
                    Expression: "CONVERT_IMPLICIT(int,[Expr1001],0)")
            }
        };
        var statement = TestPlanFactory.Statement(
            new[]
            {
                TestPlanFactory.Node("1", physicalOp: "Index Seek", logicalOp: "Index Seek"),
                TestPlanFactory.Node("2", physicalOp: "Compute Scalar", logicalOp: "Compute Scalar")
            },
            rootNodeIds: new[] { "1", "2" },
            summary: summary);

        var layout = graphLayoutService.CreateLayout(statement);
        var implicitConversionByNode = layout.Nodes.ToDictionary(node => node.NodeId, node => node.HasImplicitConversion, StringComparer.Ordinal);

        Assert.False(implicitConversionByNode["1"]);
        Assert.True(implicitConversionByNode["2"]);
    }

    [Fact]
    public void GraphLayout_AddsStatementNodeFromStatementType()
    {
        var summary = new StatementPlanSummary(10m, 1, null, null, null, null, null, null, Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<OptimizerStatsUsageEntry>(), Array.Empty<MissingIndexEntry>(), Array.Empty<WaitStatEntry>(), Array.Empty<AccessedObjectEntry>(), Array.Empty<AccessedIndexEntry>(), Array.Empty<SeekScanPredicateEntry>(), Array.Empty<ParameterListEntry>());
        var statement = new StatementPlan(
            StatementId: "1",
            StatementType: "SELECT",
            StatementText: "SELECT * FROM dbo.NATION",
            Summary: summary,
            Nodes: new[] { CreateNode("0", 10m) },
            Edges: Array.Empty<PlanEdge>(),
            Warnings: Array.Empty<PlanWarning>(),
            RootNodeIds: new[] { "0" });

        var layout = graphLayoutService.CreateLayout(statement, 0.25m);

        var statementNode = Assert.IsType<StatementGraphNodeLayout>(layout.StatementNode);
        Assert.Equal("SELECT", statementNode.PrimaryLabel);
        Assert.Equal("SELECT * FROM dbo.NATION", statementNode.SecondaryLabel);
        Assert.Equal(0.25m, statementNode.CostRatio);
        Assert.True(statementNode.Y < layout.Nodes[0].Y);

        var statementEdge = Assert.Single(layout.StatementEdges);
        Assert.Equal("1", statementEdge.FromNodeId);
        Assert.Equal("0", statementEdge.ToNodeId);
        Assert.False(statementEdge.IsOnCriticalPath);
    }

    [Fact]
    public void GraphLayout_FallsBackWhenStatementTypeIsShowplanElementName()
    {
        var summary = new StatementPlanSummary(10m, 1, null, null, null, null, null, null, Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<OptimizerStatsUsageEntry>(), Array.Empty<MissingIndexEntry>(), Array.Empty<WaitStatEntry>(), Array.Empty<AccessedObjectEntry>(), Array.Empty<AccessedIndexEntry>(), Array.Empty<SeekScanPredicateEntry>(), Array.Empty<ParameterListEntry>());
        var statement = new StatementPlan(
            StatementId: "1",
            StatementType: "StmtSimple",
            StatementText: "SELECT * FROM dbo.NATION",
            Summary: summary,
            Nodes: new[] { CreateNode("0", 10m) },
            Edges: Array.Empty<PlanEdge>(),
            Warnings: Array.Empty<PlanWarning>(),
            RootNodeIds: new[] { "0" });

        var layout = graphLayoutService.CreateLayout(statement);

        var statementNode = Assert.IsType<StatementGraphNodeLayout>(layout.StatementNode);
        Assert.Equal("Statement", statementNode.PrimaryLabel);
        Assert.Equal("SELECT * FROM dbo.NATION", statementNode.SecondaryLabel);
    }

    [Fact]
    public void GraphLayout_HorizontalSsms_LaysOutTreeLeftToRightWithRightToLeftEdges()
    {
        var summary = new StatementPlanSummary(10m, 1, null, null, null, null, null, null, Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<OptimizerStatsUsageEntry>(), Array.Empty<MissingIndexEntry>(), Array.Empty<WaitStatEntry>(), Array.Empty<AccessedObjectEntry>(), Array.Empty<AccessedIndexEntry>(), Array.Empty<SeekScanPredicateEntry>(), Array.Empty<ParameterListEntry>());
        var statement = new StatementPlan(
            StatementId: "1",
            StatementType: "SELECT",
            StatementText: "SELECT * FROM dbo.NATION",
            Summary: summary,
            Nodes: new[] { CreateNode("0", 10m), CreateNode("1", 8m), CreateNode("2", 2m) },
            Edges: new[] { new PlanEdge("0", "1"), new PlanEdge("0", "2") },
            Warnings: Array.Empty<PlanWarning>(),
            RootNodeIds: new[] { "0" });

        var layout = graphLayoutService.CreateLayout(statement, direction: GraphLayoutDirection.HorizontalSsms);
        var statementNode = Assert.IsType<StatementGraphNodeLayout>(layout.StatementNode);
        var nodesById = layout.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);

        Assert.Equal(GraphLayoutDirection.HorizontalSsms, layout.Direction);
        Assert.True(statementNode.X < nodesById["0"].X);
        Assert.True(nodesById["0"].X < nodesById["1"].X);
        Assert.True(nodesById["0"].X < nodesById["2"].X);
        Assert.Equal(nodesById["0"].Y, nodesById["1"].Y);
        Assert.True(nodesById["2"].Y > nodesById["0"].Y);

        var statementEdge = Assert.Single(layout.StatementEdges);
        Assert.Equal("1", statementEdge.FromNodeId);
        Assert.Equal("0", statementEdge.ToNodeId);
        Assert.True(statementEdge.X1 > statementEdge.X2);

        var firstChildEdge = Assert.Single(layout.Edges, edge => edge.ToNodeId == "1");
        Assert.Equal("0", firstChildEdge.FromNodeId);
        Assert.True(firstChildEdge.X1 > firstChildEdge.X2);
        Assert.Equal(firstChildEdge.Y1, firstChildEdge.Y2);
    }

    [Fact]
    public void GraphLayout_ScalesOperatorEdgeWidthByFlowRows()
    {
        var summary = new StatementPlanSummary(10m, 1, null, null, null, null, null, null, Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<OptimizerStatsUsageEntry>(), Array.Empty<MissingIndexEntry>(), Array.Empty<WaitStatEntry>(), Array.Empty<AccessedObjectEntry>(), Array.Empty<AccessedIndexEntry>(), Array.Empty<SeekScanPredicateEntry>(), Array.Empty<ParameterListEntry>());
        var statement = new StatementPlan(
            StatementId: "1",
            StatementType: "SELECT",
            StatementText: "SELECT * FROM dbo.NATION",
            Summary: summary,
            Nodes: new[]
            {
                CreateNode("0", 10m, estimatedRows: 10, actualRows: 10),
                CreateNode("1", 8m, estimatedRows: 1_000, actualRows: 1_000),
                CreateNode("2", 2m, estimatedRows: 10, actualRows: 10)
            },
            Edges: new[] { new PlanEdge("0", "1"), new PlanEdge("0", "2") },
            Warnings: Array.Empty<PlanWarning>(),
            RootNodeIds: new[] { "0" });

        var layout = graphLayoutService.CreateLayout(statement, direction: GraphLayoutDirection.HorizontalSsms);

        var highFlowEdge = Assert.Single(layout.Edges, edge => edge.ToNodeId == "1");
        var lowFlowEdge = Assert.Single(layout.Edges, edge => edge.ToNodeId == "2");
        Assert.Equal(1_000d, highFlowEdge.FlowRows.GetValueOrDefault());
        Assert.Equal(1_000d, highFlowEdge.EstimatedRows.GetValueOrDefault());
        Assert.Equal(1_000d, highFlowEdge.ActualRows.GetValueOrDefault());
        Assert.Equal(10d, lowFlowEdge.FlowRows.GetValueOrDefault());
        Assert.Equal(10d, lowFlowEdge.EstimatedRows.GetValueOrDefault());
        Assert.Equal(10d, lowFlowEdge.ActualRows.GetValueOrDefault());
        Assert.True(highFlowEdge.StrokeWidth > lowFlowEdge.StrokeWidth);
        Assert.InRange(lowFlowEdge.StrokeWidth, 1.6d, 12d);
        Assert.InRange(highFlowEdge.StrokeWidth, 1.6d, 12d);
        Assert.True(highFlowEdge.StrokeWidth > 10d);
    }

    [Fact]
    public void TableProjection_PreservesOperatorHierarchy()
    {
        var document = parser.Parse(SamplePlanLoader.Load("nested-loops-2022.sqlplan"));
        var statement = document.Statements[0];

        var rows = tableProjector.Project(statement);

        Assert.Equal(statement.Nodes.Count, rows.Count);
        Assert.Equal("0", rows[0].NodeId);
        Assert.Null(rows[0].ParentNodeId);
        Assert.Equal(0, rows[0].Depth);
        Assert.True(rows[0].HasChildren);
        Assert.Equal("1", rows[1].NodeId);
        Assert.Equal("0", rows[1].ParentNodeId);
        Assert.Equal(1, rows[1].Depth);
        Assert.Equal("2", rows[2].NodeId);
        Assert.Equal("0", rows[2].ParentNodeId);
        Assert.Equal(1, rows[2].Depth);
        Assert.Equal(1m, rows[0].CostRatio);
        Assert.Equal(0.001m, rows[0].EstimatedCpuCost);
        Assert.Equal(0.002m, rows[0].EstimatedIoCost);
        Assert.Equal(28, rows[0].AverageRowSize);
        Assert.Equal(1, rows[0].WarningCount);
        Assert.Equal(1, rows[0].ActualExecutions);
        Assert.Equal(14, rows[0].ActualLogicalReads);
        Assert.Equal(0, rows[0].ActualPhysicalReads);
        Assert.Equal(1, rows[0].ActualCpuMs);
        Assert.Equal(2, rows[0].ActualElapsedMs);
        Assert.Contains("Cost 100%", rows[0].Summary);
        Assert.Contains("EstSubtree 0.034", rows[0].Summary);
        Assert.Contains("EstCPU 0.001", rows[0].Summary);
        Assert.Contains("EstIO 0.002", rows[0].Summary);
        Assert.DoesNotContain("EstRows", rows[0].Summary);
        Assert.DoesNotContain("ActualRows", rows[0].Summary);
        Assert.DoesNotContain("ActualExecs", rows[0].Summary);
        Assert.DoesNotContain("Parallel", rows[0].Summary);
        Assert.Contains("NoJoinPredicate", rows[0].Summary);
    }

    [Fact]
    public void GraphAndTableProjection_DoNotDoubleBracketAlreadyBracketedObjectNames()
    {
        var document = parser.Parse(SamplePlanLoader.Load("diagnostics-2022.sqlplan"));
        var statement = document.Statements[0];

        var layout = graphLayoutService.CreateLayout(statement);
        var graphNode = layout.Nodes.Single(node => node.NodeId == "1");
        var tableRow = tableProjector.Project(statement).Single(row => row.NodeId == "1");

        const string expected = "[SalesDb].[dbo].[Orders] / [IX_Orders_CustomerCode] (NonClustered)";
        Assert.Equal(expected, graphNode.SecondaryLabel);
        Assert.Equal(expected, tableRow.ObjectName);
        Assert.DoesNotContain("[[", graphNode.SecondaryLabel, StringComparison.Ordinal);
        Assert.DoesNotContain("[[", tableRow.ObjectName, StringComparison.Ordinal);
    }

    [Fact]
    public void GraphLayout_ReusesHorizontalSpaceForSideBranchesAtDifferentDepths()
    {
        var summary = new StatementPlanSummary(100m, 1, null, null, null, null, null, null, Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<OptimizerStatsUsageEntry>(), Array.Empty<MissingIndexEntry>(), Array.Empty<WaitStatEntry>(), Array.Empty<AccessedObjectEntry>(), Array.Empty<AccessedIndexEntry>(), Array.Empty<SeekScanPredicateEntry>(), Array.Empty<ParameterListEntry>());
        var nodes = new[]
        {
            CreateNode("0", 100m),
            CreateNode("1", 90m),
            CreateNode("2", 5m),
            CreateNode("3", 80m),
            CreateNode("4", 4m),
            CreateNode("5", 70m)
        };
        var edges = new[]
        {
            new PlanEdge("0", "1"),
            new PlanEdge("0", "2"),
            new PlanEdge("1", "3"),
            new PlanEdge("1", "4"),
            new PlanEdge("3", "5")
        };

        var statement = new StatementPlan(
            StatementId: "1",
            StatementType: "StmtSimple",
            StatementText: "SELECT 1",
            Summary: summary,
            Nodes: nodes,
            Edges: edges,
            Warnings: Array.Empty<PlanWarning>(),
            RootNodeIds: new[] { "0" });

        var layout = graphLayoutService.CreateLayout(statement);
        var nodesById = layout.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);

        Assert.Equal(nodesById["0"].X, nodesById["1"].X);
        Assert.Equal(nodesById["1"].X, nodesById["3"].X);
        Assert.Equal(nodesById["3"].X, nodesById["5"].X);
        Assert.Equal(nodesById["2"].X, nodesById["4"].X);
        Assert.True(nodesById["2"].X > nodesById["0"].X);
    }

    [Fact]
    public void TableProjection_AddsStatementWarningsToFirstRow()
    {
        var summary = new StatementPlanSummary(10m, 1, null, null, null, null, null, null, Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<PlanProperty>(), Array.Empty<OptimizerStatsUsageEntry>(), Array.Empty<MissingIndexEntry>(), Array.Empty<WaitStatEntry>(), Array.Empty<AccessedObjectEntry>(), Array.Empty<AccessedIndexEntry>(), Array.Empty<SeekScanPredicateEntry>(), Array.Empty<ParameterListEntry>());
        var warnings = new[] { new PlanWarning("PlanAffectingConvert", "true", null) };
        var statement = new StatementPlan(
            StatementId: "1",
            StatementType: "StmtSimple",
            StatementText: "SELECT 1",
            Summary: summary,
            Nodes: new[] { CreateNode("0", 10m) },
            Edges: Array.Empty<PlanEdge>(),
            Warnings: warnings,
            RootNodeIds: new[] { "0" });

        var rows = tableProjector.Project(statement);

        Assert.Single(rows);
        Assert.Equal(1, rows[0].WarningCount);
        Assert.Contains("PlanAffectingConvert", rows[0].Summary);
    }

    [Fact]
    public void Parser_ReadsStatementTypeAttribute()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan" Version="1.2" Build="16.0.1000.6">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementType="SELECT" StatementText="SELECT 1" StatementSubTreeCost="0.1" StatementEstRows="1">
                      <StatementSetOptions QUOTED_IDENTIFIER="true" ANSI_NULLS="true" />
                      <QueryPlan CachedPlanSize="32">
                        <MemoryGrantInfo GrantedMemory="128" MaxUsedMemory="96" />
                        <RelOp NodeId="0" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1" />
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);
        var statement = document.Statements[0];

        Assert.Equal("StmtSimple", statement.StatementElementName);
        Assert.Equal("SELECT", statement.StatementType);
        Assert.Contains(statement.StatementProperties, property => property.Name == "StatementType" && property.Value == "SELECT");
        Assert.Contains(statement.StatementProperties, property => property.Name == "StatementSubTreeCost" && property.Value == "0.1");
        Assert.Contains(statement.StatementSetOptionsProperties, property => property.Name == "QUOTED_IDENTIFIER" && property.Value == "true");
        Assert.Contains(statement.StatementSetOptionsProperties, property => property.Name == "ANSI_NULLS" && property.Value == "true");
        Assert.Contains(statement.Summary.QueryPlanProperties, property => property.Name == "CachedPlanSize" && property.Value == "32");
        Assert.Contains(statement.Summary.MemoryGrantInfoProperties, property => property.Name == "GrantedMemory" && property.Value == "128");
    }

    [Fact]
    public void Parser_ReadsActualExecutionModeIntoNodeProperties()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan" Version="1.2" Build="16.0.1000.6">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT 1" StatementSubTreeCost="0.1" StatementEstRows="1">
                      <QueryPlan CachedPlanSize="32">
                        <RelOp NodeId="0" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1" EstimatedExecutionMode="Batch" ActualExecutionMode="Row" />
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);
        var node = document.Statements[0].Nodes[0];

        var property = Assert.Single(node.Properties, item => item.Name == "ActualExecutionMode");
        Assert.Equal("Row", property.Value);

        var layout = graphLayoutService.CreateLayout(document.Statements[0]);
        Assert.Equal("Batch", layout.Nodes[0].EstimatedExecutionMode);
        Assert.Equal("Row", layout.Nodes[0].ActualExecutionMode);
    }

    [Fact]
    public void Parser_ReadsParallelActualExecutionModeFromThreadOneRuntimeCounter()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan" Version="1.2" Build="16.0.1000.6">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT 1" StatementSubTreeCost="0.1" StatementEstRows="1">
                      <QueryPlan CachedPlanSize="32">
                        <RelOp NodeId="0" PhysicalOp="Compute Scalar" LogicalOp="Compute Scalar" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1" EstimatedExecutionMode="Batch" Parallel="true">
                          <RunTimeInformation>
                            <RunTimeCountersPerThread Thread="4" ActualRows="0" ActualExecutions="1" ActualExecutionMode="Batch" ActualElapsedms="0" ActualCPUms="0" />
                            <RunTimeCountersPerThread Thread="3" ActualRows="0" ActualExecutions="1" ActualExecutionMode="Batch" ActualElapsedms="0" ActualCPUms="0" />
                            <RunTimeCountersPerThread Thread="2" ActualRows="0" ActualExecutions="1" ActualExecutionMode="Batch" ActualElapsedms="0" ActualCPUms="0" />
                            <RunTimeCountersPerThread Thread="1" ActualRows="4" ActualExecutions="1" ActualExecutionMode="Batch" ActualElapsedms="1" ActualCPUms="1" />
                            <RunTimeCountersPerThread Thread="0" ActualRows="0" ActualExecutions="0" ActualExecutionMode="Row" ActualElapsedms="0" ActualCPUms="0" />
                          </RunTimeInformation>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);
        var node = document.Statements[0].Nodes[0];

        Assert.Equal("Batch", node.RuntimeMetrics.ActualExecutionMode);
        Assert.Equal("Batch", node.RuntimeMetrics.Threads.Single(thread => thread.ThreadId == 1).ActualExecutionMode);
        Assert.Contains(node.Properties, item => item.Name == "ActualExecutionMode" && item.Value == "Batch");

        var layout = graphLayoutService.CreateLayout(document.Statements[0]);
        Assert.Equal("Batch", layout.Nodes[0].ActualExecutionMode);
    }

    [Fact]
    public void Parser_CollectsAllOwnedXmlAttributesForOperatorDetails()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan" Version="1.2" Build="16.0.1000.6">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT * FROM T WHERE C > 1" StatementSubTreeCost="0.1" StatementEstRows="1">
                      <QueryPlan CachedPlanSize="32">
                        <RelOp NodeId="0" PhysicalOp="Filter" LogicalOp="Filter" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1" Parallel="false">
                          <Filter StartupExpression="true">
                            <Predicate>
                              <ScalarOperator ScalarString="[dbo].[T].[C]>(1)" />
                            </Predicate>
                            <RelOp NodeId="1" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimateCPU="0" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.01" />
                          </Filter>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);
        var node = document.Statements[0].Nodes.Single(item => item.NodeId == "0");

        Assert.Contains(node.XmlAttributes, item => item.Name == "RelOp.NodeId" && item.Value == "0");
        Assert.Contains(node.XmlAttributes, item => item.Name == "RelOp.Filter.StartupExpression" && item.Value == "true");
        Assert.Contains(node.XmlAttributes, item => item.Name == "RelOp.Filter.Predicate.ScalarOperator.ScalarString" && item.Value == "[dbo].[T].[C]>(1)");
        Assert.DoesNotContain(node.XmlAttributes, item => item.Name == "RelOp.Filter.RelOp.NodeId");
        Assert.DoesNotContain(node.DetailXmlAttributes, item => item.Name == "RelOp.Filter.RelOp.NodeId");
    }

    [Fact]
    public void Parser_ExcludesOutputListAndDefinedValueXmlAttributesFromOperatorDetails()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan" Version="1.2" Build="16.0.1000.6">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT A + 1 FROM T" StatementSubTreeCost="0.1" StatementEstRows="1">
                      <QueryPlan CachedPlanSize="32">
                        <RelOp NodeId="0" PhysicalOp="Compute Scalar" LogicalOp="Compute Scalar" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1">
                          <OutputList>
                            <ColumnReference Table="[T]" Column="Expr1001" />
                          </OutputList>
                          <ComputeScalar>
                            <DefinedValues>
                              <DefinedValue>
                                <ColumnReference Table="[T]" Column="Expr1001" />
                                <ScalarOperator ScalarString="[T].[A]+(1)" />
                              </DefinedValue>
                            </DefinedValues>
                          </ComputeScalar>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);
        var node = document.Statements[0].Nodes.Single(item => item.NodeId == "0");

        Assert.Contains(node.XmlAttributes, item => item.Name == "RelOp.NodeId" && item.Value == "0");
        Assert.DoesNotContain(node.XmlAttributes, item => item.Name.StartsWith("RelOp.OutputList.ColumnReference", StringComparison.Ordinal));
        Assert.DoesNotContain(node.XmlAttributes, item => item.Name.Contains("ComputeScalar.DefinedValue", StringComparison.Ordinal));
        Assert.DoesNotContain(node.XmlAttributes, item => item.Name.Contains("ComputeScalar.DefinedValues.DefinedValue", StringComparison.Ordinal));
        Assert.Contains(node.DetailXmlAttributes, item => item.Name == "RelOp.OutputList.ColumnReference.Table" && item.Value == "[T]");
        Assert.Contains(node.DetailXmlAttributes, item => item.Name == "RelOp.OutputList.ColumnReference.Column" && item.Value == "Expr1001");
        Assert.Contains(node.DetailXmlAttributes, item => item.Name == "RelOp.ComputeScalar.DefinedValues.DefinedValue.ColumnReference.Column" && item.Value == "Expr1001");
        Assert.Contains(node.DetailXmlAttributes, item => item.Name == "RelOp.ComputeScalar.DefinedValues.DefinedValue.ScalarOperator.ScalarString" && item.Value == "[T].[A]+(1)");
    }

    [Fact]
    public void Parser_ExcludesConfiguredRedundantXmlAttributeSubtreesFromOperatorDetails()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan" Version="1.2" Build="16.0.1000.6">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT 1" StatementSubTreeCost="0.1" StatementEstRows="1">
                      <QueryPlan CachedPlanSize="32">
                        <RelOp NodeId="0" PhysicalOp="Sort" LogicalOp="Sort" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1">
                          <Sort Distinct="false">
                            <OrderBy>
                              <OrderByColumn Ascending="true">
                                <ColumnReference Table="[T]" Column="SortColumn" />
                              </OrderByColumn>
                            </OrderBy>
                          </Sort>
                        </RelOp>
                        <RelOp NodeId="1" PhysicalOp="Parallelism" LogicalOp="Gather Streams" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1">
                          <Parallelism>
                            <OrderBy>
                              <OrderByColumn Ascending="true">
                                <ColumnReference Table="[T]" Column="ParallelColumn" />
                              </OrderByColumn>
                            </OrderBy>
                          </Parallelism>
                        </RelOp>
                        <RelOp NodeId="2" PhysicalOp="Top Sort" LogicalOp="Top Sort" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1">
                          <TopSort Rows="10">
                            <OrderBy>
                              <OrderByColumn Ascending="true">
                                <ColumnReference Table="[T]" Column="TopSortColumn" />
                              </OrderByColumn>
                            </OrderBy>
                          </TopSort>
                        </RelOp>
                        <RelOp NodeId="3" PhysicalOp="Hash Match" LogicalOp="Inner Join" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1">
                          <Hash>
                            <HashKeysBuild>
                              <ColumnReference Table="[T]" Column="HashBuildColumn" />
                            </HashKeysBuild>
                            <BuildResidual>
                              <ScalarOperator ScalarString="[T].[HashA]=[T].[HashB]">
                                <Compare CompareOp="EQ">
                                  <ScalarOperator>
                                    <Identifier>
                                      <ColumnReference Table="[T]" Column="HashA" />
                                    </Identifier>
                                  </ScalarOperator>
                                  <ScalarOperator>
                                    <Identifier>
                                      <ColumnReference Table="[T]" Column="HashB" />
                                    </Identifier>
                                  </ScalarOperator>
                                </Compare>
                              </ScalarOperator>
                            </BuildResidual>
                            <ProbeResidual>
                              <ScalarOperator ScalarString="[T].[HashC]=[T].[HashD]">
                                <Compare CompareOp="EQ">
                                  <ScalarOperator>
                                    <Identifier>
                                      <ColumnReference Table="[T]" Column="HashC" />
                                    </Identifier>
                                  </ScalarOperator>
                                  <ScalarOperator>
                                    <Identifier>
                                      <ColumnReference Table="[T]" Column="HashD" />
                                    </Identifier>
                                  </ScalarOperator>
                                </Compare>
                              </ScalarOperator>
                            </ProbeResidual>
                            <DefinedValues>
                              <DefinedValue>
                                <ColumnReference Table="[T]" Column="HashExpr1001" />
                              </DefinedValue>
                            </DefinedValues>
                          </Hash>
                        </RelOp>
                        <RelOp NodeId="4" PhysicalOp="Bitmap" LogicalOp="Bitmap Create" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1">
                          <Bitmap>
                            <HashKeys>
                              <ColumnReference Table="[T]" Column="BitmapColumn" />
                            </HashKeys>
                          </Bitmap>
                        </RelOp>
                        <RelOp NodeId="5" PhysicalOp="Index Scan" LogicalOp="Index Scan" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1">
                          <IndexScan Ordered="true">
                            <DefinedValues>
                              <DefinedValue>
                                <ColumnReference Table="[T]" Column="Expr1001" />
                              </DefinedValue>
                            </DefinedValues>
                            <Predicate>
                              <ScalarOperator ScalarString="[T].[A]=(1)">
                                <Compare CompareOp="EQ">
                                  <ScalarOperator>
                                    <Identifier>
                                      <ColumnReference Table="[T]" Column="A" />
                                    </Identifier>
                                  </ScalarOperator>
                                  <ScalarOperator>
                                    <Const ConstValue="(1)" />
                                  </ScalarOperator>
                                </Compare>
                                <Intrinsic FunctionName="abs">
                                  <ScalarOperator>
                                    <Identifier>
                                      <ColumnReference Table="[T]" Column="B" />
                                    </Identifier>
                                  </ScalarOperator>
                                </Intrinsic>
                              </ScalarOperator>
                            </Predicate>
                            <SeekPredicateNew>
                              <SeekKeys>
                                <Prefix ScanType="EQ">
                                  <RangeColumns>
                                    <ColumnReference Table="[T]" Column="SeekColumn" />
                                  </RangeColumns>
                                  <RangeExpressions>
                                    <ScalarOperator ScalarString="(1)">
                                      <Const ConstValue="(1)" />
                                    </ScalarOperator>
                                  </RangeExpressions>
                                </Prefix>
                              </SeekKeys>
                            </SeekPredicateNew>
                          </IndexScan>
                        </RelOp>
                        <RelOp NodeId="6" PhysicalOp="Nested Loops" LogicalOp="Inner Join" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1">
                          <NestedLoops Optimized="false">
                            <OuterReferences>
                              <ColumnReference Table="[T]" Column="OuterRefColumn" />
                            </OuterReferences>
                          </NestedLoops>
                        </RelOp>
                        <RelOp NodeId="7" PhysicalOp="Adaptive Join" LogicalOp="Inner Join" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1">
                          <AdaptiveJoin Optimized="true">
                            <DefinedValues>
                              <DefinedValue>
                                <ColumnReference Table="[T]" Column="AdaptiveExpr1001" />
                              </DefinedValue>
                            </DefinedValues>
                            <HashKeysBuild>
                              <ColumnReference Table="[T]" Column="AdaptiveBuildColumn" />
                            </HashKeysBuild>
                            <HashKeysProbe>
                              <ColumnReference Table="[T]" Column="AdaptiveProbeColumn" />
                            </HashKeysProbe>
                            <OuterReferences>
                              <ColumnReference Table="[T]" Column="AdaptiveOuterRefColumn" />
                            </OuterReferences>
                          </AdaptiveJoin>
                        </RelOp>
                        <RelOp NodeId="8" PhysicalOp="Stream Aggregate" LogicalOp="Aggregate" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1">
                          <RunTimeInformation>
                            <RunTimeCountersPerThread Thread="0" ActualRows="1" ActualExecutions="1" />
                          </RunTimeInformation>
                          <StreamAggregate>
                            <GroupBy>
                              <ColumnReference Table="[T]" Column="AggregateGroupColumn" />
                            </GroupBy>
                          </StreamAggregate>
                        </RelOp>
                        <RelOp NodeId="9" PhysicalOp="Compute Scalar" LogicalOp="Compute Scalar" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1">
                          <ComputeScalar ScalarString="keep-me">
                            <Values>
                              <ScalarExpressionList>
                                <ScalarOperator ScalarString="[T].[Expr1002]+(1)" />
                              </ScalarExpressionList>
                            </Values>
                          </ComputeScalar>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);
        var nodes = document.Statements[0].Nodes.ToDictionary(node => node.NodeId);

        Assert.DoesNotContain(nodes["0"].XmlAttributes, item => item.Name.StartsWith("RelOp.Sort.OrderBy.OrderByColumn", StringComparison.Ordinal) || item.Name.StartsWith("RelOp.Sort.OrderBy.OrderByColumns", StringComparison.Ordinal) || item.Name.StartsWith("RelOp.Sort.OrderBy.OrderByColums", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["1"].XmlAttributes, item => item.Name.StartsWith("RelOp.Parallelism.OrderBy", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["2"].XmlAttributes, item => item.Name.StartsWith("RelOp.TopSort.OrderBy.OrderByColumn", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["3"].XmlAttributes, item => item.Name.StartsWith("RelOp.Hash.HashKeysBuild.ColumnReference", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["3"].XmlAttributes, item => item.Name.StartsWith("RelOp.Hash.DefinedValues.DefinedValue", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["3"].XmlAttributes, item => item.Name.StartsWith("RelOp.Hash.BuildResidual.ScalarOperator", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["3"].XmlAttributes, item => item.Name.StartsWith("RelOp.Hash.ProbeResidual.ScalarOperator", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["4"].XmlAttributes, item => item.Name.StartsWith("RelOp.Bitmap.HashKeys.ColumnReference", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["5"].XmlAttributes, item => item.Name.StartsWith("RelOp.IndexScan.DefinedValues", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["5"].XmlAttributes, item => item.Name.StartsWith("RelOp.IndexScan.Predicate", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["5"].XmlAttributes, item => item.Name.StartsWith("RelOp.IndexScan.SeekPredicate", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["5"].XmlAttributes, item => item.Name.StartsWith("RelOp.IndexScan.Predicate.ScalarOperator.Compare.ScalarOperator", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["5"].XmlAttributes, item => item.Name.StartsWith("RelOp.IndexScan.Predicate.ScalarOperator.Intrinsic.ScalarOperator", StringComparison.Ordinal));
        Assert.Contains(nodes["5"].XmlAttributes, item => item.Name == "RelOp.IndexScan.Ordered" && item.Value == "true");
        Assert.DoesNotContain(nodes["6"].XmlAttributes, item => item.Name.StartsWith("RelOp.NestedLoops.OuterReferences.ColumnReference", StringComparison.Ordinal));
        Assert.Contains(nodes["6"].XmlAttributes, item => item.Name == "RelOp.NestedLoops.Optimized" && item.Value == "false");
        Assert.DoesNotContain(nodes["7"].XmlAttributes, item => item.Name.StartsWith("RelOp.AdaptiveJoin.DefinedValues", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["7"].XmlAttributes, item => item.Name.StartsWith("RelOp.AdaptiveJoin.HashKeysBuild", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["7"].XmlAttributes, item => item.Name.StartsWith("RelOp.AdaptiveJoin.HashKeysProbe", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["7"].XmlAttributes, item => item.Name.StartsWith("RelOp.AdaptiveJoin.OuterReferences", StringComparison.Ordinal));
        Assert.Contains(nodes["7"].XmlAttributes, item => item.Name == "RelOp.AdaptiveJoin.Optimized" && item.Value == "true");
        Assert.DoesNotContain(nodes["8"].XmlAttributes, item => item.Name.StartsWith("RelOp.StreamAggregate.GroupBy.ColumnReference", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["8"].XmlAttributes, item => item.Name.StartsWith("RelOp.RunTimeInformation", StringComparison.Ordinal));
        Assert.DoesNotContain(nodes["9"].XmlAttributes, item => item.Name.StartsWith("RelOp.ComputeScalar.Values.ScalarExpressionList", StringComparison.Ordinal));
        Assert.Contains(nodes["9"].XmlAttributes, item => item.Name == "RelOp.ComputeScalar.ScalarString" && item.Value == "keep-me");
        Assert.Contains(nodes["0"].DetailXmlAttributes, item => item.Name == "RelOp.Sort.OrderBy.OrderByColumn.ColumnReference.Column" && item.Value == "SortColumn");
        Assert.Contains(nodes["3"].DetailXmlAttributes, item => item.Name == "RelOp.Hash.HashKeysBuild.ColumnReference.Column" && item.Value == "HashBuildColumn");
        Assert.Contains(nodes["5"].DetailXmlAttributes, item => item.Name == "RelOp.IndexScan.Predicate.ScalarOperator.ScalarString" && item.Value == "[T].[A]=(1)");
        Assert.Contains(nodes["5"].DetailXmlAttributes, item => item.Name == "RelOp.IndexScan.SeekPredicateNew.SeekKeys.Prefix.ScanType" && item.Value == "EQ");
        Assert.Contains(nodes["8"].DetailXmlAttributes, item => item.Name == "RelOp.RunTimeInformation.RunTimeCountersPerThread.ActualRows" && item.Value == "1");
        Assert.Contains(nodes["9"].DetailXmlAttributes, item => item.Name == "RelOp.ComputeScalar.Values.ScalarExpressionList.ScalarOperator.ScalarString" && item.Value == "[T].[Expr1002]+(1)");
    }

    [Fact]
    public void Parser_ReadsQueryPlanMetadataSections()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan" Version="1.2" Build="16.0.1000.6">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT 1" StatementSubTreeCost="0.1" StatementEstRows="1" CardinalityEstimationModelVersion="160">
                      <StatementSetOptions QUOTED_IDENTIFIER="true" ANSI_NULLS="true" />
                      <QueryPlan CachedPlanSize="32" CompileTime="6" CompileCPU="3" CompileMemory="256" EstimatedAvailableMemoryGrant="1024" EstimatedMemoryGrant="128" BatchModeOnRowStoreUsed="true">
                        <ThreadStat Branches="1" UsedThreads="4" Threads="4" />
                        <MemoryGrantInfo SerialRequiredMemory="64" SerialDesiredMemory="128" GrantedMemory="128" MaxUsedMemory="96" />
                        <OptimizerHardwareDependentProperties EstimatedAvailableMemoryGrant="1024" EstimatedPagesCached="2048" EstimatedAvailableDegreeOfParallelism="4" MaxCompileMemory="8192" />
                        <RelOp NodeId="0" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1" />
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);
        var statement = document.Statements[0];
        var summary = statement.Summary;

        Assert.Contains(statement.StatementProperties, property => property.Name == "CardinalityEstimationModelVersion" && property.Value == "160");
        Assert.Contains(statement.StatementSetOptionsProperties, property => property.Name == "QUOTED_IDENTIFIER" && property.Value == "true");
        Assert.NotEmpty(summary.QueryPlanProperties);
        Assert.Contains(summary.QueryPlanProperties, property => property.Name == "BatchModeOnRowStoreUsed" && property.Value == "true");
        Assert.Single(summary.ThreadStatProperties);
        Assert.Contains(summary.ThreadStatProperties[0], property => property.Name == "UsedThreads" && property.Value == "4");
        Assert.NotEmpty(summary.MemoryGrantInfoProperties);
        Assert.NotEmpty(summary.OptimizerHardwareDependentProperties);
    }

    [Fact]
    public void Parser_ReadsQueryTimeStatsSection()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan" Version="1.2" Build="16.0.1000.6">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT 1" StatementSubTreeCost="0.1" StatementEstRows="1">
                      <QueryPlan CachedPlanSize="32">
                        <QueryTimeStats CpuTime="15" ElapsedTime="19" />
                        <RelOp NodeId="0" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1" />
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);
        var stats = document.Statements[0].Summary.QueryTimeStatsProperties;

        Assert.Equal(2, stats.Count);
        Assert.Equal("CpuTime", stats[0].Name);
        Assert.Equal("15", stats[0].Value);
        Assert.Equal("ElapsedTime", stats[1].Name);
        Assert.Equal("19", stats[1].Value);
    }

    [Fact]
    public void Parser_ReadsOptimizerStatsUsageSection()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan" Version="1.2" Build="16.0.1000.6">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT 1" StatementSubTreeCost="0.1" StatementEstRows="1">
                      <OptimizerStatsUsage>
                        <StatisticsInfo Database="[SalesDb]" Schema="[dbo]" Table="[Orders]" Statistics="[IX_Orders_OrderDate]" LastUpdate="2026-05-01T10:15:30" StatisticsModificationCount="42" LastSample="2026-05-01T10:15:30" SamplingPercent="25" Steps="200" Rows="120000" UnfilteredRows="120000" PersistedSamplePercent="100" />
                      </OptimizerStatsUsage>
                      <QueryPlan CachedPlanSize="32">
                        <RelOp NodeId="0" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1" />
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);
        var entry = Assert.Single(document.Statements[0].Summary.OptimizerStatsUsageEntries);

        Assert.Equal("[SalesDb]", entry.Database);
        Assert.Equal("[dbo]", entry.Schema);
        Assert.Equal("[Orders]", entry.Table);
        Assert.Equal("[IX_Orders_OrderDate]", entry.Statistics);
        Assert.Equal("42", entry.StatisticsModificationCount);
        Assert.Equal("120000", entry.Rows);
    }

    [Fact]
    public void Parser_ReadsMissingIndexesAndWaitStatsSections()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan" Version="1.2" Build="16.0.1000.6">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT 1" StatementSubTreeCost="0.1" StatementEstRows="1">
                      <QueryPlan CachedPlanSize="32">
                        <MissingIndexes>
                          <MissingIndexGroup Impact="95.18">
                            <MissingIndex Database="[AdventureWorks]" Schema="[dbo]" Table="[SalesOrderDetail]">
                              <ColumnGroup Usage="EQUALITY">
                                <Column Name="[ProductID]" />
                              </ColumnGroup>
                              <ColumnGroup Usage="INEQUALITY">
                                <Column Name="[ModifiedDate]" />
                              </ColumnGroup>
                              <ColumnGroup Usage="INCLUDE">
                                <Column Name="[OrderQty]" />
                                <Column Name="[UnitPrice]" />
                              </ColumnGroup>
                            </MissingIndex>
                          </MissingIndexGroup>
                        </MissingIndexes>
                        <WaitStats>
                          <Wait WaitType="PAGEIOLATCH_SH" WaitTimeMs="67588" WaitCount="140599" />
                          <Wait WaitType="CXPACKET" WaitTimeMs="20542" WaitCount="1767" />
                        </WaitStats>
                        <RelOp NodeId="0" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1" />
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);
        var summary = document.Statements[0].Summary;

        Assert.Single(summary.MissingIndexesEntries);
        Assert.Equal("[AdventureWorks].[dbo].[SalesOrderDetail]", summary.MissingIndexesEntries[0].ObjectName);
        Assert.Equal("95.18", summary.MissingIndexesEntries[0].Impact);
        Assert.Equal("[ProductID]", summary.MissingIndexesEntries[0].EqualityColumns);
        Assert.Equal("[ModifiedDate]", summary.MissingIndexesEntries[0].InequalityColumns);
        Assert.Equal("[OrderQty], [UnitPrice]", summary.MissingIndexesEntries[0].IncludeColumns);

        Assert.Equal(2, summary.WaitStatsEntries.Count);
        Assert.Equal("PAGEIOLATCH_SH", summary.WaitStatsEntries[0].WaitType);
        Assert.Equal(67588, summary.WaitStatsEntries[0].WaitTimeMs);
        Assert.Equal(140599, summary.WaitStatsEntries[0].WaitCount);
    }

    [Fact]
    public void Parser_ReadsQueryPlanLevelWarnings()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan" Version="1.2" Build="16.0.1000.6">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT 1" StatementSubTreeCost="0.1" StatementEstRows="1">
                      <QueryPlan CachedPlanSize="32">
                        <Warnings>
                          <PlanAffectingConvert ConvertIssue="Seek Plan" Expression="CONVERT(int,[t].[c],0)=(1)" />
                        </Warnings>
                        <RelOp NodeId="0" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1" />
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);
        var statement = document.Statements[0];

        Assert.Single(statement.Warnings);
        Assert.Equal("PlanAffectingConvert", statement.Warnings[0].Name);
        Assert.Equal("Seek Plan", statement.Warnings[0].Value);
    }

    [Fact]
    public void Parser_AddsPredicateToNodeProperties()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan" Version="1.2" Build="16.0.1000.6">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT * FROM T WHERE C > 1" StatementSubTreeCost="0.1" StatementEstRows="1">
                      <QueryPlan CachedPlanSize="32">
                        <RelOp NodeId="0" PhysicalOp="Filter" LogicalOp="Filter" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1">
                          <Filter>
                            <Predicate>
                              <ScalarOperator ScalarString="[dbo].[T].[C]>(1)" />
                            </Predicate>
                            <RelOp NodeId="1" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimateCPU="0" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.01" />
                          </Filter>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);
        var node = document.Statements[0].Nodes.Single(planNode => planNode.NodeId == "0");
        var predicate = node.Properties.Single(property => property.Name == "Predicate");

        Assert.Equal("[dbo].[T].[C]>(1)", predicate.Value);
    }

    [Fact]
    public void Parser_AddsJoinConditionToJoinNodeProperties()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan" Version="1.2" Build="16.0.1000.6">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT * FROM A JOIN B ON A.Id = B.Id" StatementSubTreeCost="0.1" StatementEstRows="1">
                      <QueryPlan CachedPlanSize="32">
                        <RelOp NodeId="0" PhysicalOp="Merge Join" LogicalOp="Inner Join" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1">
                          <Merge>
                            <OuterSideJoinColumns>
                              <ColumnReference Table="[A]" Column="Id" />
                            </OuterSideJoinColumns>
                            <InnerSideJoinColumns>
                              <ColumnReference Table="[B]" Column="Id" />
                            </InnerSideJoinColumns>
                            <RelOp NodeId="1" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimateCPU="0" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.01" />
                            <RelOp NodeId="2" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimateCPU="0" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.01" />
                          </Merge>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);
        var node = document.Statements[0].Nodes.Single(planNode => planNode.NodeId == "0");
        var joinCondition = node.Properties.Single(property => property.Name == "Join condition");

        Assert.Equal("[A].Id = [B].Id", joinCondition.Value);
    }

    [Fact]
    public void Parser_AddsSeekPredicateToNodeProperties()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan" Version="1.2" Build="16.0.1000.6">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT * FROM A WHERE Id = B.Id" StatementSubTreeCost="0.1" StatementEstRows="1">
                      <QueryPlan CachedPlanSize="32">
                        <RelOp NodeId="0" PhysicalOp="Clustered Index Seek" LogicalOp="Clustered Index Seek" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1">
                          <IndexScan>
                            <SeekPredicates>
                              <SeekPredicateNew>
                                <SeekKeys>
                                  <Prefix ScanType="EQ">
                                    <RangeColumns>
                                      <ColumnReference Table="[A]" Column="Id" />
                                    </RangeColumns>
                                    <RangeExpressions>
                                      <ScalarOperator ScalarString="[B].Id" />
                                    </RangeExpressions>
                                  </Prefix>
                                </SeekKeys>
                              </SeekPredicateNew>
                            </SeekPredicates>
                          </IndexScan>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);
        var node = document.Statements[0].Nodes.Single(planNode => planNode.NodeId == "0");
        var seekPredicate = node.Properties.Single(property => property.Name == "Seek predicate");

        Assert.Equal("Prefix (EQ): [A].Id = [B].Id", seekPredicate.Value);
    }

    [Fact]
    public void Parser_AddsOrderByTopAndRuntimeLoopProperties()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan" Version="1.2" Build="16.0.1000.6">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT TOP 10 * FROM A ORDER BY A.Id" StatementSubTreeCost="0.1" StatementEstRows="10">
                      <QueryPlan CachedPlanSize="32">
                        <RelOp NodeId="0" PhysicalOp="Top" LogicalOp="Top" EstimateRows="10" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1">
                          <RunTimeInformation>
                            <RunTimeCountersPerThread Thread="0" ActualRows="10" ActualExecutions="1" ActualRebinds="2" ActualRewinds="1" />
                          </RunTimeInformation>
                          <Top RowCount="false" IsPercent="false" WithTies="true">
                            <TopExpression>
                              <ScalarOperator ScalarString="(10)" />
                            </TopExpression>
                            <TieColumns>
                              <ColumnReference Table="[A]" Column="Id" />
                            </TieColumns>
                            <RelOp NodeId="1" PhysicalOp="Sort" LogicalOp="Sort" EstimateRows="10" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.09">
                              <Sort Distinct="false">
                                <OrderBy>
                                  <OrderByColumn Ascending="false">
                                    <ColumnReference Table="[A]" Column="Id" />
                                  </OrderByColumn>
                                </OrderBy>
                                <RelOp NodeId="2" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="10" EstimateCPU="0" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.01" />
                              </Sort>
                            </RelOp>
                          </Top>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);
        var topNode = document.Statements[0].Nodes.Single(planNode => planNode.NodeId == "0");
        var sortNode = document.Statements[0].Nodes.Single(planNode => planNode.NodeId == "1");

        Assert.Equal("(10)", topNode.Properties.Single(property => property.Name == "Top expression").Value);
        Assert.Equal("[A].Id", topNode.Properties.Single(property => property.Name == "Tie columns").Value);
        Assert.Equal("2", topNode.Properties.Single(property => property.Name == "Actual rebinds").Value);
        Assert.Equal("1", topNode.Properties.Single(property => property.Name == "Actual rewinds").Value);
        Assert.Equal("RowCount=false, IsPercent=false, WithTies=true", topNode.Properties.Single(property => property.Name == "Top").Value);
        Assert.Equal("[A].Id DESC", sortNode.Properties.Single(property => property.Name == "Order by").Value);
        Assert.Equal("Distinct=false", sortNode.Properties.Single(property => property.Name == "Sort").Value);
    }

    [Fact]
    public void Parser_AddsDefinedValuesAndGroupByProperties()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan" Version="1.2" Build="16.0.1000.6">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT COUNT(*) FROM A GROUP BY A.Id" StatementSubTreeCost="0.1" StatementEstRows="1">
                      <QueryPlan CachedPlanSize="32">
                        <RelOp NodeId="0" PhysicalOp="Compute Scalar" LogicalOp="Compute Scalar" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.1">
                          <ComputeScalar>
                            <DefinedValues>
                              <DefinedValue>
                                <ColumnReference Column="Expr1001" />
                                <ScalarOperator ScalarString="CONVERT(int,[A].Id)" />
                              </DefinedValue>
                            </DefinedValues>
                            <RelOp NodeId="1" PhysicalOp="Segment" LogicalOp="Segment" EstimateRows="1" EstimateCPU="0.001" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.09">
                              <Segment>
                                <GroupBy>
                                  <ColumnReference Table="[A]" Column="Id" />
                                </GroupBy>
                                <RelOp NodeId="2" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" EstimateRows="1" EstimateCPU="0" EstimateIO="0" AvgRowSize="8" EstimatedTotalSubtreeCost="0.01" />
                              </Segment>
                            </RelOp>
                          </ComputeScalar>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);
        var computeNode = document.Statements[0].Nodes.Single(planNode => planNode.NodeId == "0");
        var segmentNode = document.Statements[0].Nodes.Single(planNode => planNode.NodeId == "1");

        Assert.Equal("Expr1001 = CONVERT(int,[A].Id)", computeNode.Properties.Single(property => property.Name == "Defined values").Value);
        Assert.Equal("[A].Id", segmentNode.Properties.Single(property => property.Name == "Group by").Value);
    }

    private static PlanNode CreateNode(string nodeId, decimal cost, double estimatedRows = 1, double? actualRows = null) =>
        new(
            NodeId: nodeId,
            PhysicalOp: "Nested Loops",
            LogicalOp: "Inner Join",
            EstimatedSubtreeCost: cost,
            EstimatedCpuCost: null,
            EstimatedIoCost: null,
            EstimatedRows: estimatedRows,
            AverageRowSize: 1,
            IsParallel: false,
            ObjectReference: null,
            RuntimeMetrics: new PlanRuntimeMetrics(actualRows, null, null, null, null, null, null, null),
            Warnings: Array.Empty<PlanWarning>(),
            Properties: Array.Empty<PlanProperty>(),
            XmlAttributes: Array.Empty<PlanProperty>(),
            DetailXmlAttributes: Array.Empty<PlanProperty>());
}

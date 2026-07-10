using MSSQLPlanViewer.Core.Models;
using MSSQLPlanViewer.Core.Parsing;

namespace MSSQLPlanViewer.Core.Tests;

public sealed class ShowplanParserTests
{
    private readonly IShowplanParser parser = new ShowplanParser();

    [Fact]
    public void Parse_RecognizesModernNamespaceAndWarnings()
    {
        var xml = SamplePlanLoader.Load("nested-loops-2022.sqlplan");

        var document = parser.Parse(xml);

        Assert.Equal(ShowplanSchemaVersion.SqlServer2022, document.Metadata.SchemaVersion);
        Assert.Single(document.Statements);

        var statement = document.Statements[0];
        Assert.Equal("1", statement.StatementId);
        Assert.Equal(3, statement.Nodes.Count);
        Assert.Equal(2, statement.Edges.Count);
        Assert.Contains(statement.Nodes, node => node.NodeId == "0" && node.Warnings.Count == 1);
        Assert.Contains(statement.Nodes, node => node.NodeId == "2" && node.IsParallel);
        Assert.Contains(statement.RootNodeIds, nodeId => nodeId == "0");
    }

    [Fact]
    public void Parse_AssignsBatchNumbersToStatementsFromMultipleBatches()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementType="SELECT" StatementText="SELECT 1">
                      <QueryPlan>
                        <RelOp NodeId="0" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" />
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementType="SELECT" StatementText="SELECT 2">
                      <QueryPlan>
                        <RelOp NodeId="1" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" />
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);

        Assert.Collection(
            document.Statements,
            statement =>
            {
                Assert.Equal(1, statement.BatchNumber);
                Assert.Equal(1, statement.StatementOrdinal);
                Assert.Equal("1", statement.StatementId);
                Assert.Equal("1:1:1", statement.StatementKey);
            },
            statement =>
            {
                Assert.Equal(2, statement.BatchNumber);
                Assert.Equal(2, statement.StatementOrdinal);
                Assert.Equal("1", statement.StatementId);
                Assert.Equal("2:2:1", statement.StatementKey);
            });
    }

    [Fact]
    public void Parse_GroupsSingleStatementXmlBatchesWhenStatementIdsContinue()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2004/07/showplan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementType="SELECT" StatementText="SELECT 1">
                      <QueryPlan>
                        <RelOp NodeId="0" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" />
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="2" StatementType="SELECT" StatementText="SELECT 2">
                      <QueryPlan>
                        <RelOp NodeId="1" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" />
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="3" StatementType="SELECT" StatementText="SELECT 3">
                      <QueryPlan>
                        <RelOp NodeId="2" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" />
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementType="SELECT" StatementText="SELECT 4">
                      <QueryPlan>
                        <RelOp NodeId="3" PhysicalOp="Constant Scan" LogicalOp="Constant Scan" />
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);

        Assert.Equal(4, document.Statements.Count);
        Assert.Equal(2, document.Statements.Select(statement => statement.BatchNumber).Distinct().Count());
        Assert.Collection(
            document.Statements,
            statement =>
            {
                Assert.Equal(1, statement.BatchNumber);
                Assert.Equal("1", statement.StatementId);
            },
            statement =>
            {
                Assert.Equal(1, statement.BatchNumber);
                Assert.Equal("2", statement.StatementId);
            },
            statement =>
            {
                Assert.Equal(1, statement.BatchNumber);
                Assert.Equal("3", statement.StatementId);
            },
            statement =>
            {
                Assert.Equal(2, statement.BatchNumber);
                Assert.Equal("1", statement.StatementId);
            });
    }

    [Fact]
    public void Parse_IsNamespaceAgnosticForLegacySchema()
    {
        var xml = SamplePlanLoader.Load("compute-scalar-2012.sqlplan");

        var document = parser.Parse(xml);

        Assert.Equal(ShowplanSchemaVersion.SqlServer2012, document.Metadata.SchemaVersion);
        Assert.Single(document.Statements);
        Assert.Equal(2, document.Statements[0].Nodes.Count);
        Assert.Equal("Compute Scalar", document.Statements[0].Nodes[0].PhysicalOp);
    }

    [Fact]
    public void Parse_CollectsDistinctAccessedObjectsIntoStatementSummary()
    {
        var xml = SamplePlanLoader.Load("nested-loops-2022.sqlplan");

        var document = parser.Parse(xml);

        var accessedObjects = document.Statements[0].Summary.AccessedObjectEntries;
        Assert.Collection(
            accessedObjects,
            item =>
            {
                Assert.Equal("AdventureWorks", item.Database);
                Assert.Equal("Sales", item.Schema);
                Assert.Equal("SalesOrderDetail", item.Table);
            },
            item =>
            {
                Assert.Equal("AdventureWorks", item.Database);
                Assert.Equal("Sales", item.Schema);
                Assert.Equal("SalesOrderHeader", item.Table);
            });
    }

    [Fact]
    public void Parse_CollectsAccessedIndexesPerOperatorIntoStatementSummary()
    {
        var xml = SamplePlanLoader.Load("nested-loops-2022.sqlplan");

        var document = parser.Parse(xml);

        var accessedIndexes = document.Statements[0].Summary.AccessedIndexEntries;
        Assert.Collection(
            accessedIndexes,
            item =>
            {
                Assert.Equal("1", item.NodeId);
                Assert.Equal("Index Seek", item.PhysicalOp);
                Assert.Equal("Index Seek", item.LogicalOp);
                Assert.Equal("AdventureWorks", item.Database);
                Assert.Equal("Sales", item.Schema);
                Assert.Equal("SalesOrderHeader", item.Table);
                Assert.Equal("IX_SalesOrderHeader_SalesOrderID", item.Index);
                Assert.Equal("NonClustered", item.IndexKind);
                Assert.Equal(12, item.EstimatedRows);
                Assert.Equal(0.0007m, item.EstimatedIoCost);
                Assert.Equal(12, item.ActualRows);
                Assert.Equal(5, item.ActualLogicalReads);
                Assert.Equal(0, item.ActualPhysicalReads);
            },
            item =>
            {
                Assert.Equal("2", item.NodeId);
                Assert.Equal("Clustered Index Seek", item.PhysicalOp);
                Assert.Equal("Clustered Index Seek", item.LogicalOp);
                Assert.Equal("AdventureWorks", item.Database);
                Assert.Equal("Sales", item.Schema);
                Assert.Equal("SalesOrderDetail", item.Table);
                Assert.Equal("PK_SalesOrderDetail", item.Index);
                Assert.Equal("Clustered", item.IndexKind);
                Assert.Equal(12, item.EstimatedRows);
                Assert.Equal(0.0014m, item.EstimatedIoCost);
                Assert.Equal(12, item.ActualRows);
                Assert.Equal(9, item.ActualLogicalReads);
                Assert.Equal(0, item.ActualPhysicalReads);
            });
    }

    [Fact]
    public void Parse_CollectsAccessedIndexesPerOperatorAndSkipsObjectsWithoutIndex()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT * FROM dbo.Orders">
                      <QueryPlan>
                        <RelOp NodeId="1" PhysicalOp="Index Seek" LogicalOp="Index Seek">
                          <IndexScan>
                            <Object Database="SalesDb" Schema="dbo" Table="Orders" Index="IX_Orders_CustomerId" IndexKind="NonClustered" />
                          </IndexScan>
                        </RelOp>
                        <RelOp NodeId="2" PhysicalOp="Index Scan" LogicalOp="Index Scan">
                          <IndexScan>
                            <Object Database="SalesDb" Schema="dbo" Table="Orders" Index="IX_Orders_CustomerId" IndexKind="NonClustered" />
                          </IndexScan>
                        </RelOp>
                        <RelOp NodeId="3" PhysicalOp="Table Scan" LogicalOp="Table Scan">
                          <TableScan>
                            <Object Database="SalesDb" Schema="dbo" Table="Orders" />
                          </TableScan>
                        </RelOp>
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);

        var accessedIndexes = document.Statements[0].Summary.AccessedIndexEntries;
        Assert.Collection(
            accessedIndexes,
            item =>
            {
                Assert.Equal("1", item.NodeId);
                Assert.Equal("Index Seek", item.PhysicalOp);
                Assert.Equal("IX_Orders_CustomerId", item.Index);
                Assert.Null(item.EstimatedRows);
                Assert.Null(item.EstimatedIoCost);
                Assert.Null(item.ActualRows);
                Assert.Null(item.ActualLogicalReads);
                Assert.Null(item.ActualPhysicalReads);
            },
            item =>
            {
                Assert.Equal("2", item.NodeId);
                Assert.Equal("Index Scan", item.PhysicalOp);
                Assert.Equal("IX_Orders_CustomerId", item.Index);
                Assert.Null(item.EstimatedRows);
                Assert.Null(item.EstimatedIoCost);
                Assert.Null(item.ActualRows);
                Assert.Null(item.ActualLogicalReads);
                Assert.Null(item.ActualPhysicalReads);
            });
        Assert.DoesNotContain(accessedIndexes, item => item.NodeId == "3");
    }

    [Fact]
    public void Parse_CollectsSeekScanPredicatesIntoStatementSummary()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT * FROM dbo.Orders WHERE Status = 1 AND OrderId = @OrderId">
                      <QueryPlan>
                        <RelOp NodeId="1" PhysicalOp="Table Scan" LogicalOp="Table Scan">
                          <TableScan>
                            <Object Database="SalesDb" Schema="dbo" Table="Orders" />
                            <Predicate>
                              <ScalarOperator ScalarString="[dbo].[Orders].[Status]=(1)" />
                            </Predicate>
                          </TableScan>
                        </RelOp>
                        <RelOp NodeId="2" PhysicalOp="Clustered Index Seek" LogicalOp="Clustered Index Seek">
                          <IndexScan>
                            <Object Database="SalesDb" Schema="dbo" Table="Orders" Index="PK_Orders" IndexKind="Clustered" />
                            <SeekPredicates>
                              <SeekPredicateNew>
                                <SeekKeys>
                                  <Prefix ScanType="EQ">
                                    <RangeColumns>
                                      <ColumnReference Table="[Orders]" Column="OrderId" />
                                    </RangeColumns>
                                    <RangeExpressions>
                                      <ScalarOperator ScalarString="[@OrderId]" />
                                    </RangeExpressions>
                                  </Prefix>
                                </SeekKeys>
                              </SeekPredicateNew>
                            </SeekPredicates>
                          </IndexScan>
                        </RelOp>
                        <RelOp NodeId="3" PhysicalOp="Index Scan" LogicalOp="Index Scan">
                          <IndexScan>
                            <Object Database="SalesDb" Schema="dbo" Table="Orders" Index="IX_Orders_Status" IndexKind="NonClustered" />
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

        var predicateEntries = document.Statements[0].Summary.SeekScanPredicateEntries;
        Assert.Collection(
            predicateEntries,
            item =>
            {
                Assert.Equal("1", item.NodeId);
                Assert.Equal("Table Scan", item.PhysicalOp);
                Assert.Equal("SalesDb", item.Database);
                Assert.Equal("dbo", item.Schema);
                Assert.Equal("Orders", item.Table);
                Assert.Null(item.Index);
                Assert.Equal("[dbo].[Orders].[Status]=(1)", item.Predicate);
                Assert.Null(item.SeekPredicate);
            },
            item =>
            {
                Assert.Equal("2", item.NodeId);
                Assert.Equal("Clustered Index Seek", item.PhysicalOp);
                Assert.Equal("SalesDb", item.Database);
                Assert.Equal("dbo", item.Schema);
                Assert.Equal("Orders", item.Table);
                Assert.Equal("PK_Orders", item.Index);
                Assert.Equal("Clustered", item.IndexKind);
                Assert.Null(item.Predicate);
                Assert.Equal("Prefix (EQ): [Orders].OrderId = [@OrderId]", item.SeekPredicate);
            });
        Assert.DoesNotContain(predicateEntries, item => item.NodeId == "3");
    }

    [Fact]
    public void Parse_CollectsImplicitConversionsIntoStatementSummary()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT converted values">
                      <QueryPlan>
                        <RelOp NodeId="2" PhysicalOp="Compute Scalar" LogicalOp="Compute Scalar">
                          <ComputeScalar>
                            <DefinedValues>
                              <DefinedValue>
                                <ColumnReference Column="Expr1006" />
                                <ScalarOperator ScalarString="CASE WHEN [Expr1030]=(0) THEN NULL ELSE [Expr1002]/CONVERT_IMPLICIT(decimal(19,0),[Expr1030],0) END" />
                              </DefinedValue>
                              <DefinedValue>
                                <ColumnReference Column="Expr1007" />
                                <ScalarOperator ScalarString="[Expr1003]+(1)" />
                              </DefinedValue>
                            </DefinedValues>
                          </ComputeScalar>
                        </RelOp>
                        <RelOp NodeId="8" PhysicalOp="Index Scan" LogicalOp="Index Scan">
                          <IndexScan>
                            <Object Database="tpch" Schema="dbo" Table="REGION" Index="PK_REGION" IndexKind="Clustered" />
                            <Predicate>
                              <ScalarOperator ScalarString="CONVERT_IMPLICIT(nchar(25),[tpch].[dbo].[REGION].[R_NAME],0)=N'ASIA'" />
                            </Predicate>
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

        var implicitConversions = document.Statements[0].Summary.ImplicitConversionEntries;
        Assert.Collection(
            implicitConversions,
            item =>
            {
                Assert.Equal("2", item.NodeId);
                Assert.Equal("Compute Scalar", item.PhysicalOp);
                Assert.Equal("Defined values", item.Source);
                Assert.Null(item.Table);
                Assert.Contains("CONVERT_IMPLICIT(decimal(19,0),[Expr1030],0)", item.Expression);
            },
            item =>
            {
                Assert.Equal("8", item.NodeId);
                Assert.Equal("Index Scan", item.PhysicalOp);
                Assert.Equal("Predicate", item.Source);
                Assert.Equal("tpch", item.Database);
                Assert.Equal("dbo", item.Schema);
                Assert.Equal("REGION", item.Table);
                Assert.Equal("PK_REGION", item.Index);
                Assert.Equal("Clustered", item.IndexKind);
                Assert.Equal("CONVERT_IMPLICIT(nchar(25),[tpch].[dbo].[REGION].[R_NAME],0)=N'ASIA'", item.Expression);
            });
    }

    [Fact]
    public void Parse_CollectsParameterListEntriesIntoStatementSummary()
    {
        const string xml = """
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan">
              <BatchSequence>
                <Batch>
                  <Statements>
                    <StmtSimple StatementId="1" StatementText="SELECT * FROM dbo.Orders WHERE CustomerId = @CustomerId">
                      <QueryPlan>
                        <ParameterList>
                          <ColumnReference Column="@CustomerId" ParameterDataType="int" ParameterCompiledValue="(42)" ParameterRuntimeValue="(43)" ParameterIsNullable="false" />
                          <ColumnReference Column="@Region" ParameterDataType="nvarchar(20)" ParameterCompiledValue="N'West'" />
                        </ParameterList>
                        <RelOp NodeId="1" PhysicalOp="Index Seek" LogicalOp="Index Seek" />
                      </QueryPlan>
                    </StmtSimple>
                  </Statements>
                </Batch>
              </BatchSequence>
            </ShowPlanXML>
            """;

        var document = parser.Parse(xml);

        var parameters = document.Statements[0].Summary.ParameterListEntries;
        Assert.Collection(
            parameters,
            item =>
            {
                Assert.Equal("@CustomerId", item.Parameter);
                Assert.Equal("int", item.DataType);
                Assert.Equal("(42)", item.CompiledValue);
                Assert.Equal("(43)", item.RuntimeValue);
                Assert.Equal("false", item.IsNullable);
            },
            item =>
            {
                Assert.Equal("@Region", item.Parameter);
                Assert.Equal("nvarchar(20)", item.DataType);
                Assert.Equal("N'West'", item.CompiledValue);
                Assert.Null(item.RuntimeValue);
                Assert.Null(item.IsNullable);
            });
    }

    [Fact]
    public void Parse_ReadsPerThreadRuntimeCountersAndKeepsAggregateTotals()
    {
        var xml = SamplePlanLoader.Load("parallel-skew-2022.sqlplan");

        var document = parser.Parse(xml);
        var scan = document.Statements[0].Nodes.Single(node => node.NodeId == "1");

        Assert.Equal(100000, scan.RuntimeMetrics.ActualRows);
        Assert.Equal(4, scan.RuntimeMetrics.ActualExecutions);
        Assert.Equal(1000, scan.RuntimeMetrics.ActualLogicalReads);
        Assert.Equal(150, scan.RuntimeMetrics.ActualCpuMs);
        Assert.Equal(210, scan.RuntimeMetrics.ActualElapsedMs);
        Assert.Equal(5, scan.RuntimeMetrics.Threads.Count);
        Assert.Collection(
            scan.RuntimeMetrics.Threads,
            thread =>
            {
                Assert.Equal(0, thread.ThreadId);
                Assert.Equal(0, thread.ActualRows);
                Assert.Equal(0, thread.ActualRowsRead);
            },
            thread =>
            {
                Assert.Equal(1, thread.ThreadId);
                Assert.Equal(90000, thread.ActualRows);
                Assert.Equal(90000, thread.ActualRowsRead);
            },
            thread => Assert.Equal(2, thread.ThreadId),
            thread => Assert.Equal(3, thread.ThreadId),
            thread => Assert.Equal(4, thread.ThreadId));
    }

    [Fact]
    public void Parse_ThrowsHelpfulMessageForInvalidXml()
    {
        var exception = Assert.Throws<ShowplanParseException>(() => parser.Parse("<not-xml"));

        Assert.Contains("Failed to parse XML", exception.Message);
    }

    [Fact]
    public void Parse_RejectsDtdToBlockXxe()
    {
        var xml = """
            <?xml version="1.0"?>
            <!DOCTYPE ShowPlanXML [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <ShowPlanXML xmlns="http://schemas.microsoft.com/sqlserver/2022/ShowPlan">&xxe;</ShowPlanXML>
            """;

        var exception = Assert.Throws<ShowplanParseException>(() => parser.Parse(xml));

        Assert.Contains("Failed to parse XML", exception.Message);
    }

    [Fact]
    public void Parse_RejectsInputThatExceedsMaximumLength()
    {
        var oversizedXml = new string('a', (10 * 1024 * 1024) + 1);

        var exception = Assert.Throws<ShowplanParseException>(() => parser.Parse(oversizedXml));

        Assert.Contains("too large", exception.Message);
    }
}

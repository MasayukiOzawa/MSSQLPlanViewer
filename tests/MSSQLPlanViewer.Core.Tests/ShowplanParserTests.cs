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

using MSSQLPlanViewer.Core.Formatting;

namespace MSSQLPlanViewer.Core.Tests;

public sealed class PlanFileNameBuilderTests
{
    [Fact]
    public void BuildBaseName_UsesStatementSuffix()
    {
        var fileName = PlanFileNameBuilder.BuildBaseName("plan graph", "42", "plan-graph");

        Assert.Equal("plan-graph-stmt42", fileName);
    }

    [Fact]
    public void BuildBaseName_FallsBackWhenNameCollapsesToEmpty()
    {
        var fileName = PlanFileNameBuilder.BuildBaseName("!!!", null, "plan-table");

        Assert.Equal("plan-table", fileName);
    }

    [Fact]
    public void BuildFileName_AppendsExtension()
    {
        var fileName = PlanFileNameBuilder.BuildFileName("plan graph", "42", "png", "plan-graph");

        Assert.Equal("plan-graph-stmt42.png", fileName);
    }
}

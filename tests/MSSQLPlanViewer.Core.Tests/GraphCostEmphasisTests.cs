using MSSQLPlanViewer.Core.Rendering;

namespace MSSQLPlanViewer.Core.Tests;

public sealed class GraphCostEmphasisTests
{
    [Theory]
    [InlineData("0.20", 20, GraphCostEmphasisLevel.None)]
    [InlineData("0.21", 20, GraphCostEmphasisLevel.Elevated)]
    [InlineData("0.30", 20, GraphCostEmphasisLevel.High)]
    [InlineData("0.60", 20, GraphCostEmphasisLevel.Critical)]
    [InlineData("0.72", 90, GraphCostEmphasisLevel.None)]
    [InlineData("0.72", 101, GraphCostEmphasisLevel.None)]
    [InlineData("0.01", -1, GraphCostEmphasisLevel.Elevated)]
    public void Resolve_ReturnsExpectedLevel(string costRatio, int threshold, GraphCostEmphasisLevel expected)
    {
        var level = GraphCostEmphasis.Resolve(decimal.Parse(costRatio, System.Globalization.CultureInfo.InvariantCulture), threshold);

        Assert.Equal(expected, level);
    }
}

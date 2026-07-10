using MSSQLPlanViewer.Core.Formatting;
using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Tests;

public sealed class PlanDisplayFormatterTests
{
    [Fact]
    public void FormatCost_ReturnsNotAvailableForNull() =>
        Assert.Equal("n/a", PlanDisplayFormatter.FormatCost(null));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(0.5, "0.5")]
    [InlineData(0.002, "0.002")]
    [InlineData(1500, "1,500")]
    [InlineData(1234.5678, "1,234.5678")]
    public void FormatCost_UsesGroupedInvariantFormatting(double value, string expected) =>
        Assert.Equal(expected, PlanDisplayFormatter.FormatCost((decimal)value));

    [Theory]
    [InlineData(0, "0%")]
    [InlineData(1, "100%")]
    [InlineData(0.26, "26%")]
    [InlineData(0.534, "53%")]
    [InlineData(0.005, "<1%")]
    [InlineData(0.00001, "<1%")]
    public void FormatPercent_DistinguishesSubOnePercentFromZero(double ratio, string expected) =>
        Assert.Equal(expected, PlanDisplayFormatter.FormatPercent((decimal)ratio));

    [Fact]
    public void FormatNumber_ReturnsNotAvailableForNull() =>
        Assert.Equal("n/a", PlanDisplayFormatter.FormatNumber(null));

    [Theory]
    [InlineData(1500, "1,500")]
    [InlineData(-1500, "-1,500")]
    [InlineData(250.5, "250.50")]
    [InlineData(12.5, "12.5")]
    [InlineData(0.125, "0.125")]
    public void FormatNumber_SelectsPrecisionByMagnitude(double value, string expected) =>
        Assert.Equal(expected, PlanDisplayFormatter.FormatNumber(value));

    [Theory]
    [InlineData("1000", "1,000")]
    [InlineData("102400", "102,400")]
    [InlineData("-1500", "-1,500")]
    [InlineData("1234.567800", "1,234.567800")]
    [InlineData("250.50", "250.50")]
    [InlineData("1.2E+6", "1,200,000")]
    public void FormatNumericText_GroupsNumericStrings(string value, string expected) =>
        Assert.Equal(expected, PlanDisplayFormatter.FormatNumericText(value));

    [Theory]
    [InlineData("[dbo].[T].[C]>(1)")]
    [InlineData("2026-05-01T10:15:30")]
    [InlineData("Row")]
    [InlineData("")]
    public void FormatNumericText_LeavesNonNumericStringsUnchanged(string value) =>
        Assert.Equal(value, PlanDisplayFormatter.FormatNumericText(value));

    [Fact]
    public void FormatObjectName_ReturnsNotAvailableForNull() =>
        Assert.Equal("n/a", PlanDisplayFormatter.FormatObjectName(null));

    [Fact]
    public void FormatObjectName_ReturnsNotAvailableWhenAllPartsAreEmpty()
    {
        var reference = new PlanObjectReference(null, null, null, null, null, null, null);

        Assert.Equal("n/a", PlanDisplayFormatter.FormatObjectName(reference));
    }

    [Fact]
    public void FormatObjectName_JoinsDatabaseSchemaTableWithBrackets()
    {
        var reference = new PlanObjectReference(
            Database: "AdventureWorks",
            Schema: "Sales",
            Table: "SalesOrderHeader",
            Index: null,
            Alias: null,
            IndexKind: null,
            Storage: null);

        Assert.Equal(
            "[AdventureWorks].[Sales].[SalesOrderHeader]",
            PlanDisplayFormatter.FormatObjectName(reference));
    }

    [Fact]
    public void FormatObjectName_AppendsIndexAndIndexKind()
    {
        var reference = new PlanObjectReference(
            Database: null,
            Schema: null,
            Table: "SalesOrderDetail",
            Index: "PK_SalesOrderDetail",
            Alias: null,
            IndexKind: "Clustered",
            Storage: null);

        Assert.Equal(
            "[SalesOrderDetail] / [PK_SalesOrderDetail] (Clustered)",
            PlanDisplayFormatter.FormatObjectName(reference));
    }

    [Fact]
    public void FormatObjectName_DoesNotDoubleBracketAlreadyBracketedParts()
    {
        var reference = new PlanObjectReference(
            Database: "[AGDB01]",
            Schema: "[dbo]",
            Table: "[NATION]",
            Index: "[PK_NATION]",
            Alias: null,
            IndexKind: "Clustered",
            Storage: null);

        Assert.Equal(
            "[AGDB01].[dbo].[NATION] / [PK_NATION] (Clustered)",
            PlanDisplayFormatter.FormatObjectName(reference));
    }

    [Fact]
    public void FormatObjectName_FallsBackToAliasWhenNoNamedParts()
    {
        var reference = new PlanObjectReference(
            Database: null,
            Schema: null,
            Table: null,
            Index: null,
            Alias: "h",
            IndexKind: null,
            Storage: null);

        Assert.Equal("h", PlanDisplayFormatter.FormatObjectName(reference));
    }

    [Fact]
    public void FormatQualifiedTableName_JoinsDatabaseSchemaTableWithoutBrackets() =>
        Assert.Equal(
            "AdventureWorks.Sales.SalesOrderHeader",
            PlanDisplayFormatter.FormatQualifiedTableName("AdventureWorks", "Sales", "SalesOrderHeader"));

    [Fact]
    public void FormatQualifiedTableName_OmitsMissingLeadingParts() =>
        Assert.Equal(
            "SalesOrderDetail",
            PlanDisplayFormatter.FormatQualifiedTableName(null, null, "SalesOrderDetail"));

    [Fact]
    public void FormatWarningSummary_ReturnsNoneWhenEmpty() =>
        Assert.Equal("None", PlanDisplayFormatter.FormatWarningSummary(Array.Empty<PlanWarning>()));

    [Fact]
    public void FormatWarningSummary_JoinsDistinctWarningNames()
    {
        var warnings = new[]
        {
            new PlanWarning("NoJoinPredicate", null, null),
            new PlanWarning("SpillToTempDb", null, null),
            new PlanWarning("NoJoinPredicate", "duplicate", "details"),
        };

        Assert.Equal(
            "NoJoinPredicate, SpillToTempDb",
            PlanDisplayFormatter.FormatWarningSummary(warnings));
    }

    [Theory]
    [InlineData("http://schemas.microsoft.com/sqlserver/2022/ShowPlan")]
    [InlineData("https://schemas.microsoft.com/sqlserver/2022/ShowPlan")]
    public void TryGetSafeHttpUrl_AllowsHttpAndHttpsUrls(string value)
    {
        var result = PlanDisplayFormatter.TryGetSafeHttpUrl(value, out var safeUrl);

        Assert.True(result);
        Assert.StartsWith("http", safeUrl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("/relative/path")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void TryGetSafeHttpUrl_RejectsUnsafeOrNonAbsoluteValues(string? value)
    {
        var result = PlanDisplayFormatter.TryGetSafeHttpUrl(value, out var safeUrl);

        Assert.False(result);
        Assert.Equal(string.Empty, safeUrl);
    }
}

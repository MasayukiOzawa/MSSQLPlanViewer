using MSSQLPlanViewer.Core.Formatting;

namespace MSSQLPlanViewer.Core.Tests;

public sealed class PlanDisplayFormatterTests
{
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

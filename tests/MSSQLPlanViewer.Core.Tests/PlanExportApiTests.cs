using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MSSQLPlanViewer.Core.Tests;

public sealed class PlanExportApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public PlanExportApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
    }

    [Fact]
    public async Task ExportCsv_ReturnsCsvFile()
    {
        var response = await _client.PostAsJsonAsync("/api/exports/table?format=csv", new
        {
            showplanXml = SamplePlanLoader.Load("nested-loops-2022.sqlplan")
        });

        response.EnsureSuccessStatusCode();

        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".csv", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("NodeId", body);
        Assert.Contains("Nested Loops", body);
    }

    [Fact]
    public async Task ExportMarkdown_ReturnsMarkdownFile()
    {
        var response = await _client.PostAsJsonAsync("/api/exports/table?format=md", new
        {
            showplanXml = SamplePlanLoader.Load("nested-loops-2022.sqlplan")
        });

        response.EnsureSuccessStatusCode();

        Assert.Equal("text/markdown", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("| NodeId |", body);
    }

    [Fact]
    public async Task ExportJson_ReturnsJsonFile()
    {
        var response = await _client.PostAsJsonAsync("/api/exports/table?format=json", new
        {
            showplanXml = SamplePlanLoader.Load("nested-loops-2022.sqlplan")
        });

        response.EnsureSuccessStatusCode();

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.EndsWith(".json", response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var rows = document.RootElement.EnumerateArray().ToArray();

        Assert.NotEmpty(rows);
        Assert.Contains(rows, row => string.Equals(row.GetProperty("physicalOp").GetString(), "Nested Loops", StringComparison.Ordinal));
        Assert.Equal(JsonValueKind.Number, rows[0].GetProperty("depth").ValueKind);
        Assert.Equal(JsonValueKind.Number, rows[0].GetProperty("costRatio").ValueKind);
        Assert.True(rows[0].TryGetProperty("hasChildren", out _));
    }

    [Fact]
    public async Task ExportSvg_ReturnsSvgFile()
    {
        var response = await _client.PostAsJsonAsync("/api/exports/graph?format=svg", new
        {
            showplanXml = SamplePlanLoader.Load("nested-loops-2022.sqlplan"),
            costHighlightThresholdPercent = 20,
            showCriticalPath = true
        });

        response.EnsureSuccessStatusCode();

        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("<svg", body, StringComparison.Ordinal);
        Assert.Contains("Execution plan graph", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportSvg_WithHorizontalLayoutDirection_ReturnsHorizontalSvg()
    {
        var response = await _client.PostAsJsonAsync("/api/exports/graph?format=svg", new
        {
            showplanXml = SamplePlanLoader.Load("nested-loops-2022.sqlplan"),
            layoutDirection = "horizontal"
        });

        response.EnsureSuccessStatusCode();

        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.StartsWith("<svg", body, StringComparison.Ordinal);
        Assert.Contains("M 332 80 C 296 80, 312 68, 276 68", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportPng_ReturnsPngFile()
    {
        var response = await _client.PostAsJsonAsync("/api/exports/graph?format=png", new
        {
            showplanXml = SamplePlanLoader.Load("nested-loops-2022.sqlplan"),
            costHighlightThresholdPercent = 20,
            showCriticalPath = true
        });

        response.EnsureSuccessStatusCode();

        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 8);
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
    }

    [Fact]
    public async Task ExportEndpoints_ReturnBadRequestForEmptyXml()
    {
        var response = await _client.PostAsJsonAsync("/api/exports/table?format=csv", new
        {
            showplanXml = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("showplanXml", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportEndpoints_ReturnNotFoundForUnknownStatement()
    {
        var response = await _client.PostAsJsonAsync("/api/exports/graph?format=svg", new
        {
            showplanXml = SamplePlanLoader.Load("nested-loops-2022.sqlplan"),
            statementId = "missing"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("missing", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TableExport_ReturnsBadRequestWhenFormatIsMissing()
    {
        var response = await _client.PostAsJsonAsync("/api/exports/table", new
        {
            showplanXml = SamplePlanLoader.Load("nested-loops-2022.sqlplan")
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("format", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TableExport_ReturnsBadRequestWhenFormatIsUnsupported()
    {
        var response = await _client.PostAsJsonAsync("/api/exports/table?format=xml", new
        {
            showplanXml = SamplePlanLoader.Load("nested-loops-2022.sqlplan")
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Supported values", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GraphExport_ReturnsBadRequestWhenFormatIsMissing()
    {
        var response = await _client.PostAsJsonAsync("/api/exports/graph", new
        {
            showplanXml = SamplePlanLoader.Load("nested-loops-2022.sqlplan")
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("format", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GraphExport_ReturnsBadRequestWhenFormatIsUnsupported()
    {
        var response = await _client.PostAsJsonAsync("/api/exports/graph?format=gif", new
        {
            showplanXml = SamplePlanLoader.Load("nested-loops-2022.sqlplan")
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Supported values", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GraphExport_ReturnsBadRequestWhenLayoutDirectionIsUnsupported()
    {
        var response = await _client.PostAsJsonAsync("/api/exports/graph?format=svg", new
        {
            showplanXml = SamplePlanLoader.Load("nested-loops-2022.sqlplan"),
            layoutDirection = "diagonal"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("layoutDirection", body, StringComparison.Ordinal);
        Assert.Contains("vertical, horizontal", body, StringComparison.Ordinal);
    }
}

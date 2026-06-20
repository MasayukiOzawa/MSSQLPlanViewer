using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MSSQLPlanViewer.Core.Tests;

public sealed class OpenApiDocumentationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public OpenApiDocumentationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OpenApiDocument_InDevelopment_IncludesApiPaths()
    {
        using var client = CreateClient("Development");
        using var document = await GetOpenApiDocumentAsync(client);
        var paths = document.RootElement.GetProperty("paths");

        Assert.True(paths.TryGetProperty("/api/exports/table", out var tableExportPath));
        Assert.True(paths.TryGetProperty("/api/exports/graph", out var graphExportPath));
        Assert.True(paths.TryGetProperty("/api/showplans/estimated", out var estimatedShowplanPath));

        var tableOperation = tableExportPath.GetProperty("post");
        AssertParameterEnum(tableOperation, "format", required: true, "csv", "md", "markdown", "json");
        AssertJsonRequestBody(tableOperation, "Body fields:", "showplanXml (required): SQL Server Showplan XML", "statementId (optional): StatementId");
        AssertComponentRequired(document.RootElement, "TableExportRequest", "showplanXml");
        AssertComponentPropertyDescription(document.RootElement, "TableExportRequest", "showplanXml", "Required.");
        AssertComponentPropertyDescription(document.RootElement, "TableExportRequest", "statementId", "Optional.");

        var graphOperation = graphExportPath.GetProperty("post");
        AssertParameterEnum(graphOperation, "format", required: true, "svg", "png");
        AssertParameterEnum(graphOperation, "layoutDirection", required: false, "vertical", "horizontal");
        AssertJsonRequestBody(graphOperation, "Body fields:", "showplanXml (required): SQL Server Showplan XML", "costHighlightThresholdPercent (optional): Operator cost highlight threshold", "layoutDirection (optional): Graph layout direction");
        AssertComponentRequired(document.RootElement, "GraphExportRequest", "showplanXml");
        AssertComponentPropertyEnum(document.RootElement, "GraphExportRequest", "layoutDirection", "vertical", "horizontal");
        AssertComponentPropertyDescription(document.RootElement, "GraphExportRequest", "costHighlightThresholdPercent", "0-100");
        AssertComponentPropertyDescription(document.RootElement, "GraphExportRequest", "showCriticalPath", "Defaults to true");

        var estimatedOperation = estimatedShowplanPath.GetProperty("post");
        AssertJsonRequestBody(estimatedOperation, "Body fields:", "connectionString (required): SQL Server connection string", "query (required): T-SQL query", "commandTimeoutSeconds (optional): Command timeout", "includeAnalysis (optional): Set to true", "analysisFormat (optional): Plan table export format");
        AssertComponentRequired(document.RootElement, "EstimatedShowplanApiRequest", "connectionString", "query");
        AssertEstimatedShowplanRequestExample(document.RootElement, estimatedOperation);
        AssertComponentPropertyDescription(document.RootElement, "EstimatedShowplanApiRequest", "includeAnalysis", "diagnostic");
        AssertComponentPropertyDescription(document.RootElement, "EstimatedShowplanApiRequest", "analysisFormat", "json, md, markdown, csv");
        AssertComponentPropertyEnum(document.RootElement, "EstimatedShowplanApiRequest", "analysisFormat", "json", "md", "markdown", "csv");
    }

    [Fact]
    public async Task OpenApiSampleRequests_CanExecuteExportApis()
    {
        using var client = CreateClient("Development");
        using var document = await GetOpenApiDocumentAsync(client);
        var paths = document.RootElement.GetProperty("paths");
        var tableSample = GetSampleRequestBody(paths.GetProperty("/api/exports/table").GetProperty("post"));
        var graphOperation = paths.GetProperty("/api/exports/graph").GetProperty("post");
        var graphSample = GetSampleRequestBody(graphOperation, "sample-plan-vertical");
        var horizontalGraphSample = GetSampleRequestBody(graphOperation, "sample-plan-horizontal");

        Assert.Contains("<ShowPlanXML", tableSample.GetProperty("showplanXml").GetString(), StringComparison.Ordinal);
        Assert.Equal("vertical", graphSample.GetProperty("layoutDirection").GetString());
        Assert.Equal("horizontal", horizontalGraphSample.GetProperty("layoutDirection").GetString());

        var tableResponse = await client.PostAsync(
            "/api/exports/table?format=csv",
            CreateJsonContent(tableSample));
        tableResponse.EnsureSuccessStatusCode();
        var tableBody = await tableResponse.Content.ReadAsStringAsync();
        Assert.Contains("Compute Scalar", tableBody, StringComparison.Ordinal);

        var graphResponse = await client.PostAsync(
            "/api/exports/graph?format=svg",
            CreateJsonContent(graphSample));
        graphResponse.EnsureSuccessStatusCode();
        Assert.Equal("image/svg+xml", graphResponse.Content.Headers.ContentType?.MediaType);
        var graphBody = await graphResponse.Content.ReadAsStringAsync();
        Assert.StartsWith("<svg", graphBody, StringComparison.Ordinal);

        var horizontalGraphResponse = await client.PostAsync(
            "/api/exports/graph?format=svg",
            CreateJsonContent(horizontalGraphSample));
        horizontalGraphResponse.EnsureSuccessStatusCode();

        var queryOverrideGraphResponse = await client.PostAsync(
            "/api/exports/graph?format=svg&layoutDirection=horizontal",
            CreateJsonContent(graphSample));
        queryOverrideGraphResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task SwaggerUi_InDevelopment_IsAvailable()
    {
        using var client = CreateClient("Development");

        var response = await client.GetAsync("/api-docs/index.html");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(body);
    }

    [Fact]
    public async Task ApiPage_InDevelopment_EmbedsSwaggerUi()
    {
        using var client = CreateClient("Development");

        var body = await client.GetStringAsync("/api");

        Assert.Contains("API Documentation", body, StringComparison.Ordinal);
        Assert.Contains("src=\"/api-docs\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenApiAndSwaggerUi_OutsideDevelopment_AreNotPublished()
    {
        using var client = CreateClient("Production");

        var openApiResponse = await client.GetAsync("/openapi/v1.json");
        var swaggerResponse = await client.GetAsync("/api-docs/index.html");
        var apiPage = await client.GetStringAsync("/api");

        Assert.Equal(HttpStatusCode.NotFound, openApiResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, swaggerResponse.StatusCode);
        Assert.Contains("API documentation is available in Development", apiPage, StringComparison.Ordinal);
        Assert.DoesNotContain("src=\"/api-docs\"", apiPage, StringComparison.Ordinal);
    }

    private static async Task<JsonDocument> GetOpenApiDocumentAsync(HttpClient client)
    {
        var response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    private static void AssertEstimatedShowplanRequestExample(JsonElement document, JsonElement operation)
    {
        var sample = GetSampleRequestBody(operation, "local-sql-server");
        Assert.Contains("Server=localhost", sample.GetProperty("connectionString").GetString(), StringComparison.Ordinal);
        Assert.Contains("sys.objects", sample.GetProperty("query").GetString(), StringComparison.Ordinal);
        Assert.Equal("Local master sample", sample.GetProperty("label").GetString());
        Assert.Equal(30, sample.GetProperty("commandTimeoutSeconds").GetInt32());
        Assert.True(sample.GetProperty("includeAnalysis").GetBoolean());
        Assert.Equal("json", sample.GetProperty("analysisFormat").GetString());

        AssertComponentPropertyExample(document, "EstimatedShowplanApiRequest", "connectionString", "Server=localhost");
        AssertComponentPropertyExample(document, "EstimatedShowplanApiRequest", "query", "sys.objects");
        AssertComponentPropertyExample(document, "EstimatedShowplanApiRequest", "label", "Local master sample");
        AssertComponentPropertyExample(document, "EstimatedShowplanApiRequest", "includeAnalysis", "True");
        AssertComponentPropertyExample(document, "EstimatedShowplanApiRequest", "analysisFormat", "json");
    }

    private static void AssertParameterEnum(JsonElement operation, string parameterName, bool required, params string[] expectedValues)
    {
        var parameter = operation
            .GetProperty("parameters")
            .EnumerateArray()
            .Single(parameter => string.Equals(
                parameter.GetProperty("name").GetString(),
                parameterName,
                StringComparison.Ordinal));

        Assert.Equal(required, parameter.TryGetProperty("required", out var requiredValue) && requiredValue.GetBoolean());
        Assert.Contains("Supported values", parameter.GetProperty("description").GetString(), StringComparison.Ordinal);
        var actualValues = parameter
            .GetProperty("schema")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        Assert.Equal(expectedValues, actualValues);
    }

    private static void AssertComponentPropertyEnum(JsonElement document, string schemaName, string propertyName, params string[] expectedValues)
    {
        var actualValues = document
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schemaName)
            .GetProperty("properties")
            .GetProperty(propertyName)
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();

        Assert.Equal(expectedValues, actualValues);
    }

    private static void AssertJsonRequestBody(JsonElement operation, params string[] expectedDescriptionSubstrings)
    {
        var requestBody = operation.GetProperty("requestBody");
        Assert.True(requestBody.GetProperty("required").GetBoolean());
        Assert.Contains("Content-Type: application/json", requestBody.GetProperty("description").GetString(), StringComparison.Ordinal);
        Assert.True(requestBody.GetProperty("content").TryGetProperty("application/json", out _));

        foreach (var expectedDescriptionSubstring in expectedDescriptionSubstrings)
        {
            Assert.Contains(expectedDescriptionSubstring, requestBody.GetProperty("description").GetString(), StringComparison.Ordinal);
        }
    }

    private static void AssertComponentRequired(JsonElement document, string schemaName, params string[] expectedProperties)
    {
        var actualProperties = document
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schemaName)
            .GetProperty("required")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();

        foreach (var expectedProperty in expectedProperties)
        {
            Assert.Contains(expectedProperty, actualProperties);
        }
    }

    private static void AssertComponentPropertyDescription(JsonElement document, string schemaName, string propertyName, string expectedSubstring)
    {
        var description = document
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schemaName)
            .GetProperty("properties")
            .GetProperty(propertyName)
            .GetProperty("description")
            .GetString();

        Assert.Contains(expectedSubstring, description, StringComparison.Ordinal);
    }
    private static void AssertComponentPropertyExample(JsonElement document, string schemaName, string propertyName, string expectedSubstring)
    {
        var property = document
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(schemaName)
            .GetProperty("properties")
            .GetProperty(propertyName);

        Assert.Contains(expectedSubstring, property.GetProperty("example").ToString(), StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(property.GetProperty("description").GetString()));
    }

    private static JsonElement GetSampleRequestBody(JsonElement operation, string name = "sample-plan") =>
        operation
            .GetProperty("requestBody")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("examples")
            .GetProperty(name)
            .GetProperty("value");

    private static StringContent CreateJsonContent(JsonElement element) =>
        new(element.GetRawText(), Encoding.UTF8, "application/json");

    private HttpClient CreateClient(string environment) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
        }).CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
}

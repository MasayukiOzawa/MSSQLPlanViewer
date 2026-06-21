using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using MSSQLPlanViewer.Core.Diagnostics;
using MSSQLPlanViewer.Core.Models;
using MSSQLPlanViewer.Core.Parsing;
using MSSQLPlanViewer.Core.Rendering;
using MSSQLPlanViewer.Web.Showplans;

namespace MSSQLPlanViewer.Web;

internal static class EstimatedShowplanEndpoints
{
    public static IEndpointRouteBuilder MapEstimatedShowplanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/showplans")
            .DisableAntiforgery()
            .WithTags("Showplans");

        group.MapPost("/estimated", GetEstimatedShowplan)
            .WithName("GetEstimatedShowplan")
            .WithSummary("Retrieve estimated SQL Server showplans")
            .WithDescription("Connects to SQL Server, requests estimated execution plans for the supplied query, and returns the resulting Showplan XML with parsed summary metadata. Send Content-Type: application/json. The request body requires connectionString and query, and can include label, commandTimeoutSeconds, includeAnalysis, and analysisFormat.")
            .Accepts<EstimatedShowplanApiRequest>("application/json")
            .Produces<EstimatedShowplanApiResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout)
            .AddOpenApiOperationTransformer((operation, context, _) =>
            {
                OpenApiDocumentationHelpers.DescribeJsonRequestBody(
                    operation,
                    "connectionString (required): SQL Server connection string used only by this request.",
                    "query (required): T-SQL query for which SQL Server should return estimated execution plans.",
                    "label (optional): Label applied to returned plans.",
                    "commandTimeoutSeconds (optional): Command timeout in seconds. Supported range: 1-300. Defaults to 60.",
                    "includeAnalysis (optional): Set to true to return statement summaries and diagnostics in the response. Defaults to false.",
                    "analysisFormat (optional): Plan table export format to include in analysis.content when includeAnalysis is true. Supported values: json, md, markdown, csv. Defaults to json.");
                DescribeEstimatedShowplanRequest(operation, context.Document);
                return Task.CompletedTask;
            });

        return endpoints;
    }

    private static JsonObject CreateEstimatedShowplanRequestBody() =>
        new()
        {
            ["connectionString"] = JsonValue.Create("Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=true;"),
            ["query"] = JsonValue.Create("SELECT TOP (10) name, object_id FROM sys.objects;"),
            ["label"] = JsonValue.Create("Local master sample"),
            ["commandTimeoutSeconds"] = JsonValue.Create(30),
            ["includeAnalysis"] = JsonValue.Create(true),
            ["analysisFormat"] = JsonValue.Create("json")
        };

    private static void DescribeEstimatedShowplanRequest(OpenApiOperation operation, OpenApiDocument? document)
    {
        if (OpenApiDocumentationHelpers.TryGetJsonRequestBody(operation, out var jsonMediaType))
        {
            var requestBody = CreateEstimatedShowplanRequestBody();
            jsonMediaType.Example ??= requestBody.DeepClone();
            jsonMediaType.Examples ??= new Dictionary<string, IOpenApiExample>(StringComparer.Ordinal);
            jsonMediaType.Examples["local-sql-server"] = new OpenApiExample
            {
                Summary = "Local SQL Server sample",
                Description = "Replace connectionString with a SQL Server connection that has SHOWPLAN permission before using Try it out.",
                Value = requestBody
            };
        }

        if (document?.Components?.Schemas?.TryGetValue(nameof(EstimatedShowplanApiRequest), out var requestSchema) == true)
        {
            OpenApiDocumentationHelpers.AddRequiredSchemaProperty(requestSchema, "connectionString");
            OpenApiDocumentationHelpers.AddRequiredSchemaProperty(requestSchema, "query");
            OpenApiDocumentationHelpers.DescribeSchemaProperty(
                requestSchema,
                "connectionString",
                "Required. SQL Server connection string used only by this request. Replace the sample value with your environment before executing.",
                JsonValue.Create("Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=true;"));
            OpenApiDocumentationHelpers.DescribeSchemaProperty(
                requestSchema,
                "query",
                "Required. T-SQL query for which SQL Server should return estimated execution plans.",
                JsonValue.Create("SELECT TOP (10) name, object_id FROM sys.objects;"));
            OpenApiDocumentationHelpers.DescribeSchemaProperty(
                requestSchema,
                "label",
                "Optional. Label applied to returned plans.",
                JsonValue.Create("Local master sample"));
            OpenApiDocumentationHelpers.DescribeSchemaProperty(
                requestSchema,
                "commandTimeoutSeconds",
                $"Optional. Command timeout in seconds. Supported range: {SqlEstimatedShowplanProvider.MinCommandTimeoutSeconds}-{SqlEstimatedShowplanProvider.MaxCommandTimeoutSeconds}. Defaults to {SqlEstimatedShowplanProvider.DefaultCommandTimeoutSeconds}.",
                JsonValue.Create(30));
            OpenApiDocumentationHelpers.DescribeSchemaProperty(
                requestSchema,
                "includeAnalysis",
                "Optional. Set to true to return parsed statement summaries and diagnostic findings in the response. Defaults to false.",
                JsonValue.Create(true));
            DescribeAnalysisFormatProperty(requestSchema);
        }
    }






    private static void DescribeAnalysisFormatProperty(IOpenApiSchema? schema)
    {
        if (schema is not OpenApiSchema requestSchema
            || requestSchema.Properties is null
            || !requestSchema.Properties.TryGetValue("analysisFormat", out var analysisFormatSchema)
            || analysisFormatSchema is not OpenApiSchema analysisFormat)
        {
            return;
        }

        analysisFormat.Description = "Optional. Plan table export format to include in analysis.content when includeAnalysis is true. Supported values: json, md, markdown, csv. Defaults to json.";
        analysisFormat.Example = JsonValue.Create("json");
        analysisFormat.Enum = new List<JsonNode>
        {
            JsonValue.Create("json")!,
            JsonValue.Create("md")!,
            JsonValue.Create("markdown")!,
            JsonValue.Create("csv")!
        };
    }

    private static Task<IResult> GetEstimatedShowplan(
        EstimatedShowplanApiRequest? request,
        EstimatedShowplanApiService apiService,
        CancellationToken cancellationToken) =>
        apiService.GetEstimatedShowplanAsync(request, cancellationToken);
    internal sealed class EstimatedShowplanApiRequest
    {
        public string? ConnectionString { get; init; }

        public string? Query { get; init; }

        public string? Label { get; init; }

        public int? CommandTimeoutSeconds { get; init; }

        public bool IncludeAnalysis { get; init; }

        public string? AnalysisFormat { get; init; }
    }

    internal sealed class EstimatedShowplanApiResponse
    {
        public IReadOnlyList<EstimatedShowplanApiPlan> Plans { get; init; } = Array.Empty<EstimatedShowplanApiPlan>();
    }

    internal sealed class EstimatedShowplanApiPlan
    {
        public required string Label { get; init; }

        public required string ShowplanXml { get; init; }

        public required int StatementCount { get; init; }

        public required string SchemaVersion { get; init; }

        public required int TotalNodeCount { get; init; }

        public required int TotalWarningCount { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public EstimatedShowplanApiAnalysis? Analysis { get; init; }
    }

    internal sealed class EstimatedShowplanApiAnalysis
    {
        public required string Format { get; init; }

        public required string ContentType { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Content { get; init; }

        public IReadOnlyList<EstimatedShowplanApiStatementAnalysis> Statements { get; init; } = Array.Empty<EstimatedShowplanApiStatementAnalysis>();

        public IReadOnlyList<EstimatedShowplanApiDiagnostic> Diagnostics { get; init; } = Array.Empty<EstimatedShowplanApiDiagnostic>();

        public int DiagnosticCount { get; init; }

        public int CriticalDiagnosticCount { get; init; }

        public int WarningDiagnosticCount { get; init; }

        public int InfoDiagnosticCount { get; init; }
    }

    internal sealed class EstimatedShowplanApiStatementAnalysis
    {
        public required string StatementId { get; init; }

        public required string StatementType { get; init; }

        public required string StatementText { get; init; }

        public decimal? EstimatedSubtreeCost { get; init; }

        public double? EstimatedRows { get; init; }

        public int NodeCount { get; init; }

        public int EdgeCount { get; init; }

        public int WarningCount { get; init; }

        public IReadOnlyList<string> RootNodeIds { get; init; } = Array.Empty<string>();
    }

    internal sealed class EstimatedShowplanApiDiagnostic
    {
        public required string RuleId { get; init; }

        public required string RuleName { get; init; }

        public required string Severity { get; init; }

        public required string StatementId { get; init; }

        public string? NodeId { get; init; }

        public required string Message { get; init; }

        public required string Recommendation { get; init; }

        public IReadOnlyList<EstimatedShowplanApiDiagnosticEvidence> Evidence { get; init; } = Array.Empty<EstimatedShowplanApiDiagnosticEvidence>();
    }

    internal sealed class EstimatedShowplanApiDiagnosticEvidence
    {
        public required string Name { get; init; }

        public required string Value { get; init; }
    }
}

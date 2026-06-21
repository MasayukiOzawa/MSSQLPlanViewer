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
                DescribeJsonRequestBody(
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
        if (TryGetJsonRequestBody(operation, out var jsonMediaType))
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
            AddRequiredSchemaProperty(requestSchema, "connectionString");
            AddRequiredSchemaProperty(requestSchema, "query");
            DescribeSchemaProperty(
                requestSchema,
                "connectionString",
                "Required. SQL Server connection string used only by this request. Replace the sample value with your environment before executing.",
                JsonValue.Create("Server=localhost;Database=master;Integrated Security=true;TrustServerCertificate=true;"));
            DescribeSchemaProperty(
                requestSchema,
                "query",
                "Required. T-SQL query for which SQL Server should return estimated execution plans.",
                JsonValue.Create("SELECT TOP (10) name, object_id FROM sys.objects;"));
            DescribeSchemaProperty(
                requestSchema,
                "label",
                "Optional. Label applied to returned plans.",
                JsonValue.Create("Local master sample"));
            DescribeSchemaProperty(
                requestSchema,
                "commandTimeoutSeconds",
                $"Optional. Command timeout in seconds. Supported range: {SqlEstimatedShowplanProvider.MinCommandTimeoutSeconds}-{SqlEstimatedShowplanProvider.MaxCommandTimeoutSeconds}. Defaults to {SqlEstimatedShowplanProvider.DefaultCommandTimeoutSeconds}.",
                JsonValue.Create(30));
            DescribeSchemaProperty(
                requestSchema,
                "includeAnalysis",
                "Optional. Set to true to return parsed statement summaries and diagnostic findings in the response. Defaults to false.",
                JsonValue.Create(true));
            DescribeAnalysisFormatProperty(requestSchema);
        }
    }

    private static void DescribeJsonRequestBody(OpenApiOperation operation, params string[] fieldDescriptions)
    {
        if (operation.RequestBody is not OpenApiRequestBody requestBody)
        {
            return;
        }

        requestBody.Required = true;
        requestBody.Description = "Required request header: Content-Type: application/json.";
        if (fieldDescriptions.Length > 0)
        {
            requestBody.Description += $"{Environment.NewLine}{Environment.NewLine}Body fields:{Environment.NewLine}"
                + string.Join(Environment.NewLine, fieldDescriptions.Select(description => $"- {description}"));
        }
    }

    private static void AddRequiredSchemaProperty(IOpenApiSchema? schema, string propertyName)
    {
        if (schema is not OpenApiSchema requestSchema)
        {
            return;
        }

        requestSchema.Required ??= new HashSet<string>(StringComparer.Ordinal);
        requestSchema.Required.Add(propertyName);
    }

    private static bool TryGetJsonRequestBody(OpenApiOperation operation, out OpenApiMediaType jsonMediaType)
    {
        jsonMediaType = null!;
        if (operation.RequestBody?.Content is null
            || !operation.RequestBody.Content.TryGetValue("application/json", out var candidate))
        {
            return false;
        }

        jsonMediaType = candidate;
        return true;
    }

    private static void DescribeSchemaProperty(
        IOpenApiSchema? schema,
        string propertyName,
        string description,
        JsonNode? example)
    {
        if (schema is not OpenApiSchema requestSchema
            || requestSchema.Properties is null
            || !requestSchema.Properties.TryGetValue(propertyName, out var propertySchema)
            || propertySchema is not OpenApiSchema property)
        {
            return;
        }

        property.Description = description;
        property.Example = example;
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

    private static async Task<IResult> GetEstimatedShowplan(
        EstimatedShowplanApiRequest? request,
        IEstimatedShowplanProvider showplanProvider,
        IShowplanParser parser,
        IPlanDiagnosticsService diagnosticsService,
        IPlanTableProjector tableProjector,
        CancellationToken cancellationToken)
    {
        if (!TryValidateRequest(request, out var commandTimeoutSeconds, out var analysisFormat, out var includeAnalysisContent, out var validationError))
        {
            return validationError!;
        }

        IReadOnlyList<EstimatedShowplanXml> showplans;
        try
        {
            showplans = await showplanProvider.GetEstimatedShowplansAsync(
                new EstimatedShowplanRequest(
                    request!.ConnectionString!,
                    request.Query!,
                    commandTimeoutSeconds),
                cancellationToken);
        }
        catch (EstimatedShowplanException exception)
        {
            return CreateProblem(
                MapStatusCode(exception.Kind),
                MapProblemTitle(exception.Kind),
                exception.Message);
        }

        var plans = new List<EstimatedShowplanApiPlan>();
        foreach (var showplan in showplans)
        {
            ShowplanDocument document;
            try
            {
                document = parser.Parse(showplan.Xml);
            }
            catch (ShowplanParseException exception)
            {
                return CreateProblem(
                    StatusCodes.Status502BadGateway,
                    "Unable to parse returned showplan XML",
                    exception.Message);
            }

            plans.Add(new EstimatedShowplanApiPlan
            {
                Label = BuildPlanLabel(request!.Label, showplan.Ordinal, showplans.Count),
                ShowplanXml = showplan.Xml,
                StatementCount = document.Statements.Count,
                SchemaVersion = document.Metadata.SchemaVersion.ToString(),
                TotalNodeCount = document.TotalNodeCount,
                TotalWarningCount = document.TotalWarningCount,
                Analysis = request!.IncludeAnalysis ? CreateAnalysis(document, diagnosticsService, tableProjector, analysisFormat, includeAnalysisContent) : null
            });
        }

        return Results.Ok(new EstimatedShowplanApiResponse { Plans = plans });
    }

    private static EstimatedShowplanApiAnalysis CreateAnalysis(
        ShowplanDocument document,
        IPlanDiagnosticsService diagnosticsService,
        IPlanTableProjector tableProjector,
        string analysisFormat,
        bool includeContent)
    {
        var diagnostics = diagnosticsService.Analyze(document);
        var statements = document.Statements.Select(CreateStatementAnalysis).ToArray();
        var diagnosticDtos = diagnostics.Select(CreateDiagnostic).ToArray();

        return new EstimatedShowplanApiAnalysis
        {
            Format = analysisFormat,
            ContentType = GetAnalysisContentType(analysisFormat),
            Content = includeContent ? CreateAnalysisContent(analysisFormat, document, tableProjector) : null,
            Statements = statements,
            Diagnostics = diagnosticDtos,
            DiagnosticCount = diagnostics.Count,
            CriticalDiagnosticCount = diagnostics.Count(diagnostic => diagnostic.Severity == PlanDiagnosticSeverity.Critical),
            WarningDiagnosticCount = diagnostics.Count(diagnostic => diagnostic.Severity == PlanDiagnosticSeverity.Warning),
            InfoDiagnosticCount = diagnostics.Count(diagnostic => diagnostic.Severity == PlanDiagnosticSeverity.Info)
        };
    }

    private static EstimatedShowplanApiStatementAnalysis CreateStatementAnalysis(StatementPlan statement) =>
        new()
        {
            StatementId = statement.StatementId,
            StatementType = statement.StatementType,
            StatementText = statement.StatementText,
            EstimatedSubtreeCost = statement.Summary.EstimatedSubtreeCost,
            EstimatedRows = statement.Summary.EstimatedRows,
            NodeCount = statement.Nodes.Count,
            EdgeCount = statement.Edges.Count,
            WarningCount = statement.WarningCount,
            RootNodeIds = statement.RootNodeIds
        };

    private static EstimatedShowplanApiDiagnostic CreateDiagnostic(PlanDiagnostic diagnostic) =>
        new()
        {
            RuleId = diagnostic.RuleId,
            RuleName = diagnostic.RuleName,
            Severity = FormatDiagnosticSeverity(diagnostic.Severity),
            StatementId = diagnostic.StatementId,
            NodeId = diagnostic.NodeId,
            Message = diagnostic.Message,
            Recommendation = diagnostic.Recommendation,
            Evidence = diagnostic.Evidence.Select(CreateDiagnosticEvidence).ToArray()
        };

    private static EstimatedShowplanApiDiagnosticEvidence CreateDiagnosticEvidence(PlanProperty property) =>
        new()
        {
            Name = property.Name,
            Value = property.Value
        };


    private static string GetAnalysisContentType(string analysisFormat) =>
        analysisFormat switch
        {
            "markdown" => "text/markdown",
            "csv" => "text/csv",
            _ => "application/json"
        };

    private static string? CreateAnalysisContent(
        string analysisFormat,
        ShowplanDocument document,
        IPlanTableProjector tableProjector)
    {
        var statement = document.Statements.FirstOrDefault();
        var rows = statement is null
            ? Array.Empty<PlanTableRow>()
            : tableProjector.Project(statement);

        return analysisFormat switch
        {
            "markdown" => PlanTableMarkdownExporter.ToMarkdown(rows),
            "csv" => PlanTableCsvExporter.ToCsv(rows),
            "json" => PlanTableJsonExporter.ToJson(rows),
            _ => null
        };
    }

    private static string FormatDiagnosticSeverity(PlanDiagnosticSeverity severity) =>
        severity switch
        {
            PlanDiagnosticSeverity.Critical => "critical",
            PlanDiagnosticSeverity.Warning => "warning",
            _ => "info"
        };

    private static bool TryValidateRequest(
        EstimatedShowplanApiRequest? request,
        out int commandTimeoutSeconds,
        out string analysisFormat,
        out bool includeAnalysisContent,
        out IResult? error)
    {
        commandTimeoutSeconds = SqlEstimatedShowplanProvider.DefaultCommandTimeoutSeconds;
        analysisFormat = "json";
        includeAnalysisContent = false;
        error = null;

        if (request is null)
        {
            error = CreateProblem(
                StatusCodes.Status400BadRequest,
                "Invalid estimated showplan request",
                "The request body is required.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            error = CreateProblem(
                StatusCodes.Status400BadRequest,
                "Invalid estimated showplan request",
                "The 'connectionString' field is required.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            error = CreateProblem(
                StatusCodes.Status400BadRequest,
                "Invalid estimated showplan request",
                "The 'query' field is required.");
            return false;
        }

        includeAnalysisContent = !string.IsNullOrWhiteSpace(request.AnalysisFormat);

        if (!TryResolveAnalysisFormat(request.AnalysisFormat, out analysisFormat, out var analysisFormatError))
        {
            error = analysisFormatError;
            return false;
        }

        commandTimeoutSeconds = request.CommandTimeoutSeconds
            ?? SqlEstimatedShowplanProvider.DefaultCommandTimeoutSeconds;
        if (commandTimeoutSeconds is < SqlEstimatedShowplanProvider.MinCommandTimeoutSeconds
            or > SqlEstimatedShowplanProvider.MaxCommandTimeoutSeconds)
        {
            error = CreateProblem(
                StatusCodes.Status400BadRequest,
                "Invalid estimated showplan request",
                $"The 'commandTimeoutSeconds' field must be between {SqlEstimatedShowplanProvider.MinCommandTimeoutSeconds} and {SqlEstimatedShowplanProvider.MaxCommandTimeoutSeconds}.");
            return false;
        }

        return true;
    }


    private static bool TryResolveAnalysisFormat(
        string? value,
        out string analysisFormat,
        out IResult? error)
    {
        analysisFormat = "json";
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var normalizedValue = value.Trim().ToLowerInvariant();
        switch (normalizedValue)
        {
            case "json":
            case "csv":
                analysisFormat = normalizedValue;
                return true;
            case "md":
            case "markdown":
                analysisFormat = "markdown";
                return true;
            default:
                error = CreateProblem(
                    StatusCodes.Status400BadRequest,
                    "Invalid estimated showplan request",
                    "The 'analysisFormat' field is invalid. Supported values: json, md, markdown, csv.");
                return false;
        }
    }

    private static string BuildPlanLabel(string? label, int ordinal, int count)
    {
        var baseLabel = string.IsNullOrWhiteSpace(label)
            ? "Estimated"
            : label.Trim();

        return count > 1
            ? $"{baseLabel} #{ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            : baseLabel;
    }

    private static int MapStatusCode(EstimatedShowplanFailureKind kind) =>
        kind switch
        {
            EstimatedShowplanFailureKind.InvalidRequest => StatusCodes.Status400BadRequest,
            EstimatedShowplanFailureKind.Timeout => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status502BadGateway
        };

    private static string MapProblemTitle(EstimatedShowplanFailureKind kind) =>
        kind switch
        {
            EstimatedShowplanFailureKind.InvalidRequest => "Invalid estimated showplan request",
            EstimatedShowplanFailureKind.Timeout => "Estimated showplan request timed out",
            _ => "Unable to retrieve estimated showplan"
        };

    private static IResult CreateProblem(int statusCode, string title, string detail) =>
        TypedResults.Problem(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail
        });

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

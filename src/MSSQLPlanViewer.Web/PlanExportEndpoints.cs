using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using MSSQLPlanViewer.Core.Models;
using MSSQLPlanViewer.Core.Formatting;
using MSSQLPlanViewer.Core.Parsing;
using MSSQLPlanViewer.Core.Rendering;

namespace MSSQLPlanViewer.Web;

internal static class PlanExportEndpoints
{
    public static IEndpointRouteBuilder MapPlanExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/exports")
            .DisableAntiforgery()
            .WithTags("Exports");

        group.MapPost("/table", ExportTable)
            .WithName("ExportPlanTable")
            .WithSummary("Export an execution plan table")
            .WithDescription("Parses SQL Server Showplan XML and exports the selected statement as CSV, Markdown, or JSON. Send Content-Type: application/json. The format query parameter is required and supports csv, md, markdown, and json. The request body requires showplanXml and can include statementId.")
            .Accepts<TableExportRequest>("application/json")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .Produces(StatusCodes.Status200OK, contentType: "text/markdown")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddOpenApiOperationTransformer((operation, context, _) =>
            {
                DescribeFormatParameter(
                    operation,
                    "Export format. Supported values: csv, md, markdown, json.",
                    "csv",
                    "md",
                    "markdown",
                    "json");
                DescribeJsonRequestBody(
                    operation,
                    "showplanXml (required): SQL Server Showplan XML document to parse and export.",
                    "statementId (optional): StatementId to export. When omitted, the first statement in the Showplan XML is used.");
                DescribeExportRequestSchema(context.Document, nameof(TableExportRequest));
                AddSampleRequestBodyExample(
                    operation,
                    "sample-plan",
                    "Sample Showplan XML",
                    "A small SELECT 1 execution plan that can be used with Try it out.",
                    CreateSampleExportRequestBody(includeGraphOptions: false));
                return Task.CompletedTask;
            });

        group.MapPost("/graph", ExportGraph)
            .WithName("ExportPlanGraph")
            .WithSummary("Export an execution plan graph")
            .WithDescription("Parses SQL Server Showplan XML and exports the selected statement graph as SVG or PNG. Send Content-Type: application/json. The format query parameter is required and supports svg and png. The optional layoutDirection query parameter supports vertical and horizontal and overrides the request body layoutDirection.")
            .Accepts<GraphExportRequest>("application/json")
            .Produces(StatusCodes.Status200OK, contentType: "image/svg+xml")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .AddOpenApiOperationTransformer((operation, context, _) =>
            {
                DescribeFormatParameter(
                    operation,
                    "Graph export format. Supported values: svg, png.",
                    "svg",
                    "png");
                DescribeLayoutDirectionParameter(operation);
                DescribeJsonRequestBody(
                    operation,
                    "showplanXml (required): SQL Server Showplan XML document to parse and export.",
                    "statementId (optional): StatementId to export. When omitted, the first statement in the Showplan XML is used.",
                    "costHighlightThresholdPercent (optional): Operator cost highlight threshold percentage. Values are clamped to 0-100 during rendering. Defaults to 20.",
                    "showCriticalPath (optional): Set to true to highlight critical path nodes and edges in the graph. Defaults to true.",
                    "layoutDirection (optional): Graph layout direction. Supported values: vertical, horizontal. Defaults to vertical when omitted.");
                DescribeGraphRequestBody(operation, context.Document);
                return Task.CompletedTask;
            });

        return endpoints;
    }

    private const string SampleShowplanXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ShowPlanXML xmlns=""http://schemas.microsoft.com/sqlserver/2012/01/showplan"" Version=""1.0"" Build=""11.0.7001.0"">
  <BatchSequence>
    <Batch>
      <Statements>
        <StmtSimple StatementId=""1"" StatementText=""SELECT 1"" StatementSubTreeCost=""0.003"" StatementEstRows=""1"">
          <QueryPlan CachedPlanSize=""16"" CompileTime=""1"" CompileCPU=""1"" CompileMemory=""64"">
            <RelOp NodeId=""0"" PhysicalOp=""Compute Scalar"" LogicalOp=""Compute Scalar"" EstimateRows=""1"" EstimateCPU=""0"" EstimateIO=""0"" AvgRowSize=""9"" EstimatedTotalSubtreeCost=""0.003"">
              <OutputList>
                <ColumnReference Column=""Expr1000"" />
              </OutputList>
              <ComputeScalar>
                <DefinedValues>
                  <DefinedValue>
                    <ColumnReference Column=""Expr1000"" />
                  </DefinedValue>
                </DefinedValues>
                <RelOp NodeId=""1"" PhysicalOp=""Constant Scan"" LogicalOp=""Constant Scan"" EstimateRows=""1"" EstimateCPU=""0"" EstimateIO=""0"" AvgRowSize=""9"" EstimatedTotalSubtreeCost=""0.002"">
                  <ConstantScan />
                </RelOp>
              </ComputeScalar>
            </RelOp>
          </QueryPlan>
        </StmtSimple>
      </Statements>
    </Batch>
  </BatchSequence>
</ShowPlanXML>";

    private static IResult ExportTable(
        [FromQuery] string? format,
        TableExportRequest? request,
        IShowplanParser parser,
        IPlanTableProjector projector)
    {
        if (!TryResolveTableFormat(format, out var resolvedFormat, out var formatError))
        {
            return formatError!;
        }

        var resolved = TryResolveStatement(parser, request?.ShowplanXml, request?.StatementId, out var error);
        if (error is not null)
        {
            return error;
        }

        var rows = projector.Project(resolved!.Statement);
        return resolvedFormat switch
        {
            "csv" => CreateTextFileResult(
                PlanTableCsvExporter.ToCsv(rows),
                "text/csv",
                PlanFileNameBuilder.BuildFileName("plan-table", resolved.Statement.StatementId, "csv", "plan-table")),
            "md" => CreateTextFileResult(
                PlanTableMarkdownExporter.ToMarkdown(rows),
                "text/markdown",
                PlanFileNameBuilder.BuildFileName("plan-table", resolved.Statement.StatementId, "md", "plan-table")),
            "json" => CreateTextFileResult(
                PlanTableJsonExporter.ToJson(rows),
                "application/json",
                PlanFileNameBuilder.BuildFileName("plan-table", resolved.Statement.StatementId, "json", "plan-table")),
            _ => throw new InvalidOperationException($"Unsupported table export format '{resolvedFormat}'.")
        };
    }

    private static IResult ExportGraph(
        [FromQuery] string? format,
        [FromQuery(Name = "layoutDirection")] string? queryLayoutDirection,
        GraphExportRequest? request,
        IShowplanParser parser,
        IPlanGraphLayoutService layoutService,
        IPlanGraphSvgRenderer svgRenderer,
        IPlanGraphPngExporter pngExporter)
    {
        if (!TryResolveGraphFormat(format, out var resolvedFormat, out var formatError))
        {
            return formatError!;
        }

        var requestedLayoutDirection = string.IsNullOrWhiteSpace(queryLayoutDirection)
            ? request?.LayoutDirection
            : queryLayoutDirection;
        if (!TryResolveGraphLayoutDirection(requestedLayoutDirection, out var graphLayoutDirection, out var layoutDirectionError))
        {
            return layoutDirectionError!;
        }

        var resolved = TryResolveStatement(parser, request?.ShowplanXml, request?.StatementId, out var error);
        if (error is not null)
        {
            return error;
        }

        var layout = layoutService.CreateLayout(
            resolved!.Statement,
            CalculateStatementCostRatio(resolved.Document, resolved.Statement),
            graphLayoutDirection);
        var svg = svgRenderer.Render(
            layout,
            new GraphRenderOptions(request?.CostHighlightThresholdPercent ?? 20, request?.ShowCriticalPath ?? true));
        return resolvedFormat switch
        {
            "svg" => CreateTextFileResult(
                svg,
                "image/svg+xml",
                PlanFileNameBuilder.BuildFileName("plan-graph", resolved.Statement.StatementId, "svg", "plan-graph")),
            "png" => Results.File(
                pngExporter.Export(svg, (int)Math.Ceiling(layout.Width), (int)Math.Ceiling(layout.Height)),
                "image/png",
                PlanFileNameBuilder.BuildFileName("plan-graph", resolved.Statement.StatementId, "png", "plan-graph")),
            _ => throw new InvalidOperationException($"Unsupported graph export format '{resolvedFormat}'.")
        };
    }

    private static JsonObject CreateSampleExportRequestBody(bool includeGraphOptions, string layoutDirection = "vertical")
    {
        var requestBody = new JsonObject
        {
            ["showplanXml"] = JsonValue.Create(SampleShowplanXml),
            ["statementId"] = JsonValue.Create("1")
        };

        if (includeGraphOptions)
        {
            requestBody["costHighlightThresholdPercent"] = JsonValue.Create(20);
            requestBody["showCriticalPath"] = JsonValue.Create(true);
            requestBody["layoutDirection"] = JsonValue.Create(layoutDirection);
        }

        return requestBody;
    }

    private static void DescribeGraphRequestBody(OpenApiOperation operation, OpenApiDocument? document)
    {
        if (!TryGetJsonRequestBody(operation, out var jsonMediaType))
        {
            return;
        }

        if (document?.Components?.Schemas?.TryGetValue(nameof(GraphExportRequest), out var graphRequestSchema) == true)
        {
            DescribeExportRequestSchema(graphRequestSchema);
            DescribeSchemaProperty(
                graphRequestSchema,
                "costHighlightThresholdPercent",
                "Optional. Operator cost highlight threshold percentage. Values are clamped to 0-100 during rendering. Defaults to 20.",
                JsonValue.Create(20));
            DescribeSchemaProperty(
                graphRequestSchema,
                "showCriticalPath",
                "Optional. Set to true to highlight critical path nodes and edges in the graph. Defaults to true.",
                JsonValue.Create(true));
            DescribeLayoutDirectionProperty(graphRequestSchema);
        }

        AddSampleRequestBodyExample(
            jsonMediaType,
            "sample-plan-vertical",
            "Sample Showplan XML - vertical",
            "A small SELECT 1 execution plan exported with a top-to-bottom graph layout.",
            CreateSampleExportRequestBody(includeGraphOptions: true, layoutDirection: "vertical"));
        AddSampleRequestBodyExample(
            jsonMediaType,
            "sample-plan-horizontal",
            "Sample Showplan XML - horizontal",
            "A small SELECT 1 execution plan exported with a left-to-right graph layout.",
            CreateSampleExportRequestBody(includeGraphOptions: true, layoutDirection: "horizontal"));
    }

    private static void AddSampleRequestBodyExample(
        OpenApiOperation operation,
        string name,
        string summary,
        string description,
        JsonObject requestBody)
    {
        if (!TryGetJsonRequestBody(operation, out var jsonMediaType))
        {
            return;
        }

        AddSampleRequestBodyExample(jsonMediaType, name, summary, description, requestBody);
    }

    private static void AddSampleRequestBodyExample(
        OpenApiMediaType jsonMediaType,
        string name,
        string summary,
        string description,
        JsonObject requestBody)
    {
        jsonMediaType.Example ??= requestBody.DeepClone();
        jsonMediaType.Examples ??= new Dictionary<string, IOpenApiExample>(StringComparer.Ordinal);
        jsonMediaType.Examples[name] = new OpenApiExample
        {
            Summary = summary,
            Description = description,
            Value = requestBody
        };
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

    private static void DescribeExportRequestSchema(OpenApiDocument? document, string schemaName)
    {
        if (document?.Components?.Schemas?.TryGetValue(schemaName, out var schema) == true)
        {
            DescribeExportRequestSchema(schema);
        }
    }

    private static void DescribeExportRequestSchema(IOpenApiSchema? schema)
    {
        AddRequiredSchemaProperty(schema, "showplanXml");
        DescribeSchemaProperty(
            schema,
            "showplanXml",
            "Required. SQL Server Showplan XML document to parse and export.",
            JsonValue.Create(SampleShowplanXml));
        DescribeSchemaProperty(
            schema,
            "statementId",
            "Optional. StatementId to export. When omitted, the first statement in the Showplan XML is used.",
            JsonValue.Create("1"));
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

    private static void DescribeLayoutDirectionProperty(IOpenApiSchema? schema)
    {
        if (schema is not OpenApiSchema requestSchema
            || requestSchema.Properties is null
            || !requestSchema.Properties.TryGetValue("layoutDirection", out var layoutDirectionSchema)
            || layoutDirectionSchema is not OpenApiSchema layoutDirection)
        {
            return;
        }

        layoutDirection.Description = "Graph layout direction. Supported values: vertical, horizontal. Defaults to vertical when omitted.";
        layoutDirection.Example = JsonValue.Create("vertical");
        layoutDirection.Enum = new List<JsonNode>
        {
            JsonValue.Create("vertical")!,
            JsonValue.Create("horizontal")!
        };
    }

    private static void DescribeLayoutDirectionParameter(OpenApiOperation operation)
    {
        var layoutDirectionParameter = operation.Parameters?.FirstOrDefault(parameter =>
            string.Equals(parameter.Name, "layoutDirection", StringComparison.OrdinalIgnoreCase));
        if (layoutDirectionParameter is not OpenApiParameter parameter)
        {
            return;
        }

        parameter.Required = false;
        parameter.Description = "Graph layout direction. Supported values: vertical, horizontal. Defaults to vertical when omitted. Overrides the request body layoutDirection when both are provided.";
        parameter.Example = JsonValue.Create("vertical");
        parameter.Schema = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Enum = new List<JsonNode>
            {
                JsonValue.Create("vertical")!,
                JsonValue.Create("horizontal")!
            }
        };
    }

    private static void DescribeFormatParameter(
        OpenApiOperation operation,
        string description,
        params string[] supportedValues)
    {
        var formatParameter = operation.Parameters?.FirstOrDefault(parameter =>
            string.Equals(parameter.Name, "format", StringComparison.OrdinalIgnoreCase));
        if (formatParameter is not OpenApiParameter parameter)
        {
            return;
        }

        parameter.Required = true;
        parameter.Description = description;
        parameter.Example = JsonValue.Create(supportedValues[0]);
        parameter.Schema = new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Enum = supportedValues
                .Select(value => JsonValue.Create(value)!)
                .ToList<JsonNode>()
        };
    }

    private static ResolvedStatement? TryResolveStatement(
        IShowplanParser parser,
        string? showplanXml,
        string? statementId,
        out IResult? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(showplanXml))
        {
            error = CreateProblem(
                StatusCodes.Status400BadRequest,
                "Invalid export request",
                "The 'showplanXml' field is required.");
            return null;
        }

        ShowplanDocument document;
        try
        {
            document = parser.Parse(showplanXml);
        }
        catch (ShowplanParseException exception)
        {
            error = CreateProblem(
                StatusCodes.Status400BadRequest,
                "Unable to parse showplan XML",
                exception.Message);
            return null;
        }

        var statement = string.IsNullOrWhiteSpace(statementId)
            ? document.Statements.FirstOrDefault()
            : document.Statements.FirstOrDefault(candidate => string.Equals(candidate.StatementId, statementId, StringComparison.Ordinal));

        if (statement is null)
        {
            error = string.IsNullOrWhiteSpace(statementId)
                ? CreateProblem(
                    StatusCodes.Status404NotFound,
                    "Statement not found",
                    "The showplan XML did not contain any statements.")
                : CreateProblem(
                    StatusCodes.Status404NotFound,
                    "Statement not found",
                    $"The statement '{statementId}' was not found.");
            return null;
        }

        return new ResolvedStatement(document, statement);
    }

    private static IResult CreateTextFileResult(string content, string contentType, string fileName) =>
        Results.File(Encoding.UTF8.GetBytes(content), contentType, fileName);

    private static IResult CreateProblem(int statusCode, string title, string detail) =>
        TypedResults.Problem(new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail
        });

    private static decimal? CalculateStatementCostRatio(ShowplanDocument document, StatementPlan statement)
    {
        var totalCost = document.Statements.Sum(item => item.Summary.EstimatedSubtreeCost ?? 0);
        if (totalCost <= 0)
        {
            return null;
        }

        return (statement.Summary.EstimatedSubtreeCost ?? 0) / totalCost;
    }

    private static bool TryResolveTableFormat(string? format, out string resolvedFormat, out IResult? error) =>
        TryResolveFormat(
            format,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["csv"] = "csv",
                ["md"] = "md",
                ["markdown"] = "md",
                ["json"] = "json"
            },
            "csv, md, markdown, json",
            out resolvedFormat,
            out error);

    private static bool TryResolveGraphFormat(string? format, out string resolvedFormat, out IResult? error) =>
        TryResolveFormat(
            format,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["svg"] = "svg",
                ["png"] = "png"
            },
            "svg, png",
            out resolvedFormat,
            out error);

    private static bool TryResolveGraphLayoutDirection(
        string? value,
        out GraphLayoutDirection direction,
        out IResult? error)
    {
        direction = GraphLayoutDirection.Vertical;
        error = null;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "vertical":
                direction = GraphLayoutDirection.Vertical;
                return true;
            case "horizontal":
                direction = GraphLayoutDirection.HorizontalSsms;
                return true;
            default:
                error = CreateProblem(
                    StatusCodes.Status400BadRequest,
                    "Invalid export request",
                    "The 'layoutDirection' field is invalid. Supported values: vertical, horizontal.");
                return false;
        }
    }

    private static bool TryResolveFormat(
        string? format,
        IReadOnlyDictionary<string, string> supportedFormats,
        string supportedValues,
        out string resolvedFormat,
        out IResult? error)
    {
        resolvedFormat = string.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(format))
        {
            error = CreateProblem(
                StatusCodes.Status400BadRequest,
                "Invalid export request",
                "The 'format' query parameter is required.");
            return false;
        }

        if (!supportedFormats.TryGetValue(format.Trim(), out var candidateFormat) || string.IsNullOrWhiteSpace(candidateFormat))
        {
            error = CreateProblem(
                StatusCodes.Status400BadRequest,
                "Invalid export request",
                $"The 'format' query parameter is invalid. Supported values: {supportedValues}.");
            return false;
        }

        resolvedFormat = candidateFormat;
        return true;
    }

    private sealed record ResolvedStatement(ShowplanDocument Document, StatementPlan Statement);

    internal class TableExportRequest
    {
        public string? ShowplanXml { get; init; }

        public string? StatementId { get; init; }
    }

    internal sealed class GraphExportRequest : TableExportRequest
    {
        public int CostHighlightThresholdPercent { get; init; } = 20;

        public bool ShowCriticalPath { get; init; } = true;

        public string? LayoutDirection { get; init; }
    }
}

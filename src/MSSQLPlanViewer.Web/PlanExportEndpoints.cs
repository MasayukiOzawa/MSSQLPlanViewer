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
                OpenApiDocumentationHelpers.DescribeJsonRequestBody(
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
                OpenApiDocumentationHelpers.DescribeJsonRequestBody(
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
        PlanExportService exportService) =>
        exportService.ExportTable(format, request);

    private static IResult ExportGraph(
        [FromQuery] string? format,
        [FromQuery(Name = "layoutDirection")] string? queryLayoutDirection,
        GraphExportRequest? request,
        PlanExportService exportService) =>
        exportService.ExportGraph(format, queryLayoutDirection, request);
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
        if (!OpenApiDocumentationHelpers.TryGetJsonRequestBody(operation, out var jsonMediaType))
        {
            return;
        }

        if (document?.Components?.Schemas?.TryGetValue(nameof(GraphExportRequest), out var graphRequestSchema) == true)
        {
            DescribeExportRequestSchema(graphRequestSchema);
            OpenApiDocumentationHelpers.DescribeSchemaProperty(
                graphRequestSchema,
                "costHighlightThresholdPercent",
                "Optional. Operator cost highlight threshold percentage. Values are clamped to 0-100 during rendering. Defaults to 20.",
                JsonValue.Create(20));
            OpenApiDocumentationHelpers.DescribeSchemaProperty(
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
        if (!OpenApiDocumentationHelpers.TryGetJsonRequestBody(operation, out var jsonMediaType))
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



    private static void DescribeExportRequestSchema(OpenApiDocument? document, string schemaName)
    {
        if (document?.Components?.Schemas?.TryGetValue(schemaName, out var schema) == true)
        {
            DescribeExportRequestSchema(schema);
        }
    }

    private static void DescribeExportRequestSchema(IOpenApiSchema? schema)
    {
        OpenApiDocumentationHelpers.AddRequiredSchemaProperty(schema, "showplanXml");
        OpenApiDocumentationHelpers.DescribeSchemaProperty(
            schema,
            "showplanXml",
            "Required. SQL Server Showplan XML document to parse and export.",
            JsonValue.Create(SampleShowplanXml));
        OpenApiDocumentationHelpers.DescribeSchemaProperty(
            schema,
            "statementId",
            "Optional. StatementId to export. When omitted, the first statement in the Showplan XML is used.",
            JsonValue.Create("1"));
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

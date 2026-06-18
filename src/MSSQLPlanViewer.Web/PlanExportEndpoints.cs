using System.Text;
using Microsoft.AspNetCore.Mvc;
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
            .DisableAntiforgery();

        group.MapPost("/table", ExportTable)
            .Accepts<TableExportRequest>("application/json")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .Produces(StatusCodes.Status200OK, contentType: "text/markdown")
            .Produces(StatusCodes.Status200OK, contentType: "application/json")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/graph", ExportGraph)
            .Accepts<GraphExportRequest>("application/json")
            .Produces(StatusCodes.Status200OK, contentType: "image/svg+xml")
            .Produces(StatusCodes.Status200OK, contentType: "image/png")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return endpoints;
    }

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

        var resolved = TryResolveStatement(parser, request?.ShowplanXml, request?.StatementId, out var error);
        if (error is not null)
        {
            return error;
        }

        var layout = layoutService.CreateLayout(resolved!.Statement);
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
    }
}

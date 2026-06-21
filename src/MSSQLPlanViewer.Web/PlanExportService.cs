using System.Text;
using Microsoft.AspNetCore.Mvc;
using MSSQLPlanViewer.Core.Formatting;
using MSSQLPlanViewer.Core.Models;
using MSSQLPlanViewer.Core.Parsing;
using MSSQLPlanViewer.Core.Rendering;

namespace MSSQLPlanViewer.Web;

internal sealed class PlanExportService(
    IShowplanParser parser,
    IPlanTableProjector tableProjector,
    IPlanGraphLayoutService graphLayoutService,
    IPlanGraphSvgRenderer graphSvgRenderer,
    IPlanGraphPngExporter graphPngExporter)
{
    public IResult ExportTable(string? format, PlanExportEndpoints.TableExportRequest? request)
    {
        if (!TryResolveTableFormat(format, out var resolvedFormat, out var formatError))
        {
            return formatError!;
        }

        var resolved = TryResolveStatement(request?.ShowplanXml, request?.StatementId, out var error);
        if (error is not null)
        {
            return error;
        }

        var rows = tableProjector.Project(resolved!.Statement);
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

    public IResult ExportGraph(
        string? format,
        string? queryLayoutDirection,
        PlanExportEndpoints.GraphExportRequest? request)
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

        var resolved = TryResolveStatement(request?.ShowplanXml, request?.StatementId, out var error);
        if (error is not null)
        {
            return error;
        }

        var layout = graphLayoutService.CreateLayout(
            resolved!.Statement,
            CalculateStatementCostRatio(resolved.Document, resolved.Statement),
            graphLayoutDirection);
        var svg = graphSvgRenderer.Render(
            layout,
            new GraphRenderOptions(request?.CostHighlightThresholdPercent ?? 20, request?.ShowCriticalPath ?? true));
        return resolvedFormat switch
        {
            "svg" => CreateTextFileResult(
                svg,
                "image/svg+xml",
                PlanFileNameBuilder.BuildFileName("plan-graph", resolved.Statement.StatementId, "svg", "plan-graph")),
            "png" => Results.File(
                graphPngExporter.Export(svg, (int)Math.Ceiling(layout.Width), (int)Math.Ceiling(layout.Height)),
                "image/png",
                PlanFileNameBuilder.BuildFileName("plan-graph", resolved.Statement.StatementId, "png", "plan-graph")),
            _ => throw new InvalidOperationException($"Unsupported graph export format '{resolvedFormat}'.")
        };
    }

    private ResolvedStatement? TryResolveStatement(
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
}

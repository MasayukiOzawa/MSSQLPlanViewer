using Microsoft.AspNetCore.Mvc;
using MSSQLPlanViewer.Core.Diagnostics;
using MSSQLPlanViewer.Core.Models;
using MSSQLPlanViewer.Core.Parsing;
using MSSQLPlanViewer.Core.Rendering;
using MSSQLPlanViewer.Web.Showplans;
using ApiAnalysis = MSSQLPlanViewer.Web.EstimatedShowplanEndpoints.EstimatedShowplanApiAnalysis;
using ApiDiagnostic = MSSQLPlanViewer.Web.EstimatedShowplanEndpoints.EstimatedShowplanApiDiagnostic;
using ApiDiagnosticEvidence = MSSQLPlanViewer.Web.EstimatedShowplanEndpoints.EstimatedShowplanApiDiagnosticEvidence;
using ApiPlan = MSSQLPlanViewer.Web.EstimatedShowplanEndpoints.EstimatedShowplanApiPlan;
using ApiRequest = MSSQLPlanViewer.Web.EstimatedShowplanEndpoints.EstimatedShowplanApiRequest;
using ApiResponse = MSSQLPlanViewer.Web.EstimatedShowplanEndpoints.EstimatedShowplanApiResponse;
using ApiStatementAnalysis = MSSQLPlanViewer.Web.EstimatedShowplanEndpoints.EstimatedShowplanApiStatementAnalysis;

namespace MSSQLPlanViewer.Web;

internal sealed class EstimatedShowplanApiService(
    IEstimatedShowplanProvider showplanProvider,
    IShowplanParser parser,
    IPlanDiagnosticsService diagnosticsService,
    IPlanTableProjector tableProjector)
{
    public async Task<IResult> GetEstimatedShowplanAsync(ApiRequest? request, CancellationToken cancellationToken)
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

        var plans = new List<ApiPlan>();
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

            plans.Add(new ApiPlan
            {
                Label = BuildPlanLabel(request!.Label, showplan.Ordinal, showplans.Count),
                ShowplanXml = showplan.Xml,
                StatementCount = document.Statements.Count,
                SchemaVersion = document.Metadata.SchemaVersion.ToString(),
                TotalNodeCount = document.TotalNodeCount,
                TotalWarningCount = document.TotalWarningCount,
                Analysis = request!.IncludeAnalysis ? CreateAnalysis(document, analysisFormat, includeAnalysisContent) : null
            });
        }

        return Results.Ok(new ApiResponse { Plans = plans });
    }

    private ApiAnalysis CreateAnalysis(
        ShowplanDocument document,
        string analysisFormat,
        bool includeContent)
    {
        var diagnostics = diagnosticsService.Analyze(document);
        var statements = document.Statements.Select(CreateStatementAnalysis).ToArray();
        var diagnosticDtos = diagnostics.Select(CreateDiagnostic).ToArray();

        return new ApiAnalysis
        {
            Format = analysisFormat,
            ContentType = GetAnalysisContentType(analysisFormat),
            Content = includeContent ? CreateAnalysisContent(analysisFormat, document) : null,
            Statements = statements,
            Diagnostics = diagnosticDtos,
            DiagnosticCount = diagnostics.Count,
            CriticalDiagnosticCount = diagnostics.Count(diagnostic => diagnostic.Severity == PlanDiagnosticSeverity.Critical),
            WarningDiagnosticCount = diagnostics.Count(diagnostic => diagnostic.Severity == PlanDiagnosticSeverity.Warning),
            InfoDiagnosticCount = diagnostics.Count(diagnostic => diagnostic.Severity == PlanDiagnosticSeverity.Info)
        };
    }

    private static ApiStatementAnalysis CreateStatementAnalysis(StatementPlan statement) =>
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

    private static ApiDiagnostic CreateDiagnostic(PlanDiagnostic diagnostic) =>
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

    private static ApiDiagnosticEvidence CreateDiagnosticEvidence(PlanProperty property) =>
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

    private string? CreateAnalysisContent(string analysisFormat, ShowplanDocument document)
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
        ApiRequest? request,
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
}

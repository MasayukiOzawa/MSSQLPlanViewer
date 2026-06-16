using Microsoft.AspNetCore.Mvc;
using MSSQLPlanViewer.Core.Models;
using MSSQLPlanViewer.Core.Parsing;
using MSSQLPlanViewer.Web.Showplans;

namespace MSSQLPlanViewer.Web;

internal static class EstimatedShowplanEndpoints
{
    public static IEndpointRouteBuilder MapEstimatedShowplanEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/showplans")
            .DisableAntiforgery();

        group.MapPost("/estimated", GetEstimatedShowplan)
            .Accepts<EstimatedShowplanApiRequest>("application/json")
            .Produces<EstimatedShowplanApiResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return endpoints;
    }

    private static async Task<IResult> GetEstimatedShowplan(
        EstimatedShowplanApiRequest? request,
        IEstimatedShowplanProvider showplanProvider,
        IShowplanParser parser,
        CancellationToken cancellationToken)
    {
        if (!TryValidateRequest(request, out var commandTimeoutSeconds, out var validationError))
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
                TotalWarningCount = document.TotalWarningCount
            });
        }

        return Results.Ok(new EstimatedShowplanApiResponse { Plans = plans });
    }

    private static bool TryValidateRequest(
        EstimatedShowplanApiRequest? request,
        out int commandTimeoutSeconds,
        out IResult? error)
    {
        commandTimeoutSeconds = SqlEstimatedShowplanProvider.DefaultCommandTimeoutSeconds;
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
    }
}

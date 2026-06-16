using System.Data;
using System.Data.SqlTypes;
using Microsoft.Data.SqlClient;

namespace MSSQLPlanViewer.Web.Showplans;

public sealed class SqlEstimatedShowplanProvider : IEstimatedShowplanProvider
{
    public const int DefaultCommandTimeoutSeconds = 60;

    public const int MinCommandTimeoutSeconds = 1;

    public const int MaxCommandTimeoutSeconds = 300;

    private readonly ILogger<SqlEstimatedShowplanProvider> _logger;

    public SqlEstimatedShowplanProvider(ILogger<SqlEstimatedShowplanProvider> logger)
    {
        _logger = logger;
    }

    public async Task<IReadOnlyList<EstimatedShowplanXml>> GetEstimatedShowplansAsync(
        EstimatedShowplanRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        await using var connection = new SqlConnection(request.ConnectionString);
        var showplanEnabled = false;

        try
        {
            await connection.OpenAsync(cancellationToken);
            await SetShowplanXmlAsync(connection, enabled: true, request.CommandTimeoutSeconds, cancellationToken);
            showplanEnabled = true;

            var showplans = await ExecuteShowplanQueryAsync(
                connection,
                request.Query,
                request.CommandTimeoutSeconds,
                cancellationToken);

            if (showplans.Count == 0)
            {
                throw new EstimatedShowplanException(
                    EstimatedShowplanFailureKind.NoShowplanReturned,
                    "SQL Server did not return Showplan XML for the query.");
            }

            return showplans;
        }
        catch (SqlException exception) when (IsSqlTimeout(exception))
        {
            throw new EstimatedShowplanException(
                EstimatedShowplanFailureKind.Timeout,
                "Timed out while retrieving the estimated execution plan.",
                exception);
        }
        catch (SqlException exception)
        {
            throw new EstimatedShowplanException(
                EstimatedShowplanFailureKind.SqlExecution,
                $"SQL Server returned error {exception.Number}: {exception.Message}",
                exception);
        }
        finally
        {
            if (showplanEnabled)
            {
                await TryDisableShowplanXmlAsync(connection, request.CommandTimeoutSeconds);
            }
        }
    }

    private static void ValidateRequest(EstimatedShowplanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ConnectionString))
        {
            throw new EstimatedShowplanException(
                EstimatedShowplanFailureKind.InvalidRequest,
                "The connection string is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Query))
        {
            throw new EstimatedShowplanException(
                EstimatedShowplanFailureKind.InvalidRequest,
                "The query is required.");
        }

        if (request.CommandTimeoutSeconds is < MinCommandTimeoutSeconds or > MaxCommandTimeoutSeconds)
        {
            throw new EstimatedShowplanException(
                EstimatedShowplanFailureKind.InvalidRequest,
                $"The command timeout must be between {MinCommandTimeoutSeconds} and {MaxCommandTimeoutSeconds} seconds.");
        }
    }

    private static async Task SetShowplanXmlAsync(
        SqlConnection connection,
        bool enabled,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(
            enabled ? "SET SHOWPLAN_XML ON" : "SET SHOWPLAN_XML OFF",
            connection,
            commandTimeoutSeconds);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task TryDisableShowplanXmlAsync(
        SqlConnection connection,
        int commandTimeoutSeconds)
    {
        try
        {
            await SetShowplanXmlAsync(connection, enabled: false, commandTimeoutSeconds, CancellationToken.None);
        }
        catch (Exception exception)
        {
            SqlConnection.ClearPool(connection);
            _logger.LogWarning(exception, "Failed to turn SHOWPLAN_XML off after estimated plan retrieval. The connection pool was cleared.");
        }
    }

    private static async Task<IReadOnlyList<EstimatedShowplanXml>> ExecuteShowplanQueryAsync(
        SqlConnection connection,
        string query,
        int commandTimeoutSeconds,
        CancellationToken cancellationToken)
    {
        var showplans = new List<EstimatedShowplanXml>();
        await using var command = CreateCommand(query, connection, commandTimeoutSeconds);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, cancellationToken);

        do
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var xml = ReadShowplanXml(reader);
                if (!string.IsNullOrWhiteSpace(xml))
                {
                    showplans.Add(new EstimatedShowplanXml(showplans.Count + 1, xml));
                }
            }
        }
        while (await reader.NextResultAsync(cancellationToken));

        return showplans;
    }

    private static SqlCommand CreateCommand(string commandText, SqlConnection connection, int commandTimeoutSeconds) =>
        new(commandText, connection)
        {
            CommandType = CommandType.Text,
            CommandTimeout = commandTimeoutSeconds
        };

    private static string? ReadShowplanXml(SqlDataReader reader)
    {
        if (reader.FieldCount == 0 || reader.IsDBNull(0))
        {
            return null;
        }

        if (reader.GetFieldType(0) == typeof(SqlXml))
        {
            var xml = reader.GetSqlXml(0);
            if (xml.IsNull)
            {
                return null;
            }

            using var xmlReader = xml.CreateReader();
            xmlReader.MoveToContent();
            return xmlReader.ReadOuterXml();
        }

        return reader.GetValue(0)?.ToString();
    }

    private static bool IsSqlTimeout(SqlException exception) =>
        exception.Errors
            .Cast<SqlError>()
            .Any(error => error.Number == -2);
}

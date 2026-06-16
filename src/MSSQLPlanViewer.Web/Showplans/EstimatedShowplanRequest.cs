namespace MSSQLPlanViewer.Web.Showplans;

public sealed record EstimatedShowplanRequest(
    string ConnectionString,
    string Query,
    int CommandTimeoutSeconds);


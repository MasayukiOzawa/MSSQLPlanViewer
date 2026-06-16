namespace MSSQLPlanViewer.Web.Showplans;

public enum EstimatedShowplanFailureKind
{
    InvalidRequest,
    SqlExecution,
    Timeout,
    NoShowplanReturned
}

public sealed class EstimatedShowplanException : Exception
{
    public EstimatedShowplanException(EstimatedShowplanFailureKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    public EstimatedShowplanException(EstimatedShowplanFailureKind kind, string message, Exception innerException)
        : base(message, innerException)
    {
        Kind = kind;
    }

    public EstimatedShowplanFailureKind Kind { get; }
}


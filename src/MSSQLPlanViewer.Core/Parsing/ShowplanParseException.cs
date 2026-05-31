namespace MSSQLPlanViewer.Core.Parsing;

public sealed class ShowplanParseException : Exception
{
    public ShowplanParseException(string message)
        : base(message)
    {
    }

    public ShowplanParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

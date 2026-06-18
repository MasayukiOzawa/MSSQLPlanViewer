namespace MSSQLPlanViewer.Core.Rendering;

public sealed record StatementGraphNodeLayout(
    string StatementId,
    string StatementType,
    string StatementText,
    string PrimaryLabel,
    string SecondaryLabel,
    double X,
    double Y,
    double Width,
    double Height,
    decimal CostRatio);

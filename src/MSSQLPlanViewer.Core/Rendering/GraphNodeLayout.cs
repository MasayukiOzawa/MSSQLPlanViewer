namespace MSSQLPlanViewer.Core.Rendering;

public sealed record GraphNodeLayout(
    string NodeId,
    string PhysicalOp,
    string LogicalOp,
    string ObjectName,
    string PrimaryLabel,
    string SecondaryLabel,
    double X,
    double Y,
    double Width,
    double Height,
    decimal CostRatio,
    double? EstimatedRows,
    double? ActualRows,
    bool HasWarnings,
    bool IsOnCriticalPath);

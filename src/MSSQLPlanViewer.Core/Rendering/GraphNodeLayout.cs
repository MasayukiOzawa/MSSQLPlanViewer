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
    bool IsOnCriticalPath,
    bool IsParallel)
{
    public double? EstimatedExecutions { get; init; }

    public string? EstimatedExecutionMode { get; init; }

    public decimal? EstimatedCpuCost { get; init; }

    public double? EstimatedCpuMs { get; init; }

    public double? EstimatedElapsedMs { get; init; }

    public double? ActualExecutions { get; init; }

    public string? ActualExecutionMode { get; init; }

    public double? ActualElapsedMs { get; init; }

    public double? ActualCpuMs { get; init; }

    public double? ActualLogicalReads { get; init; }

    public double? ActualPhysicalReads { get; init; }

    public double? AverageRowSize { get; init; }

    public bool HasImplicitConversion { get; init; }
}

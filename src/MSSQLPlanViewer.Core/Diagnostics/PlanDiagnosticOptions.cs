namespace MSSQLPlanViewer.Core.Diagnostics;

public sealed record PlanDiagnosticOptions
{
    public double CardinalityEstimateSkewWarningRatio { get; init; } = 10d;

    public double CardinalityEstimateSkewCriticalRatio { get; init; } = 100d;

    public double CardinalityEstimateSkewWarningMinimumRows { get; init; } = 1_000d;

    public double CardinalityEstimateSkewCriticalMinimumRows { get; init; } = 10_000d;

    public double ExpensiveLookupWarningExecutions { get; init; } = 1_000d;

    public double ExpensiveLookupCriticalExecutions { get; init; } = 100_000d;

    public double HighImpactMissingIndexImpact { get; init; } = 80d;

    public double MemoryGrantMinimumGrantedKb { get; init; } = 10_240d;

    public double MemoryGrantLowUsageRatio { get; init; } = 0.10d;

    public double StaleStatisticsModificationRatio { get; init; } = 0.20d;

    public double StaleStatisticsMinimumRows { get; init; } = 1_000d;

    public double StaleStatisticsSamplingPercent { get; init; } = 10d;

    public double StaleStatisticsSamplingMinimumRows { get; init; } = 1_000_000d;

    public double LargeScanRowsInfoThreshold { get; init; } = 100_000d;

    public double LargeScanRowsReadToRowsWarningRatio { get; init; } = 10d;

    public double ParallelThreadSkewMinimumRows { get; init; } = 10_000d;

    public double ParallelThreadSkewWarningRatio { get; init; } = 2d;

    public double ParallelThreadSkewCriticalRatio { get; init; } = 4d;

    public double ParallelThreadSkewCriticalCoefficientOfVariation { get; init; } = 1d;
}

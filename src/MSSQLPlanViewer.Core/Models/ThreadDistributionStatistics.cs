namespace MSSQLPlanViewer.Core.Models;

public sealed record ThreadDistributionStatistics(
    int WorkerThreadCount,
    double TotalRows,
    double MaxRows,
    double AverageRows,
    double MaxToAverageRatio,
    double CoefficientOfVariation)
{
    public static ThreadDistributionStatistics? Compute(IReadOnlyList<PlanThreadRuntimeMetrics> threads)
    {
        if (threads.Count == 0 || threads.All(thread => !thread.ActualRows.HasValue))
        {
            return null;
        }

        var workerThreads = threads.Any(thread => thread.ThreadId != 0)
            ? threads.Where(thread => thread.ThreadId != 0).ToArray()
            : threads.ToArray();

        if (workerThreads.Length < 2)
        {
            return null;
        }

        var rows = workerThreads
            .Select(thread => thread.ActualRows ?? 0d)
            .ToArray();
        var totalRows = rows.Sum();
        var averageRows = totalRows / rows.Length;

        if (averageRows <= 0d)
        {
            return null;
        }

        var maxRows = rows.Max();
        var variance = rows.Sum(value => Math.Pow(value - averageRows, 2d)) / rows.Length;
        var standardDeviation = Math.Sqrt(variance);

        return new ThreadDistributionStatistics(
            WorkerThreadCount: workerThreads.Length,
            TotalRows: totalRows,
            MaxRows: maxRows,
            AverageRows: averageRows,
            MaxToAverageRatio: maxRows / averageRows,
            CoefficientOfVariation: standardDeviation / averageRows);
    }
}

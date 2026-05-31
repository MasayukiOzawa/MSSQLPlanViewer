namespace MSSQLPlanViewer.Web.Components.Plan;

internal static class OperatorIconRegistry
{
    public static OperatorIconDescriptor Resolve(string physicalOp, string logicalOp)
    {
        var key = $"{physicalOp} {logicalOp}".ToLowerInvariant();

        if (key.Contains("nested loops", StringComparison.Ordinal))
        {
            return new OperatorIconDescriptor(OperatorIconKind.NestedLoops, "#2563eb");
        }

        if (key.Contains("merge join", StringComparison.Ordinal))
        {
            return new OperatorIconDescriptor(OperatorIconKind.MergeJoin, "#0f766e");
        }

        if (key.Contains("hash match", StringComparison.Ordinal))
        {
            return new OperatorIconDescriptor(OperatorIconKind.HashMatch, "#7c3aed");
        }

        if (key.Contains("index seek", StringComparison.Ordinal) || key.Contains("seek", StringComparison.Ordinal))
        {
            return new OperatorIconDescriptor(OperatorIconKind.Seek, "#1d4ed8");
        }

        if (key.Contains("scan", StringComparison.Ordinal))
        {
            return new OperatorIconDescriptor(OperatorIconKind.Scan, "#0891b2");
        }

        if (key.Contains("sort", StringComparison.Ordinal))
        {
            return new OperatorIconDescriptor(OperatorIconKind.Sort, "#dc2626");
        }

        if (key.Contains("filter", StringComparison.Ordinal))
        {
            return new OperatorIconDescriptor(OperatorIconKind.Filter, "#d97706");
        }

        if (key.Contains("compute scalar", StringComparison.Ordinal))
        {
            return new OperatorIconDescriptor(OperatorIconKind.ComputeScalar, "#4f46e5");
        }

        if (key.Contains("parallelism", StringComparison.Ordinal) || key.Contains("streams", StringComparison.Ordinal))
        {
            return new OperatorIconDescriptor(OperatorIconKind.Parallelism, "#0f766e");
        }

        if (key.Contains("aggregate", StringComparison.Ordinal))
        {
            return new OperatorIconDescriptor(OperatorIconKind.Aggregate, "#9333ea");
        }

        if (key.Contains("lookup", StringComparison.Ordinal))
        {
            return new OperatorIconDescriptor(OperatorIconKind.KeyLookup, "#1d4ed8");
        }

        if (key.Contains("spool", StringComparison.Ordinal))
        {
            return new OperatorIconDescriptor(OperatorIconKind.Spool, "#ea580c");
        }

        if (key.Contains("constant scan", StringComparison.Ordinal))
        {
            return new OperatorIconDescriptor(OperatorIconKind.ConstantScan, "#475569");
        }

        return new OperatorIconDescriptor(OperatorIconKind.Generic, "#475569");
    }
}

internal sealed record OperatorIconDescriptor(OperatorIconKind Kind, string AccentColor);

internal enum OperatorIconKind
{
    Generic = 0,
    Seek,
    Scan,
    NestedLoops,
    MergeJoin,
    HashMatch,
    Sort,
    Filter,
    ComputeScalar,
    Parallelism,
    Aggregate,
    KeyLookup,
    Spool,
    ConstantScan
}

using MSSQLPlanViewer.Core.Formatting;
using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Rendering;

public sealed class PlanTableProjector : IPlanTableProjector
{
    public IReadOnlyList<PlanTableRow> Project(StatementPlan statement)
    {
        var displayCostBasis = PlanCostDisplay.ResolveDisplayCostBasis(statement);
        var nodesById = statement.Nodes.ToDictionary(node => node.NodeId, StringComparer.Ordinal);
        var childrenByParent = statement.Edges
            .GroupBy(edge => edge.FromNodeId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(edge => edge.ToNodeId)
                    .Where(nodesById.ContainsKey)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var rootNodeIds = PlanTreeNavigator.ResolveRootNodeIds(statement, nodesById);

        var rows = new List<PlanTableRow>(statement.Nodes.Count);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rootNodeId in rootNodeIds)
        {
            AppendRow(rootNodeId, parentNodeId: null, depth: 0, displayCostBasis, nodesById, childrenByParent, visited, rows);
        }

        foreach (var node in statement.Nodes.Where(node => !visited.Contains(node.NodeId)))
        {
            AppendRow(node.NodeId, parentNodeId: null, depth: 0, displayCostBasis, nodesById, childrenByParent, visited, rows);
        }

        if (rows.Count > 0 && statement.Warnings.Count > 0)
        {
            var firstRow = rows[0];
            rows[0] = firstRow with
            {
                WarningCount = firstRow.WarningCount + statement.Warnings.Count,
                Summary = AppendWarningSummary(firstRow.Summary, statement.Warnings)
            };
        }

        return rows;
    }

    private static void AppendRow(
        string nodeId,
        string? parentNodeId,
        int depth,
        decimal displayCostBasis,
        IReadOnlyDictionary<string, PlanNode> nodesById,
        IReadOnlyDictionary<string, string[]> childrenByParent,
        ISet<string> visited,
        ICollection<PlanTableRow> rows)
    {
        if (!visited.Add(nodeId) || !nodesById.TryGetValue(nodeId, out var node))
        {
            return;
        }

        var childIds = childrenByParent.TryGetValue(nodeId, out var childNodes)
            ? childNodes
            : Array.Empty<string>();

        rows.Add(
            new PlanTableRow(
                NodeId: node.NodeId,
                ParentNodeId: parentNodeId,
                Depth: depth,
                HasChildren: childIds.Length > 0,
                PhysicalOp: node.PhysicalOp,
                LogicalOp: node.LogicalOp,
                ObjectName: PlanDisplayFormatter.FormatObjectName(node.ObjectReference),
                CostRatio: PlanCostDisplay.CalculateCostRatio(node, displayCostBasis),
                EstimatedSubtreeCost: node.EstimatedSubtreeCost,
                EstimatedCpuCost: node.EstimatedCpuCost,
                EstimatedIoCost: node.EstimatedIoCost,
                EstimatedRows: node.EstimatedRows,
                AverageRowSize: node.AverageRowSize,
                ActualRows: node.RuntimeMetrics.ActualRows,
                ActualExecutions: node.RuntimeMetrics.ActualExecutions,
                ActualLogicalReads: node.RuntimeMetrics.ActualLogicalReads,
                ActualPhysicalReads: node.RuntimeMetrics.ActualPhysicalReads,
                ActualCpuMs: node.RuntimeMetrics.ActualCpuMs,
                ActualElapsedMs: node.RuntimeMetrics.ActualElapsedMs,
                WarningCount: node.Warnings.Count,
                IsParallel: node.IsParallel,
                Summary: BuildSummary(node, displayCostBasis)));

        foreach (var childNodeId in childIds)
        {
            AppendRow(childNodeId, nodeId, depth + 1, displayCostBasis, nodesById, childrenByParent, visited, rows);
        }
    }

    private static string BuildSummary(PlanNode node, decimal displayCostBasis)
    {
        var parts = new List<string>();

        if (node.EstimatedSubtreeCost.HasValue)
        {
            var costRatio = PlanCostDisplay.CalculateCostRatio(node, displayCostBasis);
            parts.Add($"Cost {PlanDisplayFormatter.FormatPercent(costRatio)}");
            parts.Add($"EstSubtree {PlanDisplayFormatter.FormatCost(node.EstimatedSubtreeCost)}");
        }

        if (node.EstimatedCpuCost.HasValue)
        {
            parts.Add($"EstCPU {PlanDisplayFormatter.FormatCost(node.EstimatedCpuCost)}");
        }

        if (node.EstimatedIoCost.HasValue)
        {
            parts.Add($"EstIO {PlanDisplayFormatter.FormatCost(node.EstimatedIoCost)}");
        }

        if (node.Warnings.Count > 0)
        {
            parts.Add(PlanDisplayFormatter.FormatWarningSummary(node.Warnings));
        }

        return parts.Count == 0 ? "n/a" : string.Join(" | ", parts);
    }

    private static string AppendWarningSummary(string summary, IReadOnlyCollection<PlanWarning> warnings)
    {
        var warningSummary = PlanDisplayFormatter.FormatWarningSummary(warnings);

        if (string.IsNullOrWhiteSpace(summary) || string.Equals(summary, "n/a", StringComparison.OrdinalIgnoreCase))
        {
            return warningSummary;
        }

        return $"{summary} | {warningSummary}";
    }
}

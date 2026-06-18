namespace MSSQLPlanViewer.Core.Models;

public sealed record PlanNode(
    string NodeId,
    string PhysicalOp,
    string LogicalOp,
    decimal? EstimatedSubtreeCost,
    decimal? EstimatedCpuCost,
    decimal? EstimatedIoCost,
    double? EstimatedRows,
    double? AverageRowSize,
    bool IsParallel,
    PlanObjectReference? ObjectReference,
    PlanRuntimeMetrics RuntimeMetrics,
    IReadOnlyList<PlanWarning> Warnings,
    IReadOnlyList<PlanProperty> Properties,
    IReadOnlyList<PlanProperty> XmlAttributes,
    IReadOnlyList<PlanProperty> DetailXmlAttributes)
{
    public bool HasWarnings => Warnings.Count > 0;
}

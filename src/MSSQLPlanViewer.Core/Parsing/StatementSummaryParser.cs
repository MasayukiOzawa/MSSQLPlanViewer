using System.Globalization;
using System.Xml.Linq;
using MSSQLPlanViewer.Core.Formatting;
using MSSQLPlanViewer.Core.Models;
using static MSSQLPlanViewer.Core.Parsing.ShowplanXml;

namespace MSSQLPlanViewer.Core.Parsing;

/// <summary>
/// Builds the <see cref="StatementPlanSummary"/> and its statement-level entry collections
/// (missing indexes, wait stats, optimizer stats usage, parameters, accessed objects/indexes).
/// </summary>
internal static class StatementSummaryParser
{
    public static StatementPlanSummary ParseSummary(XElement statementElement, XElement queryPlanElement, IReadOnlyList<XElement> rootRelOps, IReadOnlyList<PlanNode> nodes) =>
        new StatementPlanSummary(
            EstimatedSubtreeCost: GetDecimalAttribute(statementElement, "StatementSubTreeCost"),
            EstimatedRows: GetDoubleAttribute(statementElement, "StatementEstRows"),
            CachedPlanSizeKb: GetIntAttribute(queryPlanElement, "CachedPlanSize"),
            CompileTimeMs: GetIntAttribute(queryPlanElement, "CompileTime"),
            CompileCpuMs: GetIntAttribute(queryPlanElement, "CompileCPU"),
            CompileMemoryKb: GetIntAttribute(queryPlanElement, "CompileMemory"),
            EstimatedAvailableMemoryGrantKb: GetDoubleAttribute(queryPlanElement, "EstimatedAvailableMemoryGrant"),
            EstimatedMemoryGrantKb: GetDoubleAttribute(queryPlanElement, "EstimatedMemoryGrant"),
            QueryPlanProperties: BuildAttributeProperties(queryPlanElement),
            QueryTimeStatsProperties: BuildAttributeProperties(GetChild(queryPlanElement, "QueryTimeStats")),
            MemoryGrantInfoProperties: BuildAttributeProperties(GetChild(queryPlanElement, "MemoryGrantInfo")),
            OptimizerHardwareDependentProperties: BuildAttributeProperties(GetChild(queryPlanElement, "OptimizerHardwareDependentProperties")),
            OptimizerStatsUsageEntries: BuildOptimizerStatsUsageEntries(statementElement, queryPlanElement),
            MissingIndexesEntries: BuildMissingIndexesEntries(queryPlanElement),
            WaitStatsEntries: BuildWaitStatsEntries(queryPlanElement),
            AccessedObjectEntries: BuildAccessedObjectEntries(rootRelOps),
            AccessedIndexEntries: BuildAccessedIndexEntries(nodes),
            SeekScanPredicateEntries: BuildSeekScanPredicateEntries(nodes),
            ParameterListEntries: BuildParameterListEntries(queryPlanElement))
        {
            ThreadStatProperties = BuildThreadStatProperties(queryPlanElement),
            ImplicitConversionEntries = BuildImplicitConversionEntries(nodes)
        };

    private static IReadOnlyList<ImplicitConversionEntry> BuildImplicitConversionEntries(IEnumerable<PlanNode> nodes) =>
        nodes
            .SelectMany(node => node.Properties
                .SelectMany(property => SelectImplicitConversionExpressions(property)
                    .Select(expression => new ImplicitConversionEntry(
                        NodeId: node.NodeId,
                        PhysicalOp: node.PhysicalOp,
                        LogicalOp: node.LogicalOp,
                        Database: node.ObjectReference?.Database,
                        Schema: node.ObjectReference?.Schema,
                        Table: node.ObjectReference?.Table,
                        Index: node.ObjectReference?.Index,
                        IndexKind: node.ObjectReference?.IndexKind,
                        Source: property.Name,
                        Expression: expression))))
            .Distinct()
            .OrderBy(entry => GetNodeSortKey(entry.NodeId))
            .ThenBy(entry => entry.NodeId, StringComparer.Ordinal)
            .ThenBy(entry => entry.Source, StringComparer.Ordinal)
            .ThenBy(entry => entry.Expression, StringComparer.Ordinal)
            .ToArray();

    private static IEnumerable<string> SelectImplicitConversionExpressions(PlanProperty property)
    {
        if (!ContainsImplicitConversion(property.Value))
        {
            yield break;
        }

        foreach (var segment in property.Value.Split(" | ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (ContainsImplicitConversion(segment))
            {
                yield return segment;
            }
        }
    }

    private static bool ContainsImplicitConversion(string value) =>
        value.Contains("CONVERT_IMPLICIT", StringComparison.OrdinalIgnoreCase);

    private static int GetNodeSortKey(string nodeId) =>
        int.TryParse(nodeId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : int.MaxValue;

    private static IReadOnlyList<AccessedObjectEntry> BuildAccessedObjectEntries(IEnumerable<XElement> rootRelOps) =>
        rootRelOps
            .SelectMany(rootRelOp => rootRelOp.DescendantsAndSelf().Where(element => HasLocalName(element, "Object")))
            .Select(objectElement => new AccessedObjectEntry(
                Database: GetAttribute(objectElement, "Database"),
                Schema: GetAttribute(objectElement, "Schema"),
                Table: GetAttribute(objectElement, "Table") ?? string.Empty))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Table))
            .Distinct()
            .OrderBy(entry => entry.Database ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(entry => entry.Schema ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(entry => entry.Table, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<AccessedIndexEntry> BuildAccessedIndexEntries(IEnumerable<PlanNode> nodes) =>
        nodes
            .Where(node => !string.IsNullOrWhiteSpace(node.ObjectReference?.Index))
            .Select(node => new AccessedIndexEntry(
                NodeId: node.NodeId,
                PhysicalOp: node.PhysicalOp,
                LogicalOp: node.LogicalOp,
                Database: node.ObjectReference?.Database,
                Schema: node.ObjectReference?.Schema,
                Table: node.ObjectReference?.Table,
                Index: node.ObjectReference?.Index ?? string.Empty,
                IndexKind: node.ObjectReference?.IndexKind,
                EstimatedRows: node.EstimatedRows,
                EstimatedIoCost: node.EstimatedIoCost,
                ActualRows: node.RuntimeMetrics.ActualRows,
                ActualLogicalReads: node.RuntimeMetrics.ActualLogicalReads,
                ActualPhysicalReads: node.RuntimeMetrics.ActualPhysicalReads))
            .ToArray();

    private static IReadOnlyList<SeekScanPredicateEntry> BuildSeekScanPredicateEntries(IEnumerable<PlanNode> nodes) =>
        nodes
            .Where(IsObjectSeekOrScan)
            .Select(node => new SeekScanPredicateEntry(
                NodeId: node.NodeId,
                PhysicalOp: node.PhysicalOp,
                LogicalOp: node.LogicalOp,
                Database: node.ObjectReference?.Database,
                Schema: node.ObjectReference?.Schema,
                Table: node.ObjectReference?.Table,
                Index: node.ObjectReference?.Index,
                IndexKind: node.ObjectReference?.IndexKind,
                Predicate: GetPropertyValue(node.Properties, "Predicate"),
                SeekPredicate: GetPropertyValue(node.Properties, "Seek predicate")))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Predicate)
                || !string.IsNullOrWhiteSpace(entry.SeekPredicate))
            .ToArray();

    private static bool IsObjectSeekOrScan(PlanNode node) =>
        node.ObjectReference is not null
        && (node.PhysicalOp.Contains("Seek", StringComparison.OrdinalIgnoreCase)
            || node.PhysicalOp.Contains("Scan", StringComparison.OrdinalIgnoreCase));

    private static string? GetPropertyValue(IEnumerable<PlanProperty> properties, string name) =>
        properties.FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;

    private static IReadOnlyList<ParameterListEntry> BuildParameterListEntries(XElement queryPlanElement)
    {
        var parameterListElement = GetChild(queryPlanElement, "ParameterList");
        if (parameterListElement is null)
        {
            return Array.Empty<ParameterListEntry>();
        }

        var entries = new List<ParameterListEntry>();
        foreach (var columnReferenceElement in GetChildren(parameterListElement, "ColumnReference"))
        {
            var parameter = GetAttribute(columnReferenceElement, "Column")
                ?? GetAttribute(columnReferenceElement, "ParameterName")
                ?? FormatColumnReference(columnReferenceElement);

            if (string.IsNullOrWhiteSpace(parameter))
            {
                continue;
            }

            entries.Add(new ParameterListEntry(
                Parameter: parameter,
                DataType: GetAttribute(columnReferenceElement, "ParameterDataType"),
                CompiledValue: GetAttribute(columnReferenceElement, "ParameterCompiledValue"),
                RuntimeValue: GetAttribute(columnReferenceElement, "ParameterRuntimeValue"),
                IsNullable: GetAttribute(columnReferenceElement, "ParameterIsNullable")));
        }

        return entries;
    }

    private static IReadOnlyList<IReadOnlyList<PlanProperty>> BuildThreadStatProperties(XElement queryPlanElement) =>
        GetChildren(queryPlanElement, "ThreadStat")
            .Select(BuildAttributeProperties)
            .Where(properties => properties.Count > 0)
            .ToArray();

    private static IReadOnlyList<MissingIndexEntry> BuildMissingIndexesEntries(XElement queryPlanElement)
    {
        var missingIndexesElement = GetChild(queryPlanElement, "MissingIndexes");
        if (missingIndexesElement is null)
        {
            return Array.Empty<MissingIndexEntry>();
        }

        var entries = new List<MissingIndexEntry>();
        var groupIndex = 1;

        foreach (var groupElement in GetChildren(missingIndexesElement, "MissingIndexGroup"))
        {
            var impact = FormatNumericText(GetAttribute(groupElement, "Impact"));

            foreach (var missingIndexElement in GetChildren(groupElement, "MissingIndex"))
            {
                var label = BuildMissingIndexLabel(missingIndexElement, groupIndex);
                string? equalityColumns = null;
                string? inequalityColumns = null;
                string? includeColumns = null;

                foreach (var columnGroupElement in GetChildren(missingIndexElement, "ColumnGroup"))
                {
                    var usage = GetAttribute(columnGroupElement, "Usage") ?? "Columns";
                    var columns = GetChildren(columnGroupElement, "Column")
                        .Select(columnElement => GetAttribute(columnElement, "Name"))
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Cast<string>()
                        .ToArray();

                    if (columns.Length > 0)
                    {
                        var value = string.Join(", ", columns);
                        switch (usage.ToUpperInvariant())
                        {
                            case "EQUALITY":
                                equalityColumns = AppendGroupValue(equalityColumns, value);
                                break;
                            case "INEQUALITY":
                                inequalityColumns = AppendGroupValue(inequalityColumns, value);
                                break;
                            case "INCLUDE":
                                includeColumns = AppendGroupValue(includeColumns, value);
                                break;
                        }
                    }
                }

                entries.Add(new MissingIndexEntry(
                    ObjectName: label,
                    Impact: string.IsNullOrWhiteSpace(impact) ? null : impact,
                    EqualityColumns: equalityColumns,
                    InequalityColumns: inequalityColumns,
                    IncludeColumns: includeColumns));
                groupIndex++;
            }
        }

        return entries;
    }

    private static string AppendGroupValue(string? existing, string value) =>
        string.IsNullOrWhiteSpace(existing)
            ? value
            : $"{existing} | {value}";

    private static IReadOnlyList<OptimizerStatsUsageEntry> BuildOptimizerStatsUsageEntries(XElement statementElement, XElement queryPlanElement)
    {
        var optimizerStatsUsageElement =
            GetChild(statementElement, "OptimizerStatsUsage")
            ?? GetChild(queryPlanElement, "OptimizerStatsUsage");

        if (optimizerStatsUsageElement is null)
        {
            return Array.Empty<OptimizerStatsUsageEntry>();
        }

        return GetChildren(optimizerStatsUsageElement, "StatisticsInfo")
            .Select(statisticsInfoElement => new OptimizerStatsUsageEntry(
                Database: GetAttribute(statisticsInfoElement, "Database"),
                Schema: GetAttribute(statisticsInfoElement, "Schema"),
                Table: GetAttribute(statisticsInfoElement, "Table"),
                Statistics: GetAttribute(statisticsInfoElement, "Statistics"),
                LastUpdate: GetAttribute(statisticsInfoElement, "LastUpdate"),
                StatisticsModificationCount: GetAttribute(statisticsInfoElement, "StatisticsModificationCount") ?? GetAttribute(statisticsInfoElement, "ModificationCount"),
                LastSample: GetAttribute(statisticsInfoElement, "LastSample"),
                SamplingPercent: GetAttribute(statisticsInfoElement, "SamplingPercent"),
                Steps: GetAttribute(statisticsInfoElement, "Steps"),
                Rows: GetAttribute(statisticsInfoElement, "Rows"),
                UnfilteredRows: GetAttribute(statisticsInfoElement, "UnfilteredRows"),
                PersistedSamplePercent: GetAttribute(statisticsInfoElement, "PersistedSamplePercent")))
            .ToArray();
    }

    private static IReadOnlyList<WaitStatEntry> BuildWaitStatsEntries(XElement queryPlanElement)
    {
        var waitStatsElement = GetChild(queryPlanElement, "WaitStats");
        if (waitStatsElement is null)
        {
            return Array.Empty<WaitStatEntry>();
        }

        var entries = new List<WaitStatEntry>();
        var waitIndex = 1;

        foreach (var waitElement in GetChildren(waitStatsElement, "Wait"))
        {
            var waitType = GetAttribute(waitElement, "WaitType");
            entries.Add(new WaitStatEntry(
                WaitType: !string.IsNullOrWhiteSpace(waitType) ? waitType! : $"Wait {waitIndex}",
                WaitTimeMs: GetDoubleAttribute(waitElement, "WaitTimeMs"),
                WaitCount: GetDoubleAttribute(waitElement, "WaitCount")));
            waitIndex++;
        }

        return entries;
    }

    private static string BuildMissingIndexLabel(XElement missingIndexElement, int ordinal)
    {
        var parts = new[]
        {
            GetAttribute(missingIndexElement, "Database"),
            GetAttribute(missingIndexElement, "Schema"),
            GetAttribute(missingIndexElement, "Table")
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .ToArray();

        return parts.Length > 0
            ? string.Join(".", parts)
            : $"Missing index {ordinal.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FormatNumericText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var numericValue))
        {
            return PlanDisplayFormatter.FormatNumber(numericValue);
        }

        return value;
    }
}

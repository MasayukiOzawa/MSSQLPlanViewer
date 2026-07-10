using System.Xml.Linq;
using MSSQLPlanViewer.Core.Formatting;
using MSSQLPlanViewer.Core.Models;
using static MSSQLPlanViewer.Core.Parsing.ShowplanXml;

namespace MSSQLPlanViewer.Core.Parsing;

/// <summary>
/// Builds the display properties and raw XML attribute properties for a single RelOp node.
/// </summary>
internal static class PlanNodePropertyBuilder
{
    private static readonly string[] ExcludedXmlAttributePathPatterns =
    [
        "RelOp.OutputList.ColumnReference",
        "RelOp.ComputeScalar.DefinedValue",
        "RelOp.*.DefinedValues",
        "RelOp.*.OrderBy",
        "RelOp.StreamAggregate.GroupBy.ColumnReference",
        "RelOp.RunTimeInformation",
        "RelOp.*.HashKeysBuild.ColumnReference",
        "RelOp.*.HashKeysProbe.ColumnReference",
        "RelOp.*.OuterReferences.ColumnReference",
        "RelOp.*.BuildResidual.ScalarOperator",
        "RelOp.*.ProbeResidual.ScalarOperator",
        "RelOp.**.ScalarExpressionList",
        "RelOp.Bitmap.HashKeys.ColumnReference",
        "RelOp.IndexScan.Predicate",
        "RelOp.IndexScan.SeekPredicate"
    ];

    public static IReadOnlyList<PlanProperty> Build(
        XElement relOpElement,
        string physicalOp,
        string logicalOp,
        PlanObjectReference? objectReference,
        PlanRuntimeMetrics runtimeMetrics,
        IReadOnlyCollection<PlanWarning> warnings)
    {
        var properties = new List<PlanProperty>
        {
            new("Node ID", GetAttribute(relOpElement, "NodeId") ?? "n/a"),
            new("Physical operation", physicalOp),
            new("Logical operation", logicalOp)
        };
        var joinCondition = BuildJoinConditionText(relOpElement, physicalOp, logicalOp);

        AddProperty(properties, "Object", PlanDisplayFormatter.FormatObjectName(objectReference), objectReference is not null);
        AddProperty(properties, "Estimated rows", PlanDisplayFormatter.FormatNumber(GetDoubleAttribute(relOpElement, "EstimateRows")));
        AddProperty(properties, "Estimated subtree cost", PlanDisplayFormatter.FormatCost(GetDecimalAttribute(relOpElement, "EstimatedTotalSubtreeCost")));
        AddProperty(properties, "Estimated CPU cost", PlanDisplayFormatter.FormatCost(GetDecimalAttribute(relOpElement, "EstimateCPU")));
        AddProperty(properties, "Estimated I/O cost", PlanDisplayFormatter.FormatCost(GetDecimalAttribute(relOpElement, "EstimateIO")));
        AddProperty(properties, "Average row size", PlanDisplayFormatter.FormatNumber(GetDoubleAttribute(relOpElement, "AvgRowSize")));
        AddProperty(properties, "Parallel", GetAttribute(relOpElement, "Parallel"));
        AddProperty(properties, "ActualExecutionMode", ResolveActualExecutionMode(relOpElement, runtimeMetrics));
        AddProperty(properties, "Actual rows", PlanDisplayFormatter.FormatNumber(runtimeMetrics.ActualRows), runtimeMetrics.ActualRows.HasValue);
        AddProperty(properties, "Actual executions", PlanDisplayFormatter.FormatNumber(runtimeMetrics.ActualExecutions), runtimeMetrics.ActualExecutions.HasValue);
        AddProperty(properties, "Actual logical reads", PlanDisplayFormatter.FormatNumber(runtimeMetrics.ActualLogicalReads), runtimeMetrics.ActualLogicalReads.HasValue);
        AddProperty(properties, "Actual physical reads", PlanDisplayFormatter.FormatNumber(runtimeMetrics.ActualPhysicalReads), runtimeMetrics.ActualPhysicalReads.HasValue);
        AddProperty(properties, "Actual CPU ms", PlanDisplayFormatter.FormatNumber(runtimeMetrics.ActualCpuMs), runtimeMetrics.ActualCpuMs.HasValue);
        AddProperty(properties, "Actual elapsed ms", PlanDisplayFormatter.FormatNumber(runtimeMetrics.ActualElapsedMs), runtimeMetrics.ActualElapsedMs.HasValue);
        AddProperty(properties, "Actual rebinds", PlanDisplayFormatter.FormatNumber(runtimeMetrics.ActualRebinds), runtimeMetrics.ActualRebinds is > 0);
        AddProperty(properties, "Actual rewinds", PlanDisplayFormatter.FormatNumber(runtimeMetrics.ActualRewinds), runtimeMetrics.ActualRewinds is > 0);
        AddProperty(properties, "Join condition", joinCondition);
        AddProperty(properties, "Predicate", BuildPredicateText(relOpElement), string.IsNullOrWhiteSpace(joinCondition));
        AddProperty(properties, "Seek predicate", BuildSeekPredicateText(relOpElement));
        AddProperty(properties, "Order by", BuildOrderByText(relOpElement));
        AddProperty(properties, "Top expression", BuildTopExpressionText(relOpElement));
        AddProperty(properties, "Tie columns", BuildColumnListText(relOpElement, "TieColumns"));
        AddProperty(properties, "Group by", BuildColumnListText(relOpElement, "GroupBy"));
        AddProperty(properties, "Defined values", BuildDefinedValuesText(relOpElement, physicalOp));

        var outputColumns = GetOwnedDescendants(relOpElement, "OutputList")
            .SelectMany(SelectColumnReferenceTexts)
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();

        if (outputColumns.Length > 0)
        {
            properties.Add(new PlanProperty("Output columns", string.Join(", ", outputColumns)));
        }

        properties.AddRange(BuildDirectIteratorElementProperties(relOpElement));

        if (warnings.Count > 0)
        {
            properties.Add(new PlanProperty("Warnings", PlanDisplayFormatter.FormatWarningSummary(warnings)));
        }

        return properties;
    }

    public static IReadOnlyList<PlanProperty> BuildXmlAttributeProperties(
        XElement relOpElement,
        bool excludeConfiguredSubtrees)
    {
        var properties = new List<PlanProperty>();
        TraverseOwnedElements(relOpElement, relOpElement.Name.LocalName);
        return properties;

        void TraverseOwnedElements(XElement element, string path)
        {
            if (excludeConfiguredSubtrees && ShouldExcludeXmlAttributePath(path))
            {
                return;
            }

            foreach (var attribute in element.Attributes())
            {
                properties.Add(new PlanProperty($"{path}.{attribute.Name.LocalName}", attribute.Value));
            }

            var children = element.Elements()
                .Where(child => !HasLocalName(child, "RelOp"))
                .ToArray();
            var totalCounts = children
                .GroupBy(child => child.Name.LocalName)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var seenCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var child in children)
            {
                var currentIndex = seenCounts.TryGetValue(child.Name.LocalName, out var seenCount)
                    ? seenCount + 1
                    : 1;
                seenCounts[child.Name.LocalName] = currentIndex;

                var childPath = totalCounts[child.Name.LocalName] > 1
                    ? $"{path}.{child.Name.LocalName}[{currentIndex}]"
                    : $"{path}.{child.Name.LocalName}";

                TraverseOwnedElements(child, childPath);
            }
        }
    }

    private static bool ShouldExcludeXmlAttributePath(string path) =>
        ShowplanXmlAttributePathMatcher.MatchesAny(path, ExcludedXmlAttributePathPatterns);

    private static void AddProperty(
        ICollection<PlanProperty> properties,
        string name,
        string? value,
        bool include = true)
    {
        if (!include || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        properties.Add(new PlanProperty(name, value));
    }

    private static string? ResolveActualExecutionMode(XElement relOpElement, PlanRuntimeMetrics runtimeMetrics)
    {
        if (runtimeMetrics.Threads.Count > 1 && !string.IsNullOrWhiteSpace(runtimeMetrics.ActualExecutionMode))
        {
            return runtimeMetrics.ActualExecutionMode;
        }

        return GetAttribute(relOpElement, "ActualExecutionMode") ?? runtimeMetrics.ActualExecutionMode;
    }

    private static string? BuildPredicateText(XElement relOpElement)
    {
        var predicates = GetOwnedDescendants(relOpElement, "Predicate")
            .Select(ExtractScalarStringOrDetails)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return predicates.Length == 0 ? null : string.Join(" | ", predicates);
    }

    private static string? BuildJoinConditionText(XElement relOpElement, string physicalOp, string logicalOp)
    {
        if (!physicalOp.Contains("Join", StringComparison.OrdinalIgnoreCase)
            && !logicalOp.Contains("Join", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(physicalOp, "Nested Loops", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var conditions = new List<string>();

        foreach (var elementName in new[] { "Predicate", "ProbeResidual", "Residual" })
        {
            conditions.AddRange(
                GetOwnedDescendants(relOpElement, elementName)
                    .Select(ExtractScalarStringOrDetails)
                    .Where(value => !string.IsNullOrWhiteSpace(value)));
        }

        conditions.AddRange(BuildJoinColumnPairs(relOpElement, "OuterSideJoinColumns", "InnerSideJoinColumns"));
        conditions.AddRange(BuildJoinColumnPairs(relOpElement, "HashKeysBuild", "HashKeysProbe"));

        var outerReferences = GetOwnedDescendants(relOpElement, "OuterReferences")
            .SelectMany(SelectColumnReferenceTexts)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (outerReferences.Length > 0)
        {
            conditions.Add($"Outer references: {string.Join(", ", outerReferences)}");
        }

        var distinctConditions = conditions
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return distinctConditions.Length == 0 ? null : string.Join(" | ", distinctConditions);
    }

    private static IEnumerable<string> BuildJoinColumnPairs(
        XElement relOpElement,
        string leftElementName,
        string rightElementName)
    {
        var pairs = PairSides(SelectColumnReferenceTexts);
        return pairs.Length > 0 ? pairs : PairSides(SelectScalarStrings);

        string[] PairSides(Func<XElement, IEnumerable<string>> selectValues)
        {
            var leftValues = SelectSide(leftElementName, selectValues);
            var rightValues = SelectSide(rightElementName, selectValues);

            return leftValues.Length > 0 && leftValues.Length == rightValues.Length
                ? leftValues.Zip(rightValues, (left, right) => $"{left} = {right}").ToArray()
                : Array.Empty<string>();
        }

        string[] SelectSide(string elementName, Func<XElement, IEnumerable<string>> selectValues) =>
            GetOwnedDescendants(relOpElement, elementName)
                .SelectMany(selectValues)
                .ToArray();
    }

    private static string? BuildSeekPredicateText(XElement relOpElement)
    {
        var segments = new List<string>();

        foreach (var rangeElementName in new[] { "Prefix", "StartRange", "EndRange" })
        {
            foreach (var rangeElement in GetOwnedDescendants(relOpElement, rangeElementName))
            {
                var rangeColumnsElement = GetChild(rangeElement, "RangeColumns");
                var columns = rangeColumnsElement is null
                    ? Array.Empty<string>()
                    : SelectColumnReferenceTexts(rangeColumnsElement).ToArray();

                var rangeExpressionsElement = GetChild(rangeElement, "RangeExpressions");
                var expressions = rangeExpressionsElement is null
                    ? Array.Empty<string>()
                    : SelectScalarStrings(rangeExpressionsElement).ToArray();

                var label = rangeElementName switch
                {
                    "Prefix" => "Prefix",
                    "StartRange" => "Start",
                    "EndRange" => "End",
                    _ => rangeElementName
                };

                var scanType = GetAttribute(rangeElement, "ScanType");
                if (!string.IsNullOrWhiteSpace(scanType))
                {
                    label += $" ({scanType})";
                }

                if (columns.Length > 0 && columns.Length == expressions.Length)
                {
                    segments.Add($"{label}: {string.Join(", ", columns.Zip(expressions, (column, expression) => $"{column} = {expression}"))}");
                    continue;
                }

                var combined = expressions.Length > 0 ? string.Join(", ", expressions) : string.Join(", ", columns);
                if (!string.IsNullOrWhiteSpace(combined))
                {
                    segments.Add($"{label}: {combined}");
                }
            }
        }

        var distinctSegments = segments
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return distinctSegments.Length == 0 ? null : string.Join(" | ", distinctSegments);
    }

    private static string? BuildOrderByText(XElement relOpElement)
    {
        var orderByColumns = GetOwnedDescendants(relOpElement, "OrderByColumn")
            .Select(orderByColumnElement =>
            {
                var columnName = SelectColumnReferenceTexts(orderByColumnElement).FirstOrDefault();
                if (string.IsNullOrWhiteSpace(columnName))
                {
                    return null;
                }

                var ascending = GetAttribute(orderByColumnElement, "Ascending");
                var direction = string.Equals(ascending, "false", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ascending, "0", StringComparison.OrdinalIgnoreCase)
                    ? "DESC"
                    : "ASC";

                return $"{columnName} {direction}";
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return orderByColumns.Length == 0 ? null : string.Join(", ", orderByColumns);
    }

    private static string? BuildTopExpressionText(XElement relOpElement) =>
        GetOwnedDescendants(relOpElement, "TopExpression")
            .Select(ExtractScalarStringOrDetails)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .FirstOrDefault();

    private static string? BuildColumnListText(XElement relOpElement, string elementName)
    {
        var columns = GetOwnedDescendants(relOpElement, elementName)
            .SelectMany(SelectColumnReferenceTexts)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return columns.Length == 0 ? null : string.Join(", ", columns);
    }

    private static string? BuildDefinedValuesText(XElement relOpElement, string physicalOp)
    {
        var values = GetOwnedDescendants(relOpElement, "DefinedValue")
            .Select(definedValueElement =>
            {
                var target = SelectColumnReferenceTexts(definedValueElement).FirstOrDefault();
                var scalarString = SelectScalarStrings(definedValueElement).FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(scalarString))
                {
                    return !string.IsNullOrWhiteSpace(target)
                        ? $"{target} = {scalarString}"
                        : scalarString;
                }

                if (physicalOp.Contains("Bitmap", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(target))
                {
                    return target;
                }

                return null;
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return values.Length == 0 ? null : string.Join(" | ", values);
    }

    private static IEnumerable<PlanProperty> BuildDirectIteratorElementProperties(XElement relOpElement)
    {
        foreach (var element in relOpElement.Elements())
        {
            if (HasLocalName(element, "OutputList")
                || HasLocalName(element, "RunTimeInformation")
                || HasLocalName(element, "Warnings"))
            {
                continue;
            }

            var attributeText = FormatAttributes(element);

            if (string.IsNullOrWhiteSpace(attributeText))
            {
                continue;
            }

            yield return new PlanProperty(element.Name.LocalName, attributeText);
        }
    }
}

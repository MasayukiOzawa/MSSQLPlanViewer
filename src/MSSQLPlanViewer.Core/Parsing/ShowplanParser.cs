using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using MSSQLPlanViewer.Core.Formatting;
using MSSQLPlanViewer.Core.Models;

namespace MSSQLPlanViewer.Core.Parsing;

public sealed class ShowplanParser : IShowplanParser
{
    private static readonly IReadOnlyDictionary<string, ShowplanSchemaVersion> NamespaceMap =
        new Dictionary<string, ShowplanSchemaVersion>(StringComparer.OrdinalIgnoreCase)
        {
            ["http://schemas.microsoft.com/sqlserver/2004/07/showplan"] = ShowplanSchemaVersion.SqlServer2004,
            ["http://schemas.microsoft.com/sqlserver/2012/01/showplan"] = ShowplanSchemaVersion.SqlServer2012,
            ["http://schemas.microsoft.com/sqlserver/2014/07/showplan"] = ShowplanSchemaVersion.SqlServer2014,
            ["http://schemas.microsoft.com/sqlserver/2017/03/showplan"] = ShowplanSchemaVersion.SqlServer2017,
            ["http://schemas.microsoft.com/sqlserver/2022/ShowPlan"] = ShowplanSchemaVersion.SqlServer2022
        };

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

    private const int MaxXmlInputLength = 10 * 1024 * 1024;

    public ShowplanDocument Parse(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
        {
            throw new ShowplanParseException("Enter Showplan XML.");
        }

        if (xml.Length > MaxXmlInputLength)
        {
            throw new ShowplanParseException("Showplan XML is too large to parse.");
        }

        XDocument document;

        try
        {
            document = LoadDocument(xml);
        }
        catch (XmlException exception)
        {
            throw new ShowplanParseException(
                $"Failed to parse XML. Line {exception.LineNumber}, position {exception.LinePosition}: {exception.Message}",
                exception);
        }

        var root = document.Root;
        if (root is null || !HasLocalName(root, "ShowPlanXML"))
        {
            throw new ShowplanParseException("ShowPlanXML root element was not found. Paste SQL Server execution plan XML.");
        }

        var namespaceUri = root.Name.NamespaceName;
        var metadata = new ShowplanMetadata(
            namespaceUri,
            NamespaceMap.TryGetValue(namespaceUri, out var schemaVersion) ? schemaVersion : ShowplanSchemaVersion.Unknown,
            GetAttribute(root, "Version"),
            GetAttribute(root, "Build"));

        var statements = ParseStatements(root);
        if (statements.Count == 0)
        {
            throw new ShowplanParseException("No statement containing QueryPlan was found.");
        }

        return new ShowplanDocument(metadata, statements);
    }

    private static XDocument LoadDocument(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaxXmlInputLength
        };

        using var stringReader = new StringReader(xml);
        using var xmlReader = XmlReader.Create(stringReader, settings);
        return XDocument.Load(xmlReader, LoadOptions.SetLineInfo);
    }

    private static IReadOnlyList<StatementPlan> ParseStatements(XElement root)
    {
        var statements = new List<StatementPlan>();

        foreach (var statementElement in root.Descendants().Where(IsStatementElement))
        {
            var queryPlan = statementElement.Elements().FirstOrDefault(element => HasLocalName(element, "QueryPlan"));
            if (queryPlan is null)
            {
                continue;
            }

            var rootRelOps = queryPlan
                .Descendants()
                .Where(element => HasLocalName(element, "RelOp") && GetNearestAncestorByLocalName(element, "RelOp") is null)
                .ToList();

            if (rootRelOps.Count == 0)
            {
                continue;
            }

            var nodes = new List<PlanNode>();
            var edges = new List<PlanEdge>();

            foreach (var rootRelOp in rootRelOps)
            {
                ParseRelOp(rootRelOp, nodes, edges, parentNodeId: null);
            }

            statements.Add(
                new StatementPlan(
                    StatementId: GetAttribute(statementElement, "StatementId") ?? statements.Count.ToString(CultureInfo.InvariantCulture),
                    StatementType: statementElement.Name.LocalName,
                    StatementText: GetAttribute(statementElement, "StatementText") ?? "(statement text unavailable)",
                    Summary: ParseStatementSummary(statementElement, queryPlan),
                    Nodes: nodes,
                    Edges: edges,
                    Warnings: ParseStatementWarnings(statementElement, queryPlan),
                    RootNodeIds: rootRelOps
                        .Select(rootRelOp => GetAttribute(rootRelOp, "NodeId"))
                        .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
                        .Cast<string>()
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()));
        }

        return statements;
    }

    private static void ParseRelOp(
        XElement relOpElement,
        ICollection<PlanNode> nodes,
        ICollection<PlanEdge> edges,
        string? parentNodeId)
    {
        var nodeId = GetAttribute(relOpElement, "NodeId")
            ?? throw new ShowplanParseException("RelOp element is missing the NodeId attribute.");

        var physicalOp = GetAttribute(relOpElement, "PhysicalOp") ?? "Unknown";
        var logicalOp = GetAttribute(relOpElement, "LogicalOp") ?? physicalOp;
        var objectReference = ParseObjectReference(relOpElement);
        var warnings = ParseWarnings(relOpElement);
        var runtimeMetrics = ParseRuntimeMetrics(relOpElement);

        var node = new PlanNode(
            NodeId: nodeId,
            PhysicalOp: physicalOp,
            LogicalOp: logicalOp,
            EstimatedSubtreeCost: GetDecimalAttribute(relOpElement, "EstimatedTotalSubtreeCost"),
            EstimatedCpuCost: GetDecimalAttribute(relOpElement, "EstimateCPU"),
            EstimatedIoCost: GetDecimalAttribute(relOpElement, "EstimateIO"),
            EstimatedRows: GetDoubleAttribute(relOpElement, "EstimateRows"),
            AverageRowSize: GetDoubleAttribute(relOpElement, "AvgRowSize"),
            IsParallel: string.Equals(GetAttribute(relOpElement, "Parallel"), "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(GetAttribute(relOpElement, "Parallel"), "1", StringComparison.OrdinalIgnoreCase),
            ObjectReference: objectReference,
            RuntimeMetrics: runtimeMetrics,
            Warnings: warnings,
            Properties: BuildProperties(relOpElement, physicalOp, logicalOp, objectReference, runtimeMetrics, warnings),
            XmlAttributes: BuildXmlAttributeProperties(relOpElement));

        nodes.Add(node);

        if (!string.IsNullOrWhiteSpace(parentNodeId))
        {
            edges.Add(new PlanEdge(parentNodeId, nodeId));
        }

        foreach (var childRelOp in GetOwnedDescendants(relOpElement, "RelOp"))
        {
            ParseRelOp(childRelOp, nodes, edges, nodeId);
        }
    }

    private static StatementPlanSummary ParseStatementSummary(XElement statementElement, XElement queryPlanElement) =>
        new(
            EstimatedSubtreeCost: GetDecimalAttribute(statementElement, "StatementSubTreeCost"),
            EstimatedRows: GetDoubleAttribute(statementElement, "StatementEstRows"),
            CachedPlanSizeKb: GetIntAttribute(queryPlanElement, "CachedPlanSize"),
            CompileTimeMs: GetIntAttribute(queryPlanElement, "CompileTime"),
            CompileCpuMs: GetIntAttribute(queryPlanElement, "CompileCPU"),
            CompileMemoryKb: GetIntAttribute(queryPlanElement, "CompileMemory"),
            EstimatedAvailableMemoryGrantKb: GetDoubleAttribute(queryPlanElement, "EstimatedAvailableMemoryGrant"),
            EstimatedMemoryGrantKb: GetDoubleAttribute(queryPlanElement, "EstimatedMemoryGrant"),
            QueryPlanProperties: BuildAttributeProperties(queryPlanElement),
            QueryTimeStatsProperties: BuildAttributeProperties(queryPlanElement.Elements().FirstOrDefault(element => HasLocalName(element, "QueryTimeStats"))),
            MemoryGrantInfoProperties: BuildAttributeProperties(queryPlanElement.Elements().FirstOrDefault(element => HasLocalName(element, "MemoryGrantInfo"))),
            OptimizerHardwareDependentProperties: BuildAttributeProperties(queryPlanElement.Elements().FirstOrDefault(element => HasLocalName(element, "OptimizerHardwareDependentProperties"))),
            OptimizerStatsUsageEntries: BuildOptimizerStatsUsageEntries(statementElement, queryPlanElement),
            MissingIndexesEntries: BuildMissingIndexesEntries(queryPlanElement),
            WaitStatsEntries: BuildWaitStatsEntries(queryPlanElement));

    private static IReadOnlyList<PlanWarning> ParseStatementWarnings(XElement statementElement, XElement queryPlanElement) =>
        ParseWarnings(statementElement)
            .Concat(ParseWarnings(queryPlanElement))
            .ToArray();

    private static PlanObjectReference? ParseObjectReference(XElement relOpElement)
    {
        var objectElement = GetOwnedDescendants(relOpElement, "Object").FirstOrDefault();
        if (objectElement is null)
        {
            return null;
        }

        return new PlanObjectReference(
            Database: GetAttribute(objectElement, "Database"),
            Schema: GetAttribute(objectElement, "Schema"),
            Table: GetAttribute(objectElement, "Table"),
            Index: GetAttribute(objectElement, "Index"),
            Alias: GetAttribute(objectElement, "Alias"),
            IndexKind: GetAttribute(objectElement, "IndexKind"),
            Storage: GetAttribute(objectElement, "Storage"));
    }

    private static PlanRuntimeMetrics ParseRuntimeMetrics(XElement relOpElement)
    {
        var counters = GetOwnedDescendants(relOpElement, "RunTimeCountersPerThread").ToArray();
        if (counters.Length == 0)
        {
            return new PlanRuntimeMetrics(null, null, null, null, null, null, null, null);
        }

        return new PlanRuntimeMetrics(
            ActualRows: SumAttributes(counters, "ActualRows"),
            ActualExecutions: SumAttributes(counters, "ActualExecutions"),
            ActualLogicalReads: SumAttributes(counters, "ActualLogicalReads"),
            ActualPhysicalReads: SumAttributes(counters, "ActualPhysicalReads"),
            ActualCpuMs: SumAttributes(counters, "ActualCPUms", "ActualCpuMs"),
            ActualElapsedMs: SumAttributes(counters, "ActualElapsedms", "ActualElapsedMs"),
            ActualRebinds: SumAttributes(counters, "ActualRebinds"),
            ActualRewinds: SumAttributes(counters, "ActualRewinds"));
    }

    private static IReadOnlyList<PlanWarning> ParseWarnings(XElement ownerElement)
    {
        var warningsElement = ownerElement.Elements().FirstOrDefault(element => HasLocalName(element, "Warnings"));
        if (warningsElement is null)
        {
            return Array.Empty<PlanWarning>();
        }

        var warnings = new List<PlanWarning>();

        warnings.AddRange(
            warningsElement.Attributes().Select(attribute =>
                new PlanWarning(attribute.Name.LocalName, attribute.Value, null)));

        warnings.AddRange(
            warningsElement.Elements().Select(element =>
                new PlanWarning(
                    element.Name.LocalName,
                    element.Attributes().FirstOrDefault()?.Value,
                    BuildDetails(element))));

        return warnings;
    }

    private static IReadOnlyList<PlanProperty> BuildProperties(
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
        AddProperty(properties, "ActualExecutionMode", GetAttribute(relOpElement, "ActualExecutionMode"));
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
            .SelectMany(outputList => outputList.Descendants().Where(element => HasLocalName(element, "ColumnReference")))
            .Select(FormatColumnReference)
            .Where(value => !string.IsNullOrWhiteSpace(value))
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

    private static string BuildDetails(XElement element)
    {
        var attributeText = string.Join(
            ", ",
            element.Attributes().Select(attribute => $"{attribute.Name.LocalName}={attribute.Value}"));

        if (!string.IsNullOrWhiteSpace(attributeText))
        {
            return attributeText;
        }

        return string.IsNullOrWhiteSpace(element.Value) ? "true" : element.Value.Trim();
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
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>());
        }

        conditions.AddRange(BuildJoinColumnPairs(relOpElement, "OuterSideJoinColumns", "InnerSideJoinColumns"));
        conditions.AddRange(BuildJoinColumnPairs(relOpElement, "HashKeysBuild", "HashKeysProbe"));

        var outerReferences = GetOwnedDescendants(relOpElement, "OuterReferences")
            .SelectMany(referenceElement => referenceElement.Descendants().Where(element => HasLocalName(element, "ColumnReference")))
            .Select(FormatColumnReference)
            .Where(value => !string.IsNullOrWhiteSpace(value))
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

    private static string? BuildSeekPredicateText(XElement relOpElement)
    {
        var segments = new List<string>();

        foreach (var rangeElementName in new[] { "Prefix", "StartRange", "EndRange" })
        {
            foreach (var rangeElement in GetOwnedDescendants(relOpElement, rangeElementName))
            {
                var columns = rangeElement.Elements()
                    .FirstOrDefault(element => HasLocalName(element, "RangeColumns"))
                    ?.Descendants()
                    .Where(element => HasLocalName(element, "ColumnReference"))
                    .Select(FormatColumnReference)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToArray()
                    ?? Array.Empty<string>();

                var expressions = rangeElement.Elements()
                    .FirstOrDefault(element => HasLocalName(element, "RangeExpressions"))
                    ?.Descendants()
                    .Where(element => HasLocalName(element, "ScalarOperator"))
                    .Select(scalarElement => GetAttribute(scalarElement, "ScalarString"))
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Cast<string>()
                    .ToArray()
                    ?? Array.Empty<string>();

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
                var column = orderByColumnElement.Descendants()
                    .FirstOrDefault(element => HasLocalName(element, "ColumnReference"));
                var columnName = column is null ? null : FormatColumnReference(column);
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
            .SelectMany(element => element.Descendants().Where(column => HasLocalName(column, "ColumnReference")))
            .Select(FormatColumnReference)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return columns.Length == 0 ? null : string.Join(", ", columns);
    }

    private static string? BuildDefinedValuesText(XElement relOpElement, string physicalOp)
    {
        var values = GetOwnedDescendants(relOpElement, "DefinedValue")
            .Select(definedValueElement =>
            {
                var targetColumn = definedValueElement.Descendants()
                    .FirstOrDefault(element => HasLocalName(element, "ColumnReference"));
                var target = targetColumn is null ? null : FormatColumnReference(targetColumn);
                var scalarString = definedValueElement.Descendants()
                    .Where(element => HasLocalName(element, "ScalarOperator"))
                    .Select(scalarElement => GetAttribute(scalarElement, "ScalarString"))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

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

            var attributeText = string.Join(
                ", ",
                element.Attributes().Select(attribute => $"{attribute.Name.LocalName}={attribute.Value}"));

            if (string.IsNullOrWhiteSpace(attributeText))
            {
                continue;
            }

            yield return new PlanProperty(element.Name.LocalName, attributeText);
        }
    }

    private static IReadOnlyList<PlanProperty> BuildXmlAttributeProperties(XElement relOpElement)
    {
        var properties = new List<PlanProperty>();
        TraverseOwnedElements(relOpElement, relOpElement.Name.LocalName);
        return properties;

        void TraverseOwnedElements(XElement element, string path)
        {
            if (ShouldExcludeXmlAttributePath(path))
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
        ExcludedXmlAttributePathPatterns.Any(pattern => XmlAttributePathMatchesPattern(path, pattern));

    private static bool XmlAttributePathMatchesPattern(string path, string pattern)
    {
        var pathSegments = path.Split('.');
        var patternSegments = pattern.Split('.');
        return XmlAttributePathMatchesPattern(pathSegments, 0, patternSegments, 0);
    }

    private static bool XmlAttributePathMatchesPattern(
        IReadOnlyList<string> pathSegments,
        int pathIndex,
        IReadOnlyList<string> patternSegments,
        int patternIndex)
    {
        if (patternIndex >= patternSegments.Count)
        {
            return true;
        }

        if (pathIndex >= pathSegments.Count)
        {
            return patternSegments.Skip(patternIndex).All(segment => segment == "**");
        }

        var patternSegment = patternSegments[patternIndex];
        if (patternSegment == "**")
        {
            if (patternIndex == patternSegments.Count - 1)
            {
                return true;
            }

            for (var nextPathIndex = pathIndex; nextPathIndex <= pathSegments.Count; nextPathIndex++)
            {
                if (XmlAttributePathMatchesPattern(pathSegments, nextPathIndex, patternSegments, patternIndex + 1))
                {
                    return true;
                }
            }

            return false;
        }

        if (patternSegment != "*"
            && !NormalizeXmlAttributePathSegment(pathSegments[pathIndex]).StartsWith(patternSegment, StringComparison.Ordinal))
        {
            return false;
        }

        return XmlAttributePathMatchesPattern(pathSegments, pathIndex + 1, patternSegments, patternIndex + 1);
    }

    private static string NormalizeXmlAttributePathSegment(string segment)
    {
        var bracketIndex = segment.IndexOf('[');
        return bracketIndex >= 0
            ? segment[..bracketIndex]
            : segment;
    }

    private static IEnumerable<string> BuildJoinColumnPairs(XElement relOpElement, string leftElementName, string rightElementName)
    {
        var leftColumns = GetOwnedDescendants(relOpElement, leftElementName)
            .SelectMany(element => element.Descendants().Where(column => HasLocalName(column, "ColumnReference")))
            .Select(FormatColumnReference)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        var rightColumns = GetOwnedDescendants(relOpElement, rightElementName)
            .SelectMany(element => element.Descendants().Where(column => HasLocalName(column, "ColumnReference")))
            .Select(FormatColumnReference)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (leftColumns.Length > 0 && leftColumns.Length == rightColumns.Length)
        {
            for (var index = 0; index < leftColumns.Length; index++)
            {
                yield return $"{leftColumns[index]} = {rightColumns[index]}";
            }

            yield break;
        }

        leftColumns = GetOwnedDescendants(relOpElement, leftElementName)
            .SelectMany(element => element.Descendants().Where(scalar => HasLocalName(scalar, "ScalarOperator")))
            .Select(scalarElement => GetAttribute(scalarElement, "ScalarString"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

        rightColumns = GetOwnedDescendants(relOpElement, rightElementName)
            .SelectMany(element => element.Descendants().Where(scalar => HasLocalName(scalar, "ScalarOperator")))
            .Select(scalarElement => GetAttribute(scalarElement, "ScalarString"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToArray();

        if (leftColumns.Length > 0 && leftColumns.Length == rightColumns.Length)
        {
            for (var index = 0; index < leftColumns.Length; index++)
            {
                yield return $"{leftColumns[index]} = {rightColumns[index]}";
            }
        }
    }

    private static string ExtractScalarStringOrDetails(XElement element)
    {
        var scalarString = element.Descendants()
            .FirstOrDefault(descendant => HasLocalName(descendant, "ScalarOperator"))
            ?.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, "ScalarString", StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return !string.IsNullOrWhiteSpace(scalarString)
            ? scalarString
            : BuildDetails(element);
    }

    private static string FormatColumnReference(XElement columnReferenceElement)
    {
        var alias = GetAttribute(columnReferenceElement, "Alias");
        var table = GetAttribute(columnReferenceElement, "Table");
        var column = GetAttribute(columnReferenceElement, "Column");

        var prefix = alias ?? table;
        if (!string.IsNullOrWhiteSpace(prefix) && !string.IsNullOrWhiteSpace(column))
        {
            return $"{prefix}.{column}";
        }

        return column ?? string.Empty;
    }

    private static IReadOnlyList<PlanProperty> BuildAttributeProperties(XElement? element)
    {
        if (element is null)
        {
            return Array.Empty<PlanProperty>();
        }

        return element.Attributes()
            .Select(attribute => new PlanProperty(attribute.Name.LocalName, attribute.Value))
            .ToArray();
    }

    private static IReadOnlyList<MissingIndexEntry> BuildMissingIndexesEntries(XElement queryPlanElement)
    {
        var missingIndexesElement = queryPlanElement.Elements().FirstOrDefault(element => HasLocalName(element, "MissingIndexes"));
        if (missingIndexesElement is null)
        {
            return Array.Empty<MissingIndexEntry>();
        }

        var entries = new List<MissingIndexEntry>();
        var groupIndex = 1;

        foreach (var groupElement in missingIndexesElement.Elements().Where(element => HasLocalName(element, "MissingIndexGroup")))
        {
            var impact = FormatNumericText(GetAttribute(groupElement, "Impact"));
            var missingIndexElements = groupElement.Elements().Where(element => HasLocalName(element, "MissingIndex")).ToArray();
            if (missingIndexElements.Length == 0)
            {
                continue;
            }

            foreach (var missingIndexElement in missingIndexElements)
            {
                var label = BuildMissingIndexLabel(missingIndexElement, groupIndex);
                string? equalityColumns = null;
                string? inequalityColumns = null;
                string? includeColumns = null;

                foreach (var columnGroupElement in missingIndexElement.Elements().Where(element => HasLocalName(element, "ColumnGroup")))
                {
                    var usage = GetAttribute(columnGroupElement, "Usage") ?? "Columns";
                    var columns = columnGroupElement.Elements()
                        .Where(element => HasLocalName(element, "Column"))
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
            statementElement.Elements().FirstOrDefault(element => HasLocalName(element, "OptimizerStatsUsage"))
            ?? queryPlanElement.Elements().FirstOrDefault(element => HasLocalName(element, "OptimizerStatsUsage"));

        if (optimizerStatsUsageElement is null)
        {
            return Array.Empty<OptimizerStatsUsageEntry>();
        }

        return optimizerStatsUsageElement.Elements()
            .Where(element => HasLocalName(element, "StatisticsInfo"))
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
        var waitStatsElement = queryPlanElement.Elements().FirstOrDefault(element => HasLocalName(element, "WaitStats"));
        if (waitStatsElement is null)
        {
            return Array.Empty<WaitStatEntry>();
        }

        var entries = new List<WaitStatEntry>();
        var waitIndex = 1;

        foreach (var waitElement in waitStatsElement.Elements().Where(element => HasLocalName(element, "Wait")))
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

    private static IEnumerable<XElement> GetOwnedDescendants(XElement ownerRelOp, string localName) =>
        ownerRelOp
            .Descendants()
            .Where(element =>
                HasLocalName(element, localName) &&
                ReferenceEquals(GetNearestAncestorByLocalName(element, "RelOp"), ownerRelOp));

    private static XElement? GetNearestAncestorByLocalName(XElement element, string localName)
    {
        var current = element.Parent;
        while (current is not null)
        {
            if (HasLocalName(current, localName))
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool IsStatementElement(XElement element) =>
        element.Name.LocalName.StartsWith("Stmt", StringComparison.Ordinal);

    private static bool HasLocalName(XElement element, string localName) =>
        string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal);

    private static string? GetAttribute(XElement element, string name) =>
        element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static decimal? GetDecimalAttribute(XElement element, string name) =>
        decimal.TryParse(GetAttribute(element, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static double? GetDoubleAttribute(XElement element, string name) =>
        double.TryParse(GetAttribute(element, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static int? GetIntAttribute(XElement element, string name) =>
        int.TryParse(GetAttribute(element, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static double? SumAttributes(IEnumerable<XElement> elements, params string[] names)
    {
        double total = 0;
        var found = false;

        foreach (var element in elements)
        {
            foreach (var name in names)
            {
                if (double.TryParse(GetAttribute(element, name), NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
                {
                    total += value;
                    found = true;
                    break;
                }
            }
        }

        return found ? total : null;
    }
}

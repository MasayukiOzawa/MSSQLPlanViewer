using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using MSSQLPlanViewer.Core.Models;
using static MSSQLPlanViewer.Core.Parsing.ShowplanXml;

namespace MSSQLPlanViewer.Core.Parsing;

public sealed class ShowplanParser : IShowplanParser
{
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
            document = ShowplanXmlDocumentLoader.Load(xml, MaxXmlInputLength);
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
            ShowplanSchemaVersionResolver.Resolve(namespaceUri),
            GetAttribute(root, "Version"),
            GetAttribute(root, "Build"));

        var statements = ParseStatements(root);
        if (statements.Count == 0)
        {
            throw new ShowplanParseException("No statement containing QueryPlan was found.");
        }

        return new ShowplanDocument(metadata, statements);
    }

    private static IReadOnlyList<StatementPlan> ParseStatements(XElement root)
    {
        var statements = new List<StatementPlan>();
        ParseStatementElements(BuildStatementElementContexts(root), statements);

        return statements;
    }

    private static IReadOnlyList<StatementElementContext> BuildStatementElementContexts(XElement root)
    {
        var batches = root
            .Descendants()
            .Where(element => HasLocalName(element, "Batch"))
            .ToArray();

        if (batches.Length == 0)
        {
            return root
                .Descendants()
                .Where(IsStatementElement)
                .Select(statement => new StatementElementContext(statement, XmlBatchNumber: 1))
                .ToArray();
        }

        var contexts = new List<StatementElementContext>();
        for (var index = 0; index < batches.Length; index++)
        {
            contexts.AddRange(
                batches[index]
                    .Descendants()
                    .Where(IsStatementElement)
                    .Select(statement => new StatementElementContext(statement, index + 1)));
        }

        return contexts;
    }

    private static void ParseStatementElements(
        IEnumerable<StatementElementContext> statementContexts,
        ICollection<StatementPlan> statements)
    {
        var logicalBatchNumber = 0;
        int? previousStatementId = null;
        int? previousXmlBatchNumber = null;

        foreach (var context in statementContexts)
        {
            var statementElement = context.StatementElement;
            var queryPlan = GetChild(statementElement, "QueryPlan");
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

            var statementElementName = statementElement.Name.LocalName;
            var currentStatementId = GetIntAttribute(statementElement, "StatementId");
            if (ShouldStartNewLogicalBatch(statements.Count, previousStatementId, currentStatementId, previousXmlBatchNumber, context.XmlBatchNumber))
            {
                logicalBatchNumber++;
            }

            previousStatementId = currentStatementId;
            previousXmlBatchNumber = context.XmlBatchNumber;

            statements.Add(
                new StatementPlan(
                    StatementId: GetAttribute(statementElement, "StatementId") ?? statements.Count.ToString(CultureInfo.InvariantCulture),
                    StatementType: GetAttribute(statementElement, "StatementType") ?? statementElementName,
                    StatementText: GetAttribute(statementElement, "StatementText") ?? "(statement text unavailable)",
                    Summary: StatementSummaryParser.ParseSummary(statementElement, queryPlan, rootRelOps, nodes),
                    Nodes: nodes,
                    Edges: edges,
                    Warnings: ParseStatementWarnings(statementElement, queryPlan),
                    RootNodeIds: rootRelOps
                        .Select(rootRelOp => GetAttribute(rootRelOp, "NodeId"))
                        .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
                        .Cast<string>()
                        .Distinct(StringComparer.Ordinal)
                        .ToArray())
                {
                    BatchNumber = logicalBatchNumber,
                    StatementOrdinal = statements.Count + 1,
                    StatementElementName = statementElementName,
                    StatementProperties = BuildAttributeProperties(statementElement),
                    StatementSetOptionsProperties = BuildAttributeProperties(GetChild(statementElement, "StatementSetOptions"))
                });
        }
    }

    private static bool ShouldStartNewLogicalBatch(
        int parsedStatementCount,
        int? previousStatementId,
        int? currentStatementId,
        int? previousXmlBatchNumber,
        int currentXmlBatchNumber)
    {
        if (parsedStatementCount == 0)
        {
            return true;
        }

        if (previousStatementId.HasValue && currentStatementId.HasValue)
        {
            return currentStatementId.Value <= previousStatementId.Value;
        }

        return !previousXmlBatchNumber.HasValue || currentXmlBatchNumber != previousXmlBatchNumber.Value;
    }

    private sealed record StatementElementContext(XElement StatementElement, int XmlBatchNumber);

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
        var parallelAttribute = GetAttribute(relOpElement, "Parallel");

        var node = new PlanNode(
            NodeId: nodeId,
            PhysicalOp: physicalOp,
            LogicalOp: logicalOp,
            EstimatedSubtreeCost: GetDecimalAttribute(relOpElement, "EstimatedTotalSubtreeCost"),
            EstimatedCpuCost: GetDecimalAttribute(relOpElement, "EstimateCPU"),
            EstimatedIoCost: GetDecimalAttribute(relOpElement, "EstimateIO"),
            EstimatedRows: GetDoubleAttribute(relOpElement, "EstimateRows"),
            AverageRowSize: GetDoubleAttribute(relOpElement, "AvgRowSize"),
            IsParallel: string.Equals(parallelAttribute, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(parallelAttribute, "1", StringComparison.OrdinalIgnoreCase),
            ObjectReference: objectReference,
            RuntimeMetrics: runtimeMetrics,
            Warnings: warnings,
            Properties: PlanNodePropertyBuilder.Build(relOpElement, physicalOp, logicalOp, objectReference, runtimeMetrics, warnings),
            XmlAttributes: PlanNodePropertyBuilder.BuildXmlAttributeProperties(relOpElement, excludeConfiguredSubtrees: true),
            DetailXmlAttributes: PlanNodePropertyBuilder.BuildXmlAttributeProperties(relOpElement, excludeConfiguredSubtrees: false));

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
            ActualRewinds: SumAttributes(counters, "ActualRewinds"))
        {
            ActualExecutionMode = ResolveRuntimeActualExecutionMode(counters),
            Threads = counters
                .Select(BuildThreadRuntimeMetrics)
                .Where(metric => metric is not null)
                .Cast<PlanThreadRuntimeMetrics>()
                .OrderBy(metric => metric.ThreadId)
                .ToArray()
        };
    }

    private static PlanThreadRuntimeMetrics? BuildThreadRuntimeMetrics(XElement counterElement)
    {
        var threadId = GetIntAttribute(counterElement, "Thread");
        if (!threadId.HasValue)
        {
            return null;
        }

        return new PlanThreadRuntimeMetrics(
            ThreadId: threadId.Value,
            ActualRows: GetFirstDoubleAttribute(counterElement, "ActualRows"),
            ActualRowsRead: GetFirstDoubleAttribute(counterElement, "ActualRowsRead"),
            ActualExecutions: GetFirstDoubleAttribute(counterElement, "ActualExecutions"),
            ActualLogicalReads: GetFirstDoubleAttribute(counterElement, "ActualLogicalReads"),
            ActualPhysicalReads: GetFirstDoubleAttribute(counterElement, "ActualPhysicalReads"),
            ActualCpuMs: GetFirstDoubleAttribute(counterElement, "ActualCPUms", "ActualCpuMs"),
            ActualElapsedMs: GetFirstDoubleAttribute(counterElement, "ActualElapsedms", "ActualElapsedMs"),
            ActualRebinds: GetFirstDoubleAttribute(counterElement, "ActualRebinds"),
            ActualRewinds: GetFirstDoubleAttribute(counterElement, "ActualRewinds"))
        {
            ActualExecutionMode = GetAttribute(counterElement, "ActualExecutionMode")
        };
    }

    private static string? ResolveRuntimeActualExecutionMode(IReadOnlyList<XElement> counters)
    {
        if (counters.Count > 1)
        {
            var threadOneMode = counters
                .Where(counter => GetIntAttribute(counter, "Thread") == 1)
                .Select(counter => GetAttribute(counter, "ActualExecutionMode"))
                .FirstOrDefault(mode => !string.IsNullOrWhiteSpace(mode));

            if (!string.IsNullOrWhiteSpace(threadOneMode))
            {
                return threadOneMode;
            }
        }

        return counters
            .Select(counter => GetAttribute(counter, "ActualExecutionMode"))
            .FirstOrDefault(mode => !string.IsNullOrWhiteSpace(mode));
    }

    private static IReadOnlyList<PlanWarning> ParseStatementWarnings(XElement statementElement, XElement queryPlanElement) =>
        ParseWarnings(statementElement)
            .Concat(ParseWarnings(queryPlanElement))
            .ToArray();

    private static IReadOnlyList<PlanWarning> ParseWarnings(XElement ownerElement)
    {
        var warningsElement = GetChild(ownerElement, "Warnings");
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

    private static bool IsStatementElement(XElement element) =>
        element.Name.LocalName.StartsWith("Stmt", StringComparison.Ordinal);
}

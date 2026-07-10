using System.Globalization;
using System.IO;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using MSSQLPlanViewer.Core.Comparison;
using MSSQLPlanViewer.Core.Formatting;
using MSSQLPlanViewer.Core.Models;
using MSSQLPlanViewer.Core.Parsing;
using MSSQLPlanViewer.Core.Rendering;
using MSSQLPlanViewer.Web.Showplans;
using MSSQLPlanViewer.Web.State;

namespace MSSQLPlanViewer.Web.Components.Pages;

public partial class Home
{
    private PlanInputMode ActiveInputMode { get; set; } = PlanInputMode.ShowplanXml;

    private string XmlInput { get; set; } = string.Empty;

    private string QueryConnectionString { get; set; } = string.Empty;

    private string QueryInput { get; set; } = string.Empty;

    private string QueryPlanLabel { get; set; } = string.Empty;

    private int QueryCommandTimeoutSeconds { get; set; } = SqlEstimatedShowplanProvider.DefaultCommandTimeoutSeconds;

    private bool ShowConnectionString { get; set; }

    private bool IsFetchingEstimatedPlan { get; set; }

    private int GraphCostThresholdPercent { get; set; } = 20;

    private bool ShowCriticalPath { get; set; } = true;

    private GraphLayoutDirection CurrentGraphLayoutDirection { get; set; } = GraphLayoutDirection.HorizontalSsms;

    private AccessSummaryTab SelectedAccessSummaryTab { get; set; } = AccessSummaryTab.AccessedObjects;

    private OptimizationSummaryTab SelectedOptimizationSummaryTab { get; set; } = OptimizationSummaryTab.OptimizerStatsUsage;

    private string? ParseError { get; set; }

    private string? EstimatedPlanError { get; set; }

    private int TableScrollRequestVersion { get; set; }

    private int TableExpandRequestVersion { get; set; }

    private int PastedPlanCounter { get; set; }

    private int EstimatedPlanCounter { get; set; }

    private readonly List<LoadedPlan> Plans = new();

    private Guid? ActivePlanId { get; set; }

    private Guid? ComparePlanAId { get; set; }

    private Guid? ComparePlanBId { get; set; }

    private readonly List<InputMessage> FileLoadMessages = new();

    private readonly List<InputMessage> EstimatedPlanMessages = new();

    private string? TableActionMessage { get; set; }

    private bool IsPlanDetailsStatementTextExpanded { get; set; }

    private ElementReference graphPanelRef;

    private ElementReference graphPanelResizeHandleRef;

    private ElementReference tablePanelRef;

    private ElementReference tablePanelResizeHandleRef;

    private ElementReference detailsPaneRef;

    private ElementReference detailsResizeHandleRef;

    private ElementReference fileDropzoneRef;

    private LoadedPlan? ActivePlan => Plans.FirstOrDefault(plan => plan.Id == ActivePlanId);

    private ShowplanDocument? Document => ActivePlan?.Document;

    private string? SelectedStatementId
    {
        get => ActivePlan?.SelectedStatementId;
        set
        {
            if (ActivePlan is not null)
            {
                ActivePlan.SelectedStatementId = value;
            }
        }
    }

    private string? SelectedStatementKey
    {
        get => ActivePlan?.SelectedStatementKey;
        set
        {
            if (ActivePlan is not null)
            {
                ActivePlan.SelectedStatementKey = value;
            }
        }
    }

    private string? SelectedNodeId
    {
        get => ActivePlan?.SelectedNodeId;
        set
        {
            if (ActivePlan is not null)
            {
                ActivePlan.SelectedNodeId = value;
            }
        }
    }

    private string? HoveredNodeId
    {
        get => ActivePlan?.HoveredNodeId;
        set
        {
            if (ActivePlan is not null)
            {
                ActivePlan.HoveredNodeId = value;
            }
        }
    }

    private bool IsStatementDetailsSelected
    {
        get => ActivePlan?.IsStatementDetailsSelected == true;
        set
        {
            if (ActivePlan is not null)
            {
                ActivePlan.IsStatementDetailsSelected = value;
            }
        }
    }

    private string? SelectedStatementDetailId =>
        IsStatementDetailsSelected ? SelectedStatementId : null;

    private StatementGraphLayout? SelectedLayout
    {
        get => ActivePlan?.SelectedLayout;
        set
        {
            if (ActivePlan is not null)
            {
                ActivePlan.SelectedLayout = value;
            }
        }
    }

    private IReadOnlyList<PlanTableRow> CurrentRows => ActivePlan?.CurrentRows ?? Array.Empty<PlanTableRow>();

    private int HighlightedCostNodeCount =>
        SelectedLayout?.Nodes.Count(node => GraphCostEmphasis.IsEmphasized(
            GraphCostEmphasis.Resolve(node.CostRatio, GraphCostThresholdPercent))) ?? 0;

    private StatementPlan? SelectedStatement =>
        Document?.Statements.FirstOrDefault(statement => statement.StatementKey == SelectedStatementKey)
        ?? Document?.Statements.FirstOrDefault(statement => statement.StatementId == SelectedStatementId)
        ?? Document?.Statements.FirstOrDefault();

    private PlanNode? SelectedNode =>
        SelectedStatement?.Nodes.FirstOrDefault(node => node.NodeId == SelectedNodeId);

    private PlanNode? HoveredNode =>
        SelectedStatement?.Nodes.FirstOrDefault(node => node.NodeId == HoveredNodeId);

    private PlanNode? OverlayNode => HoveredNode ?? SelectedNode;

    private enum PlanInputMode
    {
        ShowplanXml,
        Query
    }

    private enum AccessSummaryTab
    {
        AccessedObjects,
        AccessedIndexes,
        ParameterList,
        MissingIndexes,
        SeekScanPredicates,
        ImplicitConversions
    }

    private enum OptimizationSummaryTab
    {
        OptimizerStatsUsage,
        WaitStats,
        WarningDetails
    }

    private sealed record InputMessage(string Text, bool IsError);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (SelectedStatement is not null)
        {
            await JS.InvokeVoidAsync(
                "mssqlPlanViewerGraphPan.initPanelResizer",
                graphPanelRef,
                graphPanelResizeHandleRef,
                BuildGraphPanelResizeOptions());
            await JS.InvokeVoidAsync(
                "mssqlPlanViewerGraphPan.initPanelResizer",
                tablePanelRef,
                tablePanelResizeHandleRef,
                BuildTablePanelResizeOptions());
            await JS.InvokeVoidAsync("mssqlPlanViewerGraphPan.initDetailsResizer", detailsPaneRef, detailsResizeHandleRef);
        }

        if (firstRender)
        {
            await JS.InvokeVoidAsync("mssqlPlanViewerDropzone.init", fileDropzoneRef);
        }
    }

    private static IReadOnlyList<PlanProperty> GetStatementSummaryItems(StatementPlan statement)
    {
        var items = new[]
        {
            new PlanProperty("Estimated subtree cost", FormatSummaryValue(PlanDisplayFormatter.FormatCost(statement.Summary.EstimatedSubtreeCost))),
            new PlanProperty("Estimated rows", FormatSummaryValue(PlanDisplayFormatter.FormatNumber(statement.Summary.EstimatedRows))),
            new PlanProperty("Cached plan size (KB)", FormatSummaryValue(FormatInt(statement.Summary.CachedPlanSizeKb))),
            new PlanProperty("Compile time (ms)", FormatSummaryValue(FormatInt(statement.Summary.CompileTimeMs))),
            new PlanProperty("Compile CPU (ms)", FormatSummaryValue(FormatInt(statement.Summary.CompileCpuMs))),
            new PlanProperty("Compile memory (KB)", FormatSummaryValue(FormatInt(statement.Summary.CompileMemoryKb))),
            new PlanProperty("Estimated memory grant (KB)", FormatSummaryValue(PlanDisplayFormatter.FormatNumber(statement.Summary.EstimatedMemoryGrantKb))),
            new PlanProperty("Estimated available memory grant (KB)", FormatSummaryValue(PlanDisplayFormatter.FormatNumber(statement.Summary.EstimatedAvailableMemoryGrantKb)))
        };

        return items
            .Where(item => !string.IsNullOrWhiteSpace(item.Value))
            .ToArray();
    }

    private static object BuildGraphPanelResizeOptions() => new
    {
        minHeightPx = 420,
        minWidthPx = 420,
        maxWidthMode = "viewport",
        resizeTrackSelector = ".viewer-grid",
        resizeTrackProperty = "--graph-panel-min-width",
        contentResizeSelector = ".graph-resize-frame",
        minContentHeightPx = 288,
        rightMarginPx = 32
    };

    private static object BuildTablePanelResizeOptions() => new
    {
        minHeightPx = 360,
        minWidthPx = 420,
        maxWidthMode = "viewport",
        resizeTrackSelector = ".viewer-grid",
        resizeTrackProperty = "--table-panel-min-width",
        contentResizeSelector = ".table-shell",
        minContentHeightPx = 256,
        rightMarginPx = 32
    };

    private static IReadOnlyList<PlanProperty> GetVisibleProperties(IReadOnlyList<PlanProperty> properties) =>
        properties
            .Select(property => new PlanProperty(property.Name, FormatSummaryValue(property.Value)))
            .Where(property => !string.IsNullOrWhiteSpace(property.Value))
            .ToArray();

    private static IReadOnlyList<WarningTableRow> GetWarningRows(StatementPlan statement)
    {
        var rows = new List<WarningTableRow>();

        rows.AddRange(statement.Warnings
            .Select(warning => new WarningTableRow(
                NodeId: null,
                Operator: "Statement",
                Warning: GetWarningName(warning),
                Details: BuildWarningDisplayText(warning))));

        rows.AddRange(statement.Nodes
            .Where(node => node.Warnings.Count > 0)
            .OrderBy(GetNodeSortKey)
            .ThenBy(node => node.NodeId, StringComparer.Ordinal)
            .SelectMany(node => node.Warnings.Select(warning => new WarningTableRow(
                node.NodeId,
                BuildWarningOperatorText(node),
                GetWarningName(warning),
                BuildWarningDisplayText(warning)))));

        return rows;
    }

    private static IReadOnlyList<SummaryTableRow> BuildCombinedPlanDetailRows(
        string statementElementName,
        IReadOnlyList<PlanProperty> statementItems,
        IReadOnlyList<PlanProperty> statementSetOptionsItems,
        IReadOnlyList<PlanProperty> queryTimeStatsItems,
        IReadOnlyList<PlanProperty> statementSummaryItems,
        IReadOnlyList<PlanProperty> queryPlanItems,
        IReadOnlyList<IReadOnlyList<PlanProperty>> threadStatItems,
        IReadOnlyList<PlanProperty> memoryGrantInfoItems,
        IReadOnlyList<PlanProperty> optimizerHardwareItems)
    {
        var rows = new List<SummaryTableRow>();

        AddSummaryRows(rows, string.IsNullOrWhiteSpace(statementElementName) ? "Statement" : statementElementName, statementItems);
        AddSummaryRows(rows, "StatementSetOptions", statementSetOptionsItems);
        AddSummaryRows(rows, "Statement Plan Summary", statementSummaryItems);
        AddSummaryRows(rows, "QueryPlan", queryPlanItems);
        AddSummaryRows(rows, "QueryTimeStats", queryTimeStatsItems);
        AddThreadStatRows(rows, threadStatItems);
        AddSummaryRows(rows, "MemoryGrantInfo", memoryGrantInfoItems);
        AddSummaryRows(rows, "OptimizerHardwareDependentProperties", optimizerHardwareItems);

        return rows;
    }

    private static void AddThreadStatRows(ICollection<SummaryTableRow> rows, IReadOnlyList<IReadOnlyList<PlanProperty>> threadStatItems)
    {
        for (var index = 0; index < threadStatItems.Count; index++)
        {
            var section = threadStatItems.Count == 1
                ? "ThreadStat"
                : $"ThreadStat #{(index + 1).ToString(CultureInfo.InvariantCulture)}";

            AddSummaryRows(rows, section, threadStatItems[index]);
        }
    }

    private static void AddSummaryRows(ICollection<SummaryTableRow> rows, string section, IReadOnlyList<PlanProperty> properties)
    {
        for (var index = 0; index < properties.Count; index++)
        {
            var property = properties[index];
            rows.Add(new SummaryTableRow(
                section,
                property.Name,
                property.Value,
                ShowSection: index == 0,
                SectionRowSpan: index == 0 ? properties.Count : 0));
        }
    }

    private static string BuildOptimizerStatsUsageTableName(OptimizerStatsUsageEntry item)
        => PlanDisplayFormatter.FormatQualifiedTableName(item.Database, item.Schema, item.Table);

    private static string BuildAccessedObjectName(AccessedObjectEntry item) =>
        PlanDisplayFormatter.FormatQualifiedTableName(item.Database, item.Schema, item.Table);

    private static string BuildAccessedIndexTableName(AccessedIndexEntry item) =>
        PlanDisplayFormatter.FormatQualifiedTableName(item.Database, item.Schema, item.Table);

    private static string BuildSeekScanPredicateTableName(SeekScanPredicateEntry item) =>
        PlanDisplayFormatter.FormatQualifiedTableName(item.Database, item.Schema, item.Table);

    private static string BuildImplicitConversionObjectName(ImplicitConversionEntry item) =>
        PlanDisplayFormatter.FormatObjectName(new PlanObjectReference(
            Database: item.Database,
            Schema: item.Schema,
            Table: item.Table,
            Index: item.Index,
            Alias: null,
            IndexKind: item.IndexKind,
            Storage: null));

    private AccessSummaryTab ResolveAccessSummaryTab(
        int accessedObjectCount,
        int accessedIndexCount,
        int missingIndexCount,
        int parameterListCount,
        int seekScanPredicateCount,
        int implicitConversionCount)
    {
        if (HasAccessSummaryTabItems(
            SelectedAccessSummaryTab,
            accessedObjectCount,
            accessedIndexCount,
            missingIndexCount,
            parameterListCount,
            seekScanPredicateCount,
            implicitConversionCount))
        {
            return SelectedAccessSummaryTab;
        }

        if (accessedObjectCount > 0)
        {
            return AccessSummaryTab.AccessedObjects;
        }

        if (accessedIndexCount > 0)
        {
            return AccessSummaryTab.AccessedIndexes;
        }

        if (parameterListCount > 0)
        {
            return AccessSummaryTab.ParameterList;
        }

        if (missingIndexCount > 0)
        {
            return AccessSummaryTab.MissingIndexes;
        }

        if (seekScanPredicateCount > 0)
        {
            return AccessSummaryTab.SeekScanPredicates;
        }

        if (implicitConversionCount > 0)
        {
            return AccessSummaryTab.ImplicitConversions;
        }

        return AccessSummaryTab.AccessedObjects;
    }

    private static bool HasAccessSummaryTabItems(
        AccessSummaryTab tab,
        int accessedObjectCount,
        int accessedIndexCount,
        int missingIndexCount,
        int parameterListCount,
        int seekScanPredicateCount,
        int implicitConversionCount) =>
        tab switch
        {
            AccessSummaryTab.AccessedObjects => accessedObjectCount > 0,
            AccessSummaryTab.AccessedIndexes => accessedIndexCount > 0,
            AccessSummaryTab.MissingIndexes => missingIndexCount > 0,
            AccessSummaryTab.ParameterList => parameterListCount > 0,
            AccessSummaryTab.SeekScanPredicates => seekScanPredicateCount > 0,
            AccessSummaryTab.ImplicitConversions => implicitConversionCount > 0,
            _ => false
        };

    private void SetAccessSummaryTab(AccessSummaryTab tab) =>
        SelectedAccessSummaryTab = tab;

    private static string GetAccessSummaryTabClass(AccessSummaryTab tab, AccessSummaryTab activeTab) =>
        tab == activeTab ? "access-tab-button is-active" : "access-tab-button";

    private OptimizationSummaryTab ResolveOptimizationSummaryTab(
        int optimizerStatsUsageCount,
        int waitStatsCount,
        int warningDetailsCount)
    {
        if (HasOptimizationSummaryTabItems(SelectedOptimizationSummaryTab, optimizerStatsUsageCount, waitStatsCount, warningDetailsCount))
        {
            return SelectedOptimizationSummaryTab;
        }

        if (optimizerStatsUsageCount > 0)
        {
            return OptimizationSummaryTab.OptimizerStatsUsage;
        }

        if (waitStatsCount > 0)
        {
            return OptimizationSummaryTab.WaitStats;
        }

        if (warningDetailsCount > 0)
        {
            return OptimizationSummaryTab.WarningDetails;
        }

        return OptimizationSummaryTab.OptimizerStatsUsage;
    }

    private static bool HasOptimizationSummaryTabItems(
        OptimizationSummaryTab tab,
        int optimizerStatsUsageCount,
        int waitStatsCount,
        int warningDetailsCount) =>
        tab switch
        {
            OptimizationSummaryTab.OptimizerStatsUsage => optimizerStatsUsageCount > 0,
            OptimizationSummaryTab.WaitStats => waitStatsCount > 0,
            OptimizationSummaryTab.WarningDetails => warningDetailsCount > 0,
            _ => false
        };

    private void SetOptimizationSummaryTab(OptimizationSummaryTab tab) =>
        SelectedOptimizationSummaryTab = tab;

    private static string GetOptimizationSummaryTabClass(OptimizationSummaryTab tab, OptimizationSummaryTab activeTab) =>
        tab == activeTab ? "access-tab-button is-active" : "access-tab-button";

    private static string BuildWarningDisplayText(PlanWarning warning)
    {
        var value = !string.IsNullOrWhiteSpace(warning.Details)
            ? warning.Details
            : !string.IsNullOrWhiteSpace(warning.Value)
                ? warning.Value!
                : "true";

        return FormatSummaryValue(value);
    }

    private static string BuildWarningOperatorText(PlanNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.PhysicalOp))
        {
            return FormatSummaryValue(node.PhysicalOp);
        }

        return FormatSummaryValue(node.LogicalOp);
    }

    private static string GetWarningName(PlanWarning warning) =>
        string.IsNullOrWhiteSpace(warning.Name) ? "Warning" : warning.Name;

    private static int GetNodeSortKey(PlanNode node) =>
        int.TryParse(node.NodeId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var nodeId)
            ? nodeId
            : int.MaxValue;

    private static string BuildStatementSelectionLabel(StatementPlan statement) =>
        $"Batch #{statement.BatchNumber.ToString(CultureInfo.InvariantCulture)} / #{statement.StatementId} - {statement.StatementType}";

    private static int GetBatchCount(ShowplanDocument document) =>
        document.Statements
            .Select(statement => statement.BatchNumber)
            .Distinct()
            .Count();

    private static string FormatSummaryValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "n/a", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integerValue))
        {
            return integerValue.ToString("N0", CultureInfo.InvariantCulture);
        }

        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
        {
            return decimalValue.ToString("#,0.###############", CultureInfo.InvariantCulture);
        }

        return value;
    }

    private void TogglePlanDetailsStatementText() =>
        IsPlanDetailsStatementTextExpanded = !IsPlanDetailsStatementTextExpanded;

    private string GetPlanDetailsStatementTextValue(string value) =>
        IsPlanDetailsStatementTextExpanded ? value : BuildPlanDetailsStatementTextPreview(value);

    private string GetPlanDetailsStatementTextClass() =>
        IsPlanDetailsStatementTextExpanded
            ? "plan-detail-statement-text is-expanded"
            : "plan-detail-statement-text";

    private static bool IsPlanDetailsStatementTextRow(SummaryTableRow row) =>
        string.Equals(row.Name, "StatementText", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlanDetailsStatementTextExpandable(string value) =>
        value.Length > PlanDetailsStatementTextPreviewLength
        || value.Contains('\r')
        || value.Contains('\n');

    private static string BuildPlanDetailsStatementTextPreview(string value)
    {
        var compact = string.Join(
            " ",
            value.Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        if (compact.Length <= PlanDetailsStatementTextPreviewLength)
        {
            return compact;
        }

        return $"{compact[..PlanDetailsStatementTextPreviewLength]}...";
    }

    private sealed record SummaryTableRow(
        string Section,
        string Name,
        string Value,
        bool ShowSection,
        int SectionRowSpan);

    private sealed record WarningTableRow(
        string? NodeId,
        string Operator,
        string Warning,
        string Details);

    private void ParsePlan()
    {
        ResetInputFeedback();

        try
        {
            var document = ShowplanParser.Parse(XmlInput);
            PastedPlanCounter++;
            AddPlan(document, $"Pasted #{PastedPlanCounter}");
        }
        catch (ShowplanParseException exception)
        {
            ParseError = exception.Message;
        }
        catch (Exception exception)
        {
            ParseError = $"An unexpected error occurred: {exception.Message}";
        }
    }

    private async Task HandleFilesSelected(InputFileChangeEventArgs args)
    {
        ResetInputFeedback();

        IReadOnlyList<IBrowserFile> files;
        try
        {
            files = args.GetMultipleFiles(maximumFileCount: MaxFileCount);
        }
        catch (InvalidOperationException)
        {
            FileLoadMessages.Add(new InputMessage($"Too many files selected; the maximum is {MaxFileCount}.", IsError: true));
            return;
        }

        foreach (var file in files)
        {
            await LoadFileAsync(file);
        }
    }

    private async Task LoadFileAsync(IBrowserFile file)
    {
        try
        {
            if (file.Size > MaxFileBytes)
            {
                FileLoadMessages.Add(new InputMessage($"{file.Name}: file is too large (max 10 MB).", IsError: true));
                return;
            }

            string xml;
            await using (var stream = file.OpenReadStream(MaxFileBytes))
            using (var memory = new MemoryStream())
            {
                await stream.CopyToAsync(memory);
                memory.Position = 0;
                using var reader = new StreamReader(memory, detectEncodingFromByteOrderMarks: true);
                xml = await reader.ReadToEndAsync();
            }

            XmlInput = xml;
            var document = ShowplanParser.Parse(xml);
            AddPlan(document, file.Name);
            FileLoadMessages.Add(new InputMessage($"{file.Name}: loaded.", IsError: false));
        }
        catch (ShowplanParseException exception)
        {
            FileLoadMessages.Add(new InputMessage($"{file.Name}: {exception.Message}", IsError: true));
        }
        catch (Exception exception)
        {
            FileLoadMessages.Add(new InputMessage($"{file.Name}: an unexpected error occurred.", IsError: true));
            Logger.LogError(exception, "Failed to load plan file {FileName}", file.Name);
        }
    }

    private void SetInputMode(PlanInputMode inputMode)
    {
        ActiveInputMode = inputMode;
        ResetInputFeedback();
    }

    private async Task FetchEstimatedPlan()
    {
        if (IsFetchingEstimatedPlan)
        {
            return;
        }

        ResetInputFeedback();

        if (string.IsNullOrWhiteSpace(QueryConnectionString))
        {
            EstimatedPlanError = "Enter a SQL Server connection string.";
            return;
        }

        if (string.IsNullOrWhiteSpace(QueryInput))
        {
            EstimatedPlanError = "Enter a query.";
            return;
        }

        if (QueryCommandTimeoutSeconds is < SqlEstimatedShowplanProvider.MinCommandTimeoutSeconds
            or > SqlEstimatedShowplanProvider.MaxCommandTimeoutSeconds)
        {
            EstimatedPlanError = $"Timeout must be between {SqlEstimatedShowplanProvider.MinCommandTimeoutSeconds} and {SqlEstimatedShowplanProvider.MaxCommandTimeoutSeconds} seconds.";
            return;
        }

        IsFetchingEstimatedPlan = true;
        try
        {
            var showplans = await EstimatedShowplanProvider.GetEstimatedShowplansAsync(
                new EstimatedShowplanRequest(
                    QueryConnectionString,
                    QueryInput,
                    QueryCommandTimeoutSeconds));

            EstimatedPlanCounter++;
            var baseLabel = string.IsNullOrWhiteSpace(QueryPlanLabel)
                ? $"Estimated #{EstimatedPlanCounter.ToString(CultureInfo.InvariantCulture)}"
                : QueryPlanLabel.Trim();

            foreach (var showplan in showplans)
            {
                var document = ShowplanParser.Parse(showplan.Xml);
                AddPlan(document, BuildEstimatedPlanLabel(baseLabel, showplan.Ordinal, showplans.Count));
            }

            EstimatedPlanMessages.Add(new InputMessage(FormatEstimatedPlanLoadedMessage(showplans.Count), IsError: false));
        }
        catch (EstimatedShowplanException exception)
        {
            EstimatedPlanError = exception.Message;
            Logger.LogWarning(exception, "Failed to retrieve estimated showplan XML.");
        }
        catch (ShowplanParseException exception)
        {
            EstimatedPlanError = $"SQL Server returned Showplan XML, but it could not be parsed: {exception.Message}";
        }
        catch (Exception exception)
        {
            EstimatedPlanError = "An unexpected error occurred while retrieving the estimated execution plan.";
            Logger.LogError(exception, "Unexpected failure while retrieving estimated showplan XML.");
        }
        finally
        {
            IsFetchingEstimatedPlan = false;
        }
    }

    private void ResetInputFeedback()
    {
        ParseError = null;
        EstimatedPlanError = null;
        FileLoadMessages.Clear();
        EstimatedPlanMessages.Clear();
    }

    private void ResetPlanViewState()
    {
        SelectedAccessSummaryTab = AccessSummaryTab.AccessedObjects;
        SelectedOptimizationSummaryTab = OptimizationSummaryTab.OptimizerStatsUsage;
        IsPlanDetailsStatementTextExpanded = false;
    }

    private void AddPlan(ShowplanDocument document, string label)
    {
        var plan = PlanWorkspace.CreateLoadedPlan(document, label, CurrentGraphLayoutDirection);
        Plans.Add(plan);
        ActivePlanId = plan.Id;
        ResetPlanViewState();
        EnsureCompareSelection();
    }

    private void SelectPlan(Guid planId)
    {
        if (Plans.Any(plan => plan.Id == planId))
        {
            ActivePlanId = planId;
            TableActionMessage = null;
            ResetPlanViewState();
            RefreshSelectedLayout();
        }
    }

    private void ClosePlan(Guid planId)
    {
        var index = Plans.FindIndex(plan => plan.Id == planId);
        if (index < 0)
        {
            return;
        }

        var wasActive = ActivePlanId == planId;
        Plans.RemoveAt(index);

        if (wasActive)
        {
            ActivePlanId = Plans.Count == 0
                ? null
                : Plans[Math.Min(index, Plans.Count - 1)].Id;
        }

        EnsureCompareSelection();
    }

    private void EnsureCompareSelection()
    {
        var selection = PlanWorkspace.EnsureCompareSelection(Plans, ComparePlanAId, ComparePlanBId);
        ComparePlanAId = selection.PlanAId;
        ComparePlanBId = selection.PlanBId;
    }

    private void OnComparePlanAChanged(ChangeEventArgs args)
    {
        if (Guid.TryParse(args.Value?.ToString(), out var id))
        {
            ComparePlanAId = id;
        }
    }

    private void OnComparePlanBChanged(ChangeEventArgs args)
    {
        if (Guid.TryParse(args.Value?.ToString(), out var id))
        {
            ComparePlanBId = id;
        }
    }

    private PlanComparisonResult? BuildComparison()
    {
        var planA = Plans.FirstOrDefault(plan => plan.Id == ComparePlanAId);
        var planB = Plans.FirstOrDefault(plan => plan.Id == ComparePlanBId);

        if (planA is null || planB is null)
        {
            return null;
        }

        return PlanComparisonService.Compare(planA.Document, planB.Document);
    }

    private async Task DownloadTableCsv()
    {
        if (CurrentRows.Count == 0)
        {
            return;
        }

        TableActionMessage = null;
        var csv = PlanTableCsvExporter.ToCsv(CurrentRows);
        await JS.InvokeVoidAsync("mssqlPlanViewerExport.downloadText", BuildExportFileName("csv"), "text/csv", csv);
    }

    private async Task DownloadTableMarkdown()
    {
        if (CurrentRows.Count == 0)
        {
            return;
        }

        TableActionMessage = null;
        var markdown = PlanTableMarkdownExporter.ToMarkdown(CurrentRows);
        await JS.InvokeVoidAsync("mssqlPlanViewerExport.downloadText", BuildExportFileName("md"), "text/markdown", markdown);
    }

    private async Task DownloadTableJson()
    {
        if (CurrentRows.Count == 0)
        {
            return;
        }

        TableActionMessage = null;
        var json = PlanTableJsonExporter.ToJson(CurrentRows);
        await JS.InvokeVoidAsync("mssqlPlanViewerExport.downloadText", BuildExportFileName("json"), "application/json", json);
    }

    private async Task CopyTableCsv()
    {
        if (CurrentRows.Count == 0)
        {
            return;
        }

        var csv = PlanTableCsvExporter.ToCsv(CurrentRows);
        var copied = await JS.InvokeAsync<bool>("mssqlPlanViewerExport.copyText", csv);
        TableActionMessage = copied
            ? "Table copied to the clipboard."
            : "Unable to copy to the clipboard. Use Download CSV instead.";
    }

    private async Task ResetGraphPanelSizeAsync() =>
        await JS.InvokeVoidAsync(
            "mssqlPlanViewerGraphPan.resetPanelResizer",
            graphPanelRef,
            BuildGraphPanelResizeOptions());

    private async Task ResetTablePanelSizeAsync() =>
        await JS.InvokeVoidAsync(
            "mssqlPlanViewerGraphPan.resetPanelResizer",
            tablePanelRef,
            BuildTablePanelResizeOptions());

    private string BuildExportBaseName()
    {
        var label = ActivePlan?.Label ?? "plan";
        return PlanFileNameBuilder.BuildBaseName(label, SelectedStatement?.StatementId, "plan");
    }

    private string BuildExportFileName(string extension) => $"{BuildExportBaseName()}.{extension}";

    private static string FormatMetricValue(double? value, bool isInteger)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return isInteger
            ? ((long)Math.Round(value.Value)).ToString("N0", CultureInfo.InvariantCulture)
            : value.Value.ToString("#,0.######", CultureInfo.InvariantCulture);
    }

    private static string FormatMetricDelta(double? delta, bool isInteger)
    {
        if (delta is null)
        {
            return string.Empty;
        }

        var sign = delta.Value > 0 ? "+" : string.Empty;
        return sign + FormatMetricValue(delta.Value, isInteger);
    }

    private static string FormatMetricPercent(double? percent)
    {
        if (percent is null)
        {
            return string.Empty;
        }

        var sign = percent.Value > 0 ? "+" : string.Empty;
        return sign + percent.Value.ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private static string? GetDeltaClass(double? delta)
    {
        if (delta is null || delta.Value == 0d)
        {
            return null;
        }

        return delta.Value > 0d ? "delta-up" : "delta-down";
    }

    private void CloseOverlay()
    {
        HoveredNodeId = null;
        SelectedNodeId = null;
        IsStatementDetailsSelected = false;
    }

    private void OnStatementChanged(ChangeEventArgs args)
    {
        var statementKey = args.Value?.ToString();
        if (string.IsNullOrWhiteSpace(statementKey))
        {
            return;
        }

        SelectStatementByKey(statementKey);
    }

    private void SelectStatement(string statementId)
    {
        var plan = ActivePlan;
        if (plan is null)
        {
            return;
        }

        TableActionMessage = null;
        ResetPlanViewState();
        PlanWorkspace.SelectStatement(plan, statementId, CurrentGraphLayoutDirection);
    }

    private void SelectStatementByKey(string statementKey)
    {
        var plan = ActivePlan;
        if (plan is null)
        {
            return;
        }

        TableActionMessage = null;
        ResetPlanViewState();
        PlanWorkspace.SelectStatementByKey(plan, statementKey, CurrentGraphLayoutDirection);
    }

    private void SetGraphLayoutDirection(GraphLayoutDirection direction)
    {
        if (CurrentGraphLayoutDirection == direction)
        {
            return;
        }

        CurrentGraphLayoutDirection = direction;
        RefreshSelectedLayout();
    }

    private void RefreshSelectedLayout()
    {
        var plan = ActivePlan;
        var statement = SelectedStatement;
        if (plan is null || statement is null)
        {
            return;
        }

        PlanWorkspace.RefreshLayout(plan, statement, CurrentGraphLayoutDirection);
    }

    private void HandleGraphNodeSelected(string nodeId)
    {
        IsStatementDetailsSelected = false;
        SelectedNodeId = nodeId;
        TableExpandRequestVersion++;
        TableScrollRequestVersion++;
    }

    private void HandleReferencedNodeSelected(string nodeId)
    {
        IsStatementDetailsSelected = false;
        SelectedNodeId = nodeId;
        TableExpandRequestVersion++;
        TableScrollRequestVersion++;
    }

    private void HandleGraphStatementSelected(string statementId)
    {
        if (!string.Equals(SelectedStatementId, statementId, StringComparison.Ordinal))
        {
            SelectStatement(statementId);
        }

        HoveredNodeId = null;
        SelectedNodeId = null;
        IsStatementDetailsSelected = true;
    }

    private void HandleTableNodeSelected(string nodeId)
    {
        IsStatementDetailsSelected = false;
        SelectedNodeId = nodeId;
    }

    private void HandleNodeHovered(string? nodeId)
    {
        HoveredNodeId = nodeId;
    }

    private void ClearInput()
    {
        XmlInput = string.Empty;
        QueryConnectionString = string.Empty;
        QueryInput = string.Empty;
        QueryPlanLabel = string.Empty;
        QueryCommandTimeoutSeconds = SqlEstimatedShowplanProvider.DefaultCommandTimeoutSeconds;
        ShowConnectionString = false;
        ResetInputFeedback();
        ClearLoadedPlanHistory();
    }

    private void ClearLoadedPlanHistory()
    {
        Plans.Clear();
        ActivePlanId = null;
        ComparePlanAId = null;
        ComparePlanBId = null;
        PastedPlanCounter = 0;
        EstimatedPlanCounter = 0;
        TableActionMessage = null;
        ResetPlanViewState();
        TableScrollRequestVersion++;
        TableExpandRequestVersion++;
    }

    private const long MaxFileBytes = 10L * 1024 * 1024;

    private const int MaxFileCount = 50;

    private const int PlanDetailsStatementTextPreviewLength = 180;

    private static string FormatInt(int? value) =>
        value?.ToString() ?? "n/a";

    private static string BuildEstimatedPlanLabel(string baseLabel, int ordinal, int count) =>
        count > 1
            ? $"{baseLabel} ({ordinal.ToString(CultureInfo.InvariantCulture)})"
            : baseLabel;

    private static string FormatEstimatedPlanLoadedMessage(int count) =>
        count == 1
            ? "1 estimated plan loaded."
            : $"{count.ToString("N0", CultureInfo.InvariantCulture)} estimated plans loaded.";
}

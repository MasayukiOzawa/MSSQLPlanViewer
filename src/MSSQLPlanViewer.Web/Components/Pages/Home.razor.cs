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
using MSSQLPlanViewer.Web.State;

namespace MSSQLPlanViewer.Web.Components.Pages;

public partial class Home
{
    private string XmlInput { get; set; } = string.Empty;

    private int GraphCostThresholdPercent { get; set; } = 20;

    private bool ShowCriticalPath { get; set; } = true;

    private string? ParseError { get; set; }

    private int TableFocusRequestVersion { get; set; }

    private int PastedPlanCounter { get; set; }

    private readonly List<LoadedPlan> Plans = new();

    private Guid? ActivePlanId { get; set; }

    private Guid? ComparePlanAId { get; set; }

    private Guid? ComparePlanBId { get; set; }

    private readonly List<FileLoadMessage> FileLoadMessages = new();

    private string? TableActionMessage { get; set; }

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

    private StatementPlan? SelectedStatement =>
        Document?.Statements.FirstOrDefault(statement => statement.StatementId == SelectedStatementId)
        ?? Document?.Statements.FirstOrDefault();

    private PlanNode? SelectedNode =>
        SelectedStatement?.Nodes.FirstOrDefault(node => node.NodeId == SelectedNodeId);

    private PlanNode? HoveredNode =>
        SelectedStatement?.Nodes.FirstOrDefault(node => node.NodeId == HoveredNodeId);

    private PlanNode? OverlayNode => HoveredNode ?? SelectedNode;

    private sealed record FileLoadMessage(string Text, bool IsError);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await JS.InvokeVoidAsync("mssqlPlanViewerGraphPan.initDetailsResizer", detailsPaneRef, detailsResizeHandleRef);

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

    private static IReadOnlyList<PlanProperty> GetVisibleProperties(IReadOnlyList<PlanProperty> properties) =>
        properties
            .Select(property => new PlanProperty(property.Name, FormatSummaryValue(property.Value)))
            .Where(property => !string.IsNullOrWhiteSpace(property.Value))
            .ToArray();

    private static IReadOnlyList<PlanProperty> GetWarningItems(StatementPlan statement) =>
        statement.Warnings
            .Concat(statement.Nodes.SelectMany(node => node.Warnings))
            .Select(warning => (
                warning.Name,
                Display: BuildWarningDisplayText(warning)))
            .GroupBy(warning => (warning.Name, warning.Display))
            .Select(group => new PlanProperty(
                    group.Key.Name,
                    group.Count() > 1
                    ? $"{group.Count().ToString("N0", System.Globalization.CultureInfo.InvariantCulture)}x {group.Key.Display}"
                    : group.Key.Display))
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<SummaryTableRow> BuildCombinedPlanDetailRows(
        IReadOnlyList<PlanProperty> queryTimeStatsItems,
        IReadOnlyList<PlanProperty> statementSummaryItems,
        IReadOnlyList<PlanProperty> queryPlanItems,
        IReadOnlyList<PlanProperty> memoryGrantInfoItems,
        IReadOnlyList<PlanProperty> optimizerHardwareItems)
    {
        var rows = new List<SummaryTableRow>();

        AddSummaryRows(rows, "Query Time Stats", queryTimeStatsItems);
        AddSummaryRows(rows, "Statement Plan Summary", statementSummaryItems);
        AddSummaryRows(rows, "Query Plan", queryPlanItems);
        AddSummaryRows(rows, "MemoryGrantInfo", memoryGrantInfoItems);
        AddSummaryRows(rows, "OptimizerHardwareDependentProperties", optimizerHardwareItems);

        return rows;
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

    private static string BuildWarningDisplayText(PlanWarning warning)
    {
        var value = !string.IsNullOrWhiteSpace(warning.Details)
            ? warning.Details
            : !string.IsNullOrWhiteSpace(warning.Value)
                ? warning.Value!
                : "true";

        return FormatSummaryValue(value);
    }

    private static string FormatSummaryValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "n/a", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (long.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var integerValue))
        {
            return integerValue.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }

        if (decimal.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var decimalValue))
        {
            return decimalValue.ToString("#,0.###############", System.Globalization.CultureInfo.InvariantCulture);
        }

        return value;
    }

    private sealed record SummaryTableRow(
        string Section,
        string Name,
        string Value,
        bool ShowSection,
        int SectionRowSpan);

    private void ParsePlan()
    {
        ParseError = null;
        FileLoadMessages.Clear();

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
        ParseError = null;
        FileLoadMessages.Clear();

        IReadOnlyList<IBrowserFile> files;
        try
        {
            files = args.GetMultipleFiles(maximumFileCount: MaxFileCount);
        }
        catch (InvalidOperationException)
        {
            FileLoadMessages.Add(new FileLoadMessage($"Too many files selected; the maximum is {MaxFileCount}.", IsError: true));
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
                FileLoadMessages.Add(new FileLoadMessage($"{file.Name}: file is too large (max 10 MB).", IsError: true));
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

            var document = ShowplanParser.Parse(xml);
            AddPlan(document, file.Name);
            FileLoadMessages.Add(new FileLoadMessage($"{file.Name}: loaded.", IsError: false));
        }
        catch (ShowplanParseException exception)
        {
            FileLoadMessages.Add(new FileLoadMessage($"{file.Name}: {exception.Message}", IsError: true));
        }
        catch (Exception exception)
        {
            FileLoadMessages.Add(new FileLoadMessage($"{file.Name}: an unexpected error occurred.", IsError: true));
            Logger.LogError(exception, "Failed to load plan file {FileName}", file.Name);
        }
    }

    private void AddPlan(ShowplanDocument document, string label)
    {
        var plan = new LoadedPlan
        {
            Label = label,
            Document = document
        };

        Plans.Add(plan);
        ActivePlanId = plan.Id;
        EnsureCompareSelection();

        if (document.Statements.Count > 0)
        {
            SelectStatement(document.Statements[0].StatementId);
        }
    }

    private void SelectPlan(Guid planId)
    {
        if (Plans.Any(plan => plan.Id == planId))
        {
            ActivePlanId = planId;
            TableActionMessage = null;
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
        if (ComparePlanAId is null || Plans.All(plan => plan.Id != ComparePlanAId))
        {
            ComparePlanAId = Plans.Count > 0 ? Plans[0].Id : null;
        }

        var requiresDistinctB = Plans.Count > 1 && ComparePlanBId == ComparePlanAId;
        if (ComparePlanBId is null || Plans.All(plan => plan.Id != ComparePlanBId) || requiresDistinctB)
        {
            ComparePlanBId = Plans.FirstOrDefault(plan => plan.Id != ComparePlanAId)?.Id
                ?? (Plans.Count > 0 ? Plans[0].Id : null);
        }
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

    private string BuildExportBaseName()
    {
        var label = ActivePlan?.Label ?? "plan";
        var statementId = SelectedStatement?.StatementId;
        var raw = string.IsNullOrWhiteSpace(statementId) ? label : $"{label}-stmt{statementId}";
        var safe = new string(raw.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray())
            .Trim('-');

        return string.IsNullOrEmpty(safe) ? "plan" : safe;
    }

    private string BuildExportFileName(string extension) => $"{BuildExportBaseName()}.{extension}";

    private static string FormatMetricValue(double? value, bool isInteger)
    {
        if (value is null)
        {
            return string.Empty;
        }

        return isInteger
            ? ((long)Math.Round(value.Value)).ToString("N0", System.Globalization.CultureInfo.InvariantCulture)
            : value.Value.ToString("#,0.######", System.Globalization.CultureInfo.InvariantCulture);
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
        return sign + percent.Value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
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
    }

    private void OnStatementChanged(ChangeEventArgs args)
    {
        var statementId = args.Value?.ToString();
        if (string.IsNullOrWhiteSpace(statementId))
        {
            return;
        }

        SelectStatement(statementId);
    }

    private void SelectStatement(string statementId)
    {
        var plan = ActivePlan;
        if (plan is null)
        {
            return;
        }

        TableActionMessage = null;
        var statement = plan.Document.Statements.FirstOrDefault(item => item.StatementId == statementId)
            ?? plan.Document.Statements.FirstOrDefault();
        if (statement is null)
        {
            return;
        }

        plan.SelectedStatementId = statement.StatementId;
        plan.SelectedLayout = GraphLayoutService.CreateLayout(statement);
        plan.CurrentRows = TableProjector.Project(statement);
        plan.SelectedNodeId = null;
        plan.HoveredNodeId = null;
    }

    private void HandleGraphNodeSelected(string nodeId)
    {
        SelectedNodeId = nodeId;
        TableFocusRequestVersion++;
    }

    private void HandleTableNodeSelected(string nodeId)
    {
        SelectedNodeId = nodeId;
    }

    private void HandleNodeHovered(string? nodeId)
    {
        HoveredNodeId = nodeId;
    }

    private void ClearInput()
    {
        XmlInput = string.Empty;
        ParseError = null;
        FileLoadMessages.Clear();
    }

    private const long MaxFileBytes = 10L * 1024 * 1024;

    private const int MaxFileCount = 50;

    private static string FormatInt(int? value) =>
        value?.ToString() ?? "n/a";
}

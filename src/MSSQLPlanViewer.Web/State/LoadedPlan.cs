using MSSQLPlanViewer.Core.Models;
using MSSQLPlanViewer.Core.Diagnostics;
using MSSQLPlanViewer.Core.Rendering;

namespace MSSQLPlanViewer.Web.State;

/// <summary>
/// In-session view model representing a single loaded execution plan and its UI state.
/// One instance corresponds to one tab. State is not persisted across page reloads.
/// </summary>
public sealed class LoadedPlan
{
    public Guid Id { get; } = Guid.NewGuid();

    public required string Label { get; set; }

    public required ShowplanDocument Document { get; init; }

    public string? SelectedStatementId { get; set; }

    public string? SelectedStatementKey { get; set; }

    public string? SelectedNodeId { get; set; }

    public string? HoveredNodeId { get; set; }

    public bool IsStatementDetailsSelected { get; set; }

    public StatementGraphLayout? SelectedLayout { get; set; }

    public IReadOnlyList<PlanTableRow> CurrentRows { get; set; } = Array.Empty<PlanTableRow>();

    public IReadOnlyList<PlanDiagnostic> Diagnostics { get; init; } = Array.Empty<PlanDiagnostic>();
}

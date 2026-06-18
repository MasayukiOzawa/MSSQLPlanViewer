# Copilot Instructions for MSSQLPlanViewer

## Build and test commands

```powershell
dotnet build .\MSSQLPlanViewer.sln
dotnet test .\MSSQLPlanViewer.sln
dotnet test .\tests\MSSQLPlanViewer.Core.Tests\MSSQLPlanViewer.Core.Tests.csproj --filter "FullyQualifiedName~MSSQLPlanViewer.Core.Tests.PlanExportApiTests.ExportCsv_ReturnsCsvFile"
pwsh -File .\scripts\Test-PlanExportApi.ps1
```

- `scripts\Test-PlanExportApi.ps1` expects the web app to be running on `http://localhost:5293` unless `-BaseUrl` is overridden.
- After tests or manual verification, terminate any local app/process you started, such as `dotnet run` on `localhost:5293`, and confirm the port is no longer listening.
- There is no separate lint script in the repository; `dotnet build` is the repository-wide command that surfaces compiler issues and warnings.

## High-level architecture

- `src\MSSQLPlanViewer.Web\Program.cs` is intentionally thin: it wires Blazor Server, raises the SignalR message size limit to 10 MB for large plans, registers Core services, and maps `MapPlanExportEndpoints()`.
- `src\MSSQLPlanViewer.Web\Components\Pages\Home.razor` is the main orchestration surface. It owns loaded-plan tabs, active plan selection, comparison state, selected statement/operator state, and the synchronization between the graphical plan, table view, and operator/details panes.
- `src\MSSQLPlanViewer.Core\Parsing\ShowplanParser.cs` is the entry point from raw XML to domain objects. It parses Showplan XML into `ShowplanDocument`, `StatementPlan`, `PlanNode`, and `PlanEdge`, and also extracts summary sections such as QueryTimeStats, QueryPlan attributes, MemoryGrantInfo, MissingIndexes, and WaitStats.
- `src\MSSQLPlanViewer.Core\Rendering\PlanGraphLayoutService.cs` and `src\MSSQLPlanViewer.Core\Rendering\PlanTableProjector.cs` operate on the same parsed statement tree. The layout service produces deterministic graph coordinates and critical-path information; the table projector flattens the same tree into hierarchical rows with warning rollups.
- Graph/table export is stateless and reuses the same Core pipeline as the UI. `src\MSSQLPlanViewer.Web\PlanExportEndpoints.cs` accepts `showplanXml` in the request body, resolves a statement, then calls the Core projector/layout/renderers instead of depending on any page/session state.
- Graph export is a two-step server-side pipeline: `src\MSSQLPlanViewer.Core\Rendering\PlanGraphSvgRenderer.cs` renders SVG, and `src\MSSQLPlanViewer.Core\Rendering\PlanGraphPngExporter.cs` rasterizes that SVG into PNG.
- `src\MSSQLPlanViewer.Core\Comparison\PlanComparisonService.cs` compares plans only through high-level aggregate metrics. There is no node-by-node diff engine.
- `tests\MSSQLPlanViewer.Core.Tests` covers parser/rendering/export behavior and API integration. `PlanExportApiTests` boots the web app through `WebApplicationFactory<Program>`, so API changes should be validated there rather than only through unit tests.

## Key conventions

- Keep parsing, projection, rendering, export, and comparison logic in `MSSQLPlanViewer.Core`. The Web project should stay focused on DI, UI state, endpoints, and component composition.
- Keep user-facing strings in English. Current UI labels, API validation messages, and script output all follow that convention.
- When adding plan metadata, extend the existing detail surfaces instead of replacing them with a smaller “most useful only” subset. The current UI intentionally preserves broad visibility across QueryTimeStats, QueryPlan, MemoryGrantInfo, OptimizerStatsUsage, MissingIndexes, WaitStats, and warning details.
- Export format selection is query-string driven: use `/api/exports/table?format=...` and `/api/exports/graph?format=...`. The request body carries `showplanXml`, `statementId`, and graph options; invalid or missing `format` should return `400`, and an unknown statement should return `404`.
- Keep Minimal API route registration in extension methods such as `MapPlanExportEndpoints()` rather than growing `Program.cs`. `Program` must remain `partial` so integration tests can keep using `WebApplicationFactory<Program>`.
- Keep graph export styling inline in `PlanGraphSvgRenderer`. PNG export depends on server-side SVG that does not rely on browser CSS. Shared operator icon mapping belongs in `src\MSSQLPlanViewer.Core\Rendering\OperatorIconRegistry.cs` so UI and export stay aligned.
- Preserve deterministic traversal/order in graph and table output. `PlanGraphLayoutService` orders children by estimated subtree cost and then node id; `PlanTableProjector` walks roots first and appends unreachable nodes afterward. Changes there will ripple into both rendering and tests.
- The parser already has a central exclusion list for verbose XML attribute paths (`ExcludedXmlAttributePathPatterns` in `ShowplanParser`). If you need to suppress or add XML-derived detail fields, update that mechanism instead of layering ad-hoc filtering in the UI.
- Reuse `scripts\Test-PlanExportApi.ps1` when changing export contracts. It already supports `-ShowplanPath` and `-ShowplanXml` and exercises all four export formats against a running app.

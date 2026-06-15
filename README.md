# MSSQL Plan Viewer

MSSQL Plan Viewer is a Blazor web application that parses SQL Server **Showplan XML** and presents it as a **Graphical plan / Table view / Diagnostics / Operator details / Plan details / Compare plans** experience.

## Screenshot

![MSSQL Plan Viewer showing the graphical plan, table view, and operator details](docs/images/mssql-plan-viewer-screenshot.png)

## Features

- Paste **Showplan XML** or load **`.sqlplan` / `.xml`** files via drag-and-drop or file picker
- Load multiple plans, switch between **tabs**, and use **Compare plans**
- **Graphical plan**
  - Inline SVG rendering
  - Operator icons
  - Zoom, reset, and drag-to-scroll
  - **Dashed outline** highlight for operators above a cost threshold
  - Estimated-cost-based **critical path**
  - **SVG / PNG** download
- **Table view**
  - Hierarchical operator list
  - **CSV / Markdown** download and CSV copy
- **Diagnostics**
  - Cardinality estimate skew
  - TempDB spills
  - Expensive lookups
  - High-impact missing indexes
  - Implicit conversions
  - Memory grant mismatch
  - Stale statistics
  - Large scans with residual predicates
  - Parallel thread skew
- **Operator details / Plan details**
  - Warning details
  - Query Time Stats
  - Query Plan
  - MemoryGrantInfo
  - OptimizerHardwareDependentProperties
  - OptimizerStatsUsage
  - MissingIndexes
  - WaitStats
  - Accessed objects
  - Per-thread runtime counters
- Graphical plan node selection is synchronized with Table view, Diagnostics, and Operator details
- **Export API**
  - `POST /api/exports/table?format=csv`
  - `POST /api/exports/table?format=md`
  - `POST /api/exports/graph?format=svg`
  - `POST /api/exports/graph?format=png`

## Project structure

| Path | Description |
| --- | --- |
| `src\MSSQLPlanViewer.Web` | Blazor Web App (`net10.0`) |
| `src\MSSQLPlanViewer.Core` | Showplan parser, diagnostics, comparison, projection, and rendering (`net10.0`) |
| `tests\MSSQLPlanViewer.Core.Tests` | Parser, rendering, export, and API integration tests (`net10.0`) |
| `scripts\Test-PlanExportApi.ps1` | PowerShell script for export API smoke testing |

## Run locally

Install the .NET 10 SDK, then run:

```powershell
dotnet run --project .\src\MSSQLPlanViewer.Web\MSSQLPlanViewer.Web.csproj
```

The default launch profile starts the app at `http://localhost:5293`.

## Run from a GitHub Release

1. Download `MSSQLPlanViewer-vX.Y.Z-win-x64.zip` from the GitHub Releases page.
2. Extract the ZIP.
3. Keep `wwwroot`, `appsettings.json`, and the static web assets file in the same folder as `MSSQLPlanViewer.Web.exe`.
4. Start the app:

```powershell
.\MSSQLPlanViewer.Web.exe --urls http://localhost:5293
```

5. Open `http://localhost:5293` in your browser.

## Create a GitHub Release

Pushing a `v*` tag builds a Windows x64 self-contained release ZIP and attaches it to a GitHub Release.

```powershell
git tag v0.1.0
git push origin v0.1.0
```

## Usage

1. Open `http://localhost:5293`.
2. Paste SQL Server Showplan XML into the input box, or drop/select one or more `.sqlplan` / `.xml` files.
3. Click **Parse**. Loaded files appear as tabs.
4. If a plan contains multiple statements, choose the active statement from the statement selector.
5. Inspect the **Graphical plan**, **Table view**, **Diagnostics**, **Plan details**, and **Operator details** panes.
6. Select an operator in the graph, table, or Diagnostics table to synchronize the focused node and details panel.
7. Use **Download CSV**, **Download Markdown**, or **Copy** in Table view to export tabular data.
8. Use **Export SVG** or **Export PNG** in Graphical plan to export the current graph.
9. Load two or more plans to compare aggregate metrics with **Compare plans**.

## Test

```powershell
dotnet test .\MSSQLPlanViewer.sln
```

See `docs\TEST_REPORT.md` for the test report.

## Export API

### Table export

- `POST /api/exports/table?format=csv`
- `POST /api/exports/table?format=md`

Request body:

```json
{
  "showplanXml": "<ShowPlanXML ...>...</ShowPlanXML>",
  "statementId": "1"
}
```

### Graph export

- `POST /api/exports/graph?format=svg`
- `POST /api/exports/graph?format=png`

Request body:

```json
{
  "showplanXml": "<ShowPlanXML ...>...</ShowPlanXML>",
  "statementId": "1",
  "costHighlightThresholdPercent": 20,
  "showCriticalPath": true
}
```

The response is returned as a downloadable file. If `statementId` is omitted, the first statement is used. The `format` query parameter is required. Invalid or unsupported `format` values return `400`. Invalid XML returns `400`, and a missing statement returns `404`.

## Verify the API with PowerShell

`scripts\Test-PlanExportApi.ps1` calls all four endpoints and saves the returned files.

```powershell
pwsh -File .\scripts\Test-PlanExportApi.ps1
```

Or:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-PlanExportApi.ps1
```

Main parameters:

- `-BaseUrl`
- `-ShowplanPath`
- `-ShowplanXml`
- `-OutputDirectory`
- `-StatementId`
- `-CostHighlightThresholdPercent`
- `-ShowCriticalPath`

If `-ShowplanXml` is provided, the script uses that string directly. Otherwise it uses `-ShowplanPath`. If neither is provided, it falls back to `tests\MSSQLPlanViewer.Core.Tests\Samples\nested-loops-2022.sqlplan`.

## Implementation notes

- `ShowplanParser` uses `XDocument` and `LocalName` so it can handle XML namespace differences.
- Parser, diagnostics, graph, table, comparison, and export logic live in `MSSQLPlanViewer.Core` to keep UI dependencies minimal.
- Blazor Server receive message size is increased to `10 MB`.
- Graph export uses a shared server-side SVG renderer, and PNG export rasterizes that SVG output.

## License

This project is licensed under the MIT License. See `LICENSE` for details.

Third-party notices are listed in `THIRD-PARTY-NOTICES.md`.

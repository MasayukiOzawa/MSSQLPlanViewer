# MSSQL Plan Viewer

MSSQL Plan Viewer is a Blazor web application that parses SQL Server **Showplan XML** and presents it as a **Graphical plan / Table view / Operator details / Plan details / Compare plans** experience.

## Features

- Paste **Showplan XML** or load **`.sqlplan` / `.xml`** files via drag-and-drop or file picker
- Load multiple plans, switch between **tabs**, and use **Compare plans**
- **Graphical plan**
  - Inline SVG rendering
  - Operator icons
  - Zoom, reset, and drag-to-scroll
  - **Dashed outline** highlight for operators above a cost threshold
  - Estimated-cost-based **critical path**
- **Table view**
  - Hierarchical operator list
  - **CSV / Markdown** download
- **Operator details / Plan details**
  - Warning details
  - Query Time Stats
  - Query Plan
  - MemoryGrantInfo
  - OptimizerHardwareDependentProperties
  - OptimizerStatsUsage
  - MissingIndexes
  - WaitStats
- Graphical plan node selection is synchronized with Table view and Operator details
- **Export API**
  - `POST /api/exports/table?format=csv`
  - `POST /api/exports/table?format=md`
  - `POST /api/exports/graph?format=svg`
  - `POST /api/exports/graph?format=png`

## Project structure

| Path | Description |
| --- | --- |
| `src\MSSQLPlanViewer.Web` | Blazor Web App (`net10.0`) |
| `src\MSSQLPlanViewer.Core` | Showplan parser, models, projection, and rendering (`net10.0`) |
| `tests\MSSQLPlanViewer.Core.Tests` | Parser, rendering, export, and API integration tests (`net10.0`) |
| `scripts\Test-PlanExportApi.ps1` | PowerShell script for export API smoke testing |

## Run locally

```powershell
dotnet run --project .\src\MSSQLPlanViewer.Web\MSSQLPlanViewer.Web.csproj
```

The default launch profile starts the app at `http://localhost:5293`.

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
- Graph, table, and export logic live in `MSSQLPlanViewer.Core` to keep UI dependencies minimal.
- Blazor Server receive message size is increased to `10 MB`.
- Graph export uses a shared server-side SVG renderer, and PNG export rasterizes that SVG output.

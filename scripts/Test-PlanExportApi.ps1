[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:5293",
    [string]$ShowplanXml,
    [string]$ShowplanPath,
    [string]$OutputDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) "MSSQLPlanViewer\ApiExport"),
    [string]$StatementId,
    [int]$CostHighlightThresholdPercent = 20,
    [bool]$ShowCriticalPath = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

if ([string]::IsNullOrWhiteSpace($ShowplanXml) -and [string]::IsNullOrWhiteSpace($ShowplanPath)) {
    $ShowplanPath = Join-Path $PSScriptRoot "..\tests\MSSQLPlanViewer.Core.Tests\Samples\nested-loops-2022.sqlplan"
}

function Get-DownloadFileName {
    param(
        [Parameter(Mandatory = $true)]
        $Response,

        [Parameter(Mandatory = $true)]
        [string]$FallbackName
    )

    $contentDisposition = $Response.Headers["Content-Disposition"]
    if ([string]::IsNullOrWhiteSpace($contentDisposition)) {
        return $FallbackName
    }

    $utf8Match = [regex]::Match($contentDisposition, 'filename\*=UTF-8''''(?<name>[^;]+)')
    if ($utf8Match.Success) {
        return [System.Uri]::UnescapeDataString($utf8Match.Groups["name"].Value.Trim('"'))
    }

    $fileNameMatch = [regex]::Match($contentDisposition, 'filename="?(?<name>[^";]+)"?')
    if ($fileNameMatch.Success) {
        return $fileNameMatch.Groups["name"].Value
    }

    return $FallbackName
}

function Invoke-ExportRequest {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Endpoint,

        [Parameter(Mandatory = $true)]
        [string]$FallbackName
    )

    $temporaryPath = Join-Path $OutputDirectory ([System.Guid]::NewGuid().ToString("N") + ".tmp")
    $response = Invoke-WebRequest -UseBasicParsing -Uri ($BaseUrl.TrimEnd("/") + $Endpoint) -Method Post -ContentType "application/json" -Body $requestBody -OutFile $temporaryPath -PassThru
    $fileName = Get-DownloadFileName -Response $response -FallbackName $FallbackName
    $path = Join-Path $OutputDirectory $fileName
    Move-Item -LiteralPath $temporaryPath -Destination $path -Force

    [pscustomobject]@{
        Endpoint    = $Endpoint
        StatusCode  = [int]$response.StatusCode
        ContentType = ($response.Headers["Content-Type"] -join ", ")
        FileName    = $fileName
        FilePath    = $path
        Bytes       = (Get-Item $path).Length
    }
}

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($ShowplanXml)) {
    if (-not (Test-Path -LiteralPath $ShowplanPath)) {
        throw "Showplan file was not found: $ShowplanPath"
    }

    $resolvedShowplanPath = (Resolve-Path -LiteralPath $ShowplanPath).ProviderPath
    $ShowplanXml = [System.IO.File]::ReadAllText($resolvedShowplanPath, [System.Text.Encoding]::UTF8)
}

$request = [ordered]@{
    showplanXml                   = [string]$ShowplanXml
    costHighlightThresholdPercent = $CostHighlightThresholdPercent
    showCriticalPath              = $ShowCriticalPath
}

if (-not [string]::IsNullOrWhiteSpace($StatementId)) {
    $request["statementId"] = $StatementId
}

$requestBody = $request | ConvertTo-Json -Depth 5 -Compress

$results = @(
    Invoke-ExportRequest -Endpoint "/api/exports/table?format=csv" -FallbackName "plan-table.csv"
    Invoke-ExportRequest -Endpoint "/api/exports/table?format=md" -FallbackName "plan-table.md"
    Invoke-ExportRequest -Endpoint "/api/exports/graph?format=svg" -FallbackName "plan-graph.svg"
    Invoke-ExportRequest -Endpoint "/api/exports/graph?format=png" -FallbackName "plan-graph.png"
)

$results | Format-Table Endpoint, StatusCode, ContentType, FileName, Bytes -AutoSize

Write-Host ""
Write-Host "Saved files:"
foreach ($result in $results) {
    Write-Host " - $($result.FilePath)"
}

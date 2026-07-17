$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$main = Join-Path $Root "ZhuaQianDesktop.cs"
$budgetPath = Join-Path (Split-Path -Parent $Root) "docs\_line_budget.json"
if (-not (Test-Path $budgetPath)) {
    $budgetPath = Join-Path $Root "docs\_line_budget.json"
}
if (-not (Test-Path $budgetPath)) { throw "Missing architecture budget: docs\_line_budget.json" }

$budget = Get-Content -LiteralPath $budgetPath -Raw | ConvertFrom-Json
$maxMainLines = [int]$budget.maxMainLines
$maxOtherCsLines = [int]$budget.maxOtherCsLines
$maxAllowedToolNewExceptions = [int]$budget.maxAllowedToolNewExceptions

$failures = New-Object System.Collections.Generic.List[string]
$mainLines = 0

if (Test-Path $main) {
    $mainLines = (Get-Content -LiteralPath $main | Measure-Object -Line).Lines
    if ($mainLines -gt $maxMainLines) {
        $failures.Add("ZhuaQianDesktop.cs grew to $mainLines lines; budget is $maxMainLines. Move new behavior into modules/executors.")
    }
}

Get-ChildItem -Path $Root -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch "\\tests\\" -and $_.FullName -ne $main } |
    ForEach-Object {
        $lines = (Get-Content -LiteralPath $_.FullName | Measure-Object -Line).Lines
        if ($lines -gt $maxOtherCsLines) {
            $rel = $_.FullName.Substring($Root.Length + 1)
            $failures.Add("$rel has $lines lines; budget is $maxOtherCsLines.")
        }
    }

$allowedToolNews = @()
foreach ($item in $budget.allowedToolNews) {
    if ([string]::IsNullOrWhiteSpace($item.pattern) -or [string]::IsNullOrWhiteSpace($item.backlog)) {
        $failures.Add("Each allowed Tools construction needs a pattern and backlog reference.")
    } else {
        $allowedToolNews += [string]$item.pattern
    }
}
if ($allowedToolNews.Count -gt $maxAllowedToolNewExceptions) {
    $failures.Add("Too many direct Tools construction exceptions: $($allowedToolNews.Count), budget is $maxAllowedToolNewExceptions.")
}

Select-String -Path $main -Pattern "new\s+Tools\." | ForEach-Object {
    $line = $_.Line.Trim()
    $allowed = $false
    foreach ($item in $allowedToolNews) {
        if ($line.Contains($item)) { $allowed = $true; break }
    }
    if (-not $allowed) {
        $failures.Add("Direct Tools construction in UI is not allowed: line $($_.LineNumber): $line")
    }
}

if ($failures.Count -gt 0) {
    Write-Output "ARCHITECTURE CHECK FAILED"
    foreach ($failure in $failures) { Write-Output "  $failure" }
    exit 1
}

if ($mainLines -gt 0 -and $mainLines -lt $maxMainLines) {
    $budget.maxMainLines = $mainLines
    $budget.lastUpdated = [DateTimeOffset]::Now.ToString("o")
    $budget | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $budgetPath -Encoding UTF8
    Write-Output "Line budget ratcheted down: ZhuaQianDesktop.cs <= $mainLines"
}

Write-Output "Architecture checks passed."

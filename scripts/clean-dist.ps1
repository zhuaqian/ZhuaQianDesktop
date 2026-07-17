param(
    [string]$DistDir = "dist",
    [string]$KeepExe = "ZhuaQianDesktop.exe"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$DistPath = Join-Path $Root $DistDir
if (-not (Test-Path -LiteralPath $DistPath)) {
    throw "Missing dist directory: $DistPath"
}

$resolvedDist = (Resolve-Path -LiteralPath $DistPath).Path
$archiveRoot = Join-Path $resolvedDist "archive"
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$archiveDir = Join-Path $archiveRoot $stamp

if (-not (Test-Path -LiteralPath $archiveRoot)) {
    New-Item -ItemType Directory -Path $archiveRoot -Force | Out-Null
}
New-Item -ItemType Directory -Path $archiveDir -Force | Out-Null

$resolvedArchive = (Resolve-Path -LiteralPath $archiveDir).Path
if (-not $resolvedArchive.StartsWith($resolvedDist, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Archive path is outside dist: $resolvedArchive"
}

$keep = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
[void]$keep.Add($KeepExe)
[void]$keep.Add($KeepExe + ".sha256")
[void]$keep.Add("archive")

$moved = New-Object System.Collections.Generic.List[string]
Get-ChildItem -LiteralPath $resolvedDist -Force | Sort-Object Name | ForEach-Object {
    if ($keep.Contains($_.Name)) { return }
    $target = Join-Path $archiveDir $_.Name
    Move-Item -LiteralPath $_.FullName -Destination $target -Force
    $moved.Add($_.Name)
}

$manifest = Join-Path $archiveDir "manifest.txt"
$lines = @(
    "ZhuaQian Desktop dist archive",
    "Created: $([DateTimeOffset]::Now.ToString("o"))",
    "Kept: $KeepExe",
    "",
    "Moved:"
) + ($moved | ForEach-Object { "- " + $_ })
$lines | Set-Content -LiteralPath $manifest -Encoding UTF8

Write-Host "Dist cleaned." -ForegroundColor Green
Write-Host "Kept: $KeepExe"
Write-Host "Archived: $($moved.Count) item(s)"
Write-Host "Archive: $archiveDir"

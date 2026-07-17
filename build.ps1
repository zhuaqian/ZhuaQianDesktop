param(
    [string]$Output = "dist\ZhuaQianDesktop.exe"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$SrcDir = Join-Path $Root "src"
$DistDir = Split-Path -Parent (Join-Path $Root $Output)

if (-not (Test-Path $SrcDir)) {
    throw "Missing src directory: $SrcDir"
}

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

Push-Location $SrcDir
try {
    $relativeOutput = Join-Path ".." $Output
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Output $relativeOutput
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}

$OutPath = Join-Path $Root $Output
if (Test-Path $OutPath) {
    Get-FileHash -Algorithm SHA256 $OutPath |
        ForEach-Object { "$($_.Hash)  ZhuaQianDesktop.exe" } |
        Set-Content -Encoding ASCII ($OutPath + ".sha256")
    Write-Host "Built: $OutPath" -ForegroundColor Green
}

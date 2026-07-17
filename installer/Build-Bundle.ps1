<#
.SYNOPSIS
    Package a built dist/ artifact into a distributable release zip with the installer.

.DESCRIPTION
    Copies the built executable + .sha256 sidecar, the installer scripts, README,
    and LICENSE into a staging folder, then zips it as
    build/bundle/ZhuaQianDesktop-<Version>.zip. This is what an end user receives
    and runs installer/Install.ps1 from.

    Only the CI-produced, tag-built dist artifact should be bundled for an official
    release. Do not publish a locally built zip as an official release.

.PARAMETER SourceDir
    Folder containing ZhuaQianDesktop.exe. Default: dist (resolved from repo root).

.PARAMETER Version
    Version label for the bundle name. Default: 0.1

.PARAMETER OutputDir
    Folder where the zip is written. Default: build/bundle
#>
[CmdletBinding()]
param(
    [string]$SourceDir = "dist",
    [string]$Version = "0.1",
    [string]$OutputDir = "build\bundle"
)

$ErrorActionPreference = 'Stop'

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
if (-not [System.IO.Path]::IsPathRooted($SourceDir)) {
    $SourceDir = Join-Path $RepoRoot $SourceDir
}
$SourceDir = [System.IO.Path]::GetFullPath($SourceDir)

$ExeName = "ZhuaQianDesktop.exe"
$ExeSrc  = Join-Path $SourceDir $ExeName
if (-not (Test-Path $ExeSrc)) {
    throw "Source executable not found: $ExeSrc. Build first with build.ps1, or pass -SourceDir."
}

$Stage = Join-Path $RepoRoot $OutputDir "ZhuaQianDesktop-$Version"
if (Test-Path $Stage) { Remove-Item $Stage -Recurse -Force }
New-Item -ItemType Directory -Path $Stage -Force | Out-Null

# Executable + sidecar.
Copy-Item $ExeSrc $Stage -Force
if (Test-Path "$ExeSrc.sha256") { Copy-Item "$ExeSrc.sha256" $Stage -Force }

# Installer scripts.
Copy-Item (Join-Path $RepoRoot "installer\Install.ps1")   $Stage -Force
Copy-Item (Join-Path $RepoRoot "installer\Uninstall.ps1") $Stage -Force

# Docs.
Copy-Item (Join-Path $RepoRoot "README.md")  $Stage -Force
Copy-Item (Join-Path $RepoRoot "LICENSE")    $Stage -Force

# Zip.
$Zip = Join-Path $RepoRoot $OutputDir "ZhuaQianDesktop-$Version.zip"
if (Test-Path $Zip) { Remove-Item $Zip -Force }
Compress-Archive -Path (Join-Path $Stage "*") -DestinationPath $Zip -Force

Write-Host "[ok] Release bundle written: $Zip"
Write-Host "     Users run installer/Install.ps1 (elevated) after extracting."

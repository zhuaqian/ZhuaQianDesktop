<#
.SYNOPSIS
    Install ZhuaQian Desktop from a built dist/ artifact.

.DESCRIPTION
    Copies ZhuaQianDesktop.exe (and its .sha256 sidecar) from -SourceDir into
    -InstallDir, verifies SHA-256 integrity against the sidecar, creates Start Menu
    and optional Desktop shortcuts, and registers an uninstall entry under
    HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall.

    Requires administrator privileges (writes to Program Files + HKLM).

    This script is a release/install tool and does NOT use the in-app agent
    command pipeline. It only stages the built executable on the local machine.

.PARAMETER SourceDir
    Folder containing ZhuaQianDesktop.exe. Default: dist (resolved from repo root).

.PARAMETER InstallDir
    Target install folder. Default: $env:ProgramFiles\ZhuaQianDesktop

.PARAMETER NoDesktopShortcut
    Skip creating a Desktop shortcut.

.PARAMETER NoStartMenuShortcut
    Skip creating a Start Menu shortcut.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File installer/Install.ps1
#>
[CmdletBinding()]
param(
    [string]$SourceDir = "dist",
    [string]$InstallDir = "$env:ProgramFiles\ZhuaQianDesktop",
    [switch]$NoDesktopShortcut,
    [switch]$NoStartMenuShortcut
)

$ErrorActionPreference = 'Stop'

# Resolve repo-relative SourceDir to an absolute path.
if (-not [System.IO.Path]::IsPathRooted($SourceDir)) {
    $SourceDir = Join-Path $PSScriptRoot ".." $SourceDir
}
$SourceDir = [System.IO.Path]::GetFullPath($SourceDir)

$ExeName = "ZhuaQianDesktop.exe"
$ExeSrc  = Join-Path $SourceDir $ExeName
if (-not (Test-Path $ExeSrc)) {
    throw "Source executable not found: $ExeSrc. Build first with build.ps1, or pass -SourceDir."
}

# Administrator check.
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "Administrator privileges required to install to '$InstallDir' and write the uninstall registry. Re-run from an elevated PowerShell window."
}

# Integrity check against the .sha256 sidecar (format: "<hash>  <filename>").
$Sidecar = "$ExeSrc.sha256"
if (Test-Path $Sidecar) {
    $expected = ((Get-Content $Sidecar -First 1) -split '\s+')[0].Trim().ToUpperInvariant()
    $actual   = (Get-FileHash -Algorithm SHA256 $ExeSrc).Hash.ToUpperInvariant()
    if ($expected -ne $actual) {
        throw "SHA-256 mismatch! sidecar=$expected actual=$actual. The artifact may be corrupt or tampered; aborting install."
    }
    Write-Host "[ok] SHA-256 verified: $actual"
}
else {
    Write-Warning "No .sha256 sidecar found next to the executable; skipping integrity verification."
}

# Stage files.
if (-not (Test-Path $InstallDir)) { New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null }
Copy-Item $ExeSrc $InstallDir -Force
if (Test-Path $Sidecar) { Copy-Item $Sidecar (Join-Path $InstallDir "$ExeName.sha256") -Force }
$ExeDst = Join-Path $InstallDir $ExeName

# Shortcuts via WScript.Shell COM.
$wshell = New-Object -ComObject WScript.Shell
function New-Shortcut {
    param([string]$LinkPath, [string]$Target)
    $sc = $wshell.CreateShortcut($LinkPath)
    $sc.TargetPath = $Target
    $sc.WorkingDirectory = $InstallDir
    $sc.Description = "ZhuaQian Desktop - local-first Windows AI workbench"
    $sc.Save()
}

if (-not $NoStartMenuShortcut) {
    $sm = [System.Environment]::GetFolderPath('StartMenu')
    $link = Join-Path $sm "Programs\ZhuaQian Desktop.lnk"
    New-Shortcut $link $ExeDst
    Write-Host "[ok] Start Menu shortcut: $link"
}
if (-not $NoDesktopShortcut) {
    $dt = [System.Environment]::GetFolderPath('Desktop')
    $link = Join-Path $dt "ZhuaQian Desktop.lnk"
    New-Shortcut $link $ExeDst
    Write-Host "[ok] Desktop shortcut: $link"
}

# Uninstall registry entry (so it appears in Programs and Features).
$uninstallPs1    = Join-Path $PSScriptRoot "Uninstall.ps1"
$uninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$uninstallPs1`" -InstallDir `"$InstallDir`""
$regPath = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ZhuaQianDesktop"
if (-not (Test-Path $regPath)) { New-Item -Path $regPath -Force | Out-Null }
Set-ItemProperty -Path $regPath -Name "DisplayName"     -Value "ZhuaQian Desktop"
Set-ItemProperty -Path $regPath -Name "UninstallString" -Value $uninstallString
Set-ItemProperty -Path $regPath -Name "InstallLocation" -Value $InstallDir
Set-ItemProperty -Path $regPath -Name "DisplayVersion"  -Value "0.1"
Set-ItemProperty -Path $regPath -Name "Publisher"       -Value "ZhuaQian Desktop contributors"
Set-ItemProperty -Path $regPath -Name "NoModify"        -Value 1
Set-ItemProperty -Path $regPath -Name "NoRepair"        -Value 1

Write-Host ""
Write-Host "ZhuaQian Desktop installed to: $InstallDir"
Write-Host "Uninstall via: Programs and Features, or installer/Uninstall.ps1"

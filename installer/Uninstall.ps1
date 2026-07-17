<#
.SYNOPSIS
    Uninstall ZhuaQian Desktop installed by installer/Install.ps1.

.DESCRIPTION
    Removes the install folder, Start Menu / Desktop shortcuts, and the
    HKLM uninstall registry entry created by Install.ps1.

    Requires administrator privileges.

.PARAMETER InstallDir
    Install folder to remove. Default: $env:ProgramFiles\ZhuaQianDesktop
#>
[CmdletBinding()]
param(
    [string]$InstallDir = "$env:ProgramFiles\ZhuaQianDesktop"
)

$ErrorActionPreference = 'Stop'

# Administrator check.
$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
$isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    throw "Administrator privileges required to remove '$InstallDir' and the uninstall registry entry. Re-run from an elevated PowerShell window."
}

# Remove install folder.
if (Test-Path $InstallDir) {
    Remove-Item $InstallDir -Recurse -Force
    Write-Host "[ok] Removed install folder: $InstallDir"
}
else {
    Write-Host "[skip] Install folder not present: $InstallDir"
}

# Remove shortcuts.
$sm = [System.Environment]::GetFolderPath('StartMenu')
$dt = [System.Environment]::GetFolderPath('Desktop')
$shortcuts = @(
    (Join-Path $sm "Programs\ZhuaQian Desktop.lnk"),
    (Join-Path $dt "ZhuaQian Desktop.lnk")
)
foreach ($link in $shortcuts) {
    if (Test-Path $link) {
        Remove-Item $link -Force
        Write-Host "[ok] Removed shortcut: $link"
    }
}

# Remove uninstall registry entry.
$regPath = "HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\ZhuaQianDesktop"
if (Test-Path $regPath) {
    Remove-Item $regPath -Recurse -Force
    Write-Host "[ok] Removed registry entry: $regPath"
}

Write-Host ""
Write-Host "ZhuaQian Desktop uninstalled."

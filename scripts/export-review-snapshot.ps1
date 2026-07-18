<#
.SYNOPSIS
    Export a review snapshot from committed git content only.

.DESCRIPTION
    Creates a zip with `git archive HEAD`, so generated outputs, local build
    folders, retired mirrors, and uncommitted half-finished work cannot leak into
    an external review package.

    By default the script refuses a dirty working tree. Use -AllowDirty only for
    an internal handoff where the filename must honestly show that the archive is
    not the current workspace state.
#>
[CmdletBinding()]
param(
    [string]$OutputDir = "build\review",
    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"

$RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$OutputDir = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    [System.IO.Path]::GetFullPath($OutputDir)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $OutputDir))
}

Push-Location $RepoRoot
try {
    git rev-parse --is-inside-work-tree *> $null
    if ($LASTEXITCODE -ne 0) { throw "Not inside a git work tree: $RepoRoot" }

    $status = git status --porcelain
    $dirty = -not [string]::IsNullOrWhiteSpace(($status | Out-String))
    if ($dirty -and -not $AllowDirty) {
        throw "Working tree has uncommitted changes. Commit/stash first, or pass -AllowDirty for an explicitly dirty internal snapshot."
    }

    $sha = (git rev-parse --short=12 HEAD).Trim()
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $suffix = if ($dirty) { "-dirty" } else { "" }
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
    $zip = Join-Path $OutputDir ("ZhuaQianDesktop-review-{0}-{1}{2}.zip" -f $stamp, $sha, $suffix)

    if (Test-Path $zip) { Remove-Item $zip -Force }
    git archive --format=zip -o $zip HEAD
    if ($LASTEXITCODE -ne 0) { throw "git archive failed" }

    $hash = Get-FileHash -Algorithm SHA256 -Path $zip
    Set-Content -LiteralPath ($zip + ".sha256") -Value ($hash.Hash + "  " + [System.IO.Path]::GetFileName($zip)) -Encoding ASCII

    Write-Host "[ok] Review snapshot written: $zip"
    Write-Host "[ok] SHA256 sidecar written: $zip.sha256"
    if ($dirty) {
        Write-Host "[warn] Workspace was dirty; archive contains committed HEAD only, not uncommitted changes."
    }
}
finally {
    Pop-Location
}

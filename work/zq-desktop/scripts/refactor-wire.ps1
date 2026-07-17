<#
 .SYNOPSIS
    refactor-wire.ps1 - applies the "wire MainForm to existing modules +
    delete inline duplicates" refactor for ZhuaQian Desktop, in
    idempotent, reviewable steps.

 .DESCRIPTION
    Round A (validated, behavior-preserving): collapse the two PermissionGate
    instances -- the readonly `permissionGate` field (line ~77) and the
    duplicate `PermissionGate permGate` field (line ~108) -- into the single
    `permissionGate` field. `EnsurePermission` (line ~687) only ever
    consulted the raw bool switches, never `permGate`, so removing the second
    instance is a pure structural de-dupe with NO runtime behavior change.
    The rich three-tier PermissionGate (Allow/Ask/Deny, patterns, auto-mode,
    allowed-dirs) is still built in LoadConfig and serialized to
    `permGateJson`, just held in one object now.

    REMAINING wiring (tracked as verified findings, to be added as
    additional idempotent transforms after each is confirmed in place):
      * LoadConfig/SaveConfig (~1229/1343) -> Core.ConfigStore
        (currently a parallel `cfg` Dictionary + DPAPI; Core.ConfigStore
        exists and is unit-tested).
      * LogAction (~4443) / RecordAction (~4454) -> Core.AuditLog
        (Core.AuditLog exists and is unit-tested).
      * EnsurePermission (~687) -> permissionGate.Check(name, target)
        (this is the real behavior upgrade: enforce patterns/auto/dirs
        instead of only the bool switch; pair with ApprovalCard for Ask).
      * OrganizeFolder (~2984) -> Tools.FolderOrganizer
        (Tools.FolderOrganizer.Organize/CategoryFor already called at
        2804/2784; the inline OrganizeFolder body is the dupe).
      * PostGemini/CallOpenRouter/CallLocalApi/ExtractReply -> providers/*Client
      * SendMessage (~3576) -> providers.StreamingBridge (already unit-tested)
      * ProcessSnapshotCollector as the monitoring dashboard base

 .PARAMETER Source
    Path to ZhuaQianDesktop.cs (default: ./ZhuaQianDesktop.cs).

 .PARAMETER NoBuild
    Skip the build+test verification at the end.

 .EXAMPLE
    .\refactor-wire.ps1
    .\refactor-wire.ps1 -Source C:\path\ZhuaQianDesktop.cs -NoBuild
#>
param(
    [string]$Source = "ZhuaQianDesktop.cs",
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $Source)) { throw "Source not found: $Source" }

$text = [System.IO.File]::ReadAllText($Source, [System.Text.Encoding]::UTF8)
$applied = @()

function Apply-Regex([string]$Name, [string]$Pattern, [string]$Replacement) {
    if ([System.Text.RegularExpressions.Regex]::IsMatch($script:text, $Pattern)) {
        $script:text = [System.Text.RegularExpressions.Regex]::Replace(
            $script:text, $Pattern, $Replacement,
            [System.Text.RegularExpressions.RegexOptions]::None)
        $script:applied += $Name
        Write-Host ("APPLIED : " + $Name) -ForegroundColor Green
    } else {
        Write-Host ("SKIPPED : " + $Name + " (already applied / not found)") -ForegroundColor DarkGray
    }
}

# --- Round A: PermissionGate dual-object consolidation -------------------------
# 1) make the single `permissionGate` field reassignable (drop readonly)
Apply-Regex "permissionGate: drop readonly" `
    'readonly Core\.PermissionGate permissionGate = new Core\.PermissionGate\(\);' `
    'Core.PermissionGate permissionGate = new Core.PermissionGate();'

# 2) remove the duplicate `PermissionGate permGate = new PermissionGate();` field line
Apply-Regex "permissionGate: remove duplicate field" `
    '[\r\n]+\s*PermissionGate permGate = new PermissionGate\(\);' `
    ''

# 3) route the dot-access references (Set / AutoMode / AllowedDirectories / ToJson)
#    `permGate.` never appears inside `permissionGate.`, and `permGateJson`
#    (the config key) has no dot, so this is safe and global.
Apply-Regex "permissionGate: route permGate. -> permissionGate." `
    'permGate\.' `
    'permissionGate.'

# 4) route the dot-less reassignment `permGate = loaded;`
Apply-Regex "permissionGate: route permGate = loaded" `
    'permGate = loaded;' `
    'permissionGate = loaded;'

# --- write back ----------------------------------------------------------
[System.IO.File]::WriteAllText($Source, $text, [System.Text.Encoding]::UTF8)
Write-Host ("`nWrote " + $Source + " (" + $text.Length + " chars). Transforms applied: " + $applied.Count) -ForegroundColor Cyan

if (-not $NoBuild) {
    $root = Split-Path -Parent (Resolve-Path $Source)
    $build = Join-Path $root "build.ps1"
    $tests = Join-Path $root "scripts\run-tests.ps1"
    if (Test-Path $build) {
        Write-Host "`nRunning build.ps1 ..." -ForegroundColor Cyan
        & $build
    }
    if (Test-Path $tests) {
        Write-Host "`nRunning tests ..." -ForegroundColor Cyan
        & $tests
    }
}

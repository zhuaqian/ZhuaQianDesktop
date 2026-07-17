$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$DocsDir = Join-Path $Root "docs"
if (-not (Test-Path $DocsDir)) { $DocsDir = Join-Path (Split-Path -Parent $Root) "docs" }
$VerificationPath = Join-Path $DocsDir "_last_verification.txt"
$verificationLines = New-Object System.Collections.Generic.List[string]
function Write-Step([string]$message, [ConsoleColor]$color = [ConsoleColor]::Gray) {
    $verificationLines.Add($message)
    Write-Host $message -ForegroundColor $color
}
function Save-Verification([int]$exitCode) {
    if (-not (Test-Path $DocsDir)) { return }
    $header = @(
        "ZhuaQian Desktop Verification",
        "Captured: $([DateTimeOffset]::Now.ToString("o"))",
        "Root: $Root",
        "ExitCode: $exitCode",
        ""
    )
    ($header + $verificationLines) | Set-Content -LiteralPath $VerificationPath -Encoding UTF8
}
Push-Location $Root
try {
    $productionSources = Get-ChildItem -Path $Root -Recurse -Filter *.cs |
        Where-Object { $_.FullName -notmatch "\\tests\\" }
    $emptyCatches = Select-String -Path $productionSources.FullName -Pattern "catch\s*\{\s*\}" |
        ForEach-Object { "$($_.Path):$($_.LineNumber)" }
    if ($emptyCatches.Count -gt 0) {
        Write-Step "FAILED: Empty catch blocks found in production source:" Red
        foreach ($item in $emptyCatches) { Write-Step "  $item" Red }
        Save-Verification 1
        exit 1
    }

    $archOutput = & (Join-Path $ScriptDir "check-architecture.ps1") 2>&1
    foreach ($line in $archOutput) { Write-Step ([Convert]::ToString($line)) Gray }
    if (-not $?) { throw "Architecture check failed" }

    $packageCheck = Join-Path $ScriptDir "check-package.ps1"
    if (Test-Path $packageCheck) {
        $packageOutput = & $packageCheck 2>&1
        foreach ($line in $packageOutput) { Write-Step ([Convert]::ToString($line)) Gray }
        if (-not $?) { throw "Package check failed" }
    }

    $CscCandidates = @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
    )
    $Csc = $CscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $Csc) { throw "csc.exe not found" }

    $Refs = @(
        "System.Windows.Forms.dll",
        "System.Drawing.dll",
        "System.Web.Extensions.dll",
        "System.Security.dll",
        "System.IO.Compression.dll",
        "System.IO.Compression.FileSystem.dll"
    ) -join ";"

    # Dynamic enumeration: always in sync with the filesystem and csproj.
    # Includes test sources (TestRunner + module tests) so the harness compiles,
    # but excludes the legacy/dead duplicate files (Program.cs, MainForm.cs,
    # TaskInfo.cs) and the two standalone harnesses (SelfTest.cs, ConfigStoreTests.cs)
    # which are run separately / via Visual Studio.
    # The EXE entry (ZhuaQianDesktop.cs) is intentionally included so the MainForm
    # partial and TaskInfo type are fully defined; /main:TestRunner (below) selects
    # the test harness entry point to avoid CS0017 (multiple entry points).
    $deadFiles = @('Program.cs', 'MainForm.cs', 'TaskInfo.cs')
    $excludeTests = @('SelfTest.cs', 'ConfigStoreTests.cs')
    $Src = @(Get-ChildItem -Path $Root -Recurse -Filter *.cs |
        Where-Object {
            ($deadFiles -notcontains $_.Name) -and
            (-not (($_.FullName -match '[\\/]tests[\\/]') -and ($excludeTests -contains $_.Name)))
        } |
        ForEach-Object { $_.FullName })

    $testDir = Join-Path ([System.IO.Path]::GetTempPath()) "ZhuaQianDesktopTests"
    if (-not (Test-Path $testDir)) { New-Item -ItemType Directory -Path $testDir | Out-Null }
    $Out = Join-Path $testDir ("TestRunner-" + [Guid]::NewGuid().ToString("N") + ".exe")

    Write-Step "Compiling unit tests ..." Cyan
    $compileOutput = & $Csc /nologo /target:exe /out:$Out /main:TestRunner /reference:$Refs $Src 2>&1
    foreach ($line in $compileOutput) { Write-Step ([Convert]::ToString($line)) Gray }
    if ($LASTEXITCODE -ne 0) { throw "Test compilation failed" }

    Write-Step "Running unit tests ..." Cyan
    $testOutput = & $Out 2>&1
    $code = $LASTEXITCODE
    foreach ($line in $testOutput) { Write-Step ([Convert]::ToString($line)) Gray }
    if ($code -eq 0) {
        Write-Step "ALL TESTS PASSED" Green
    } else {
        Write-Step "TESTS FAILED (exit $code)" Red
    }
    Save-Verification $code
    exit $code
}
catch {
    Write-Step ("FAILED: " + $_.Exception.Message) Red
    Save-Verification 1
    throw
}
finally {
    Pop-Location
}

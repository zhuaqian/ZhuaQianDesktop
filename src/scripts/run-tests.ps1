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

    $global:LASTEXITCODE = 0
    $archOutput = & (Join-Path $ScriptDir "check-architecture.ps1") 2>&1
    $archCode = $LASTEXITCODE
    foreach ($line in $archOutput) { Write-Step ([Convert]::ToString($line)) Gray }
    if ($archCode -ne 0) { throw "Architecture check failed" }

    $packageCheck = Join-Path $ScriptDir "check-package.ps1"
    if (Test-Path $packageCheck) {
        $global:LASTEXITCODE = 0
        $packageOutput = & $packageCheck 2>&1
        $packageCode = $LASTEXITCODE
        foreach ($line in $packageOutput) { Write-Step ([Convert]::ToString($line)) Gray }
        if ($packageCode -ne 0) { throw "Package check failed" }
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
        "System.IO.Compression.FileSystem.dll",
        "System.Net.Http.dll",
        "System.Xml.dll"
    ) -join ";"

    # Optional Playwright for .NET + transitive DLLs. The default raw-csc test
    # build leaves browser rendering disabled because some .NET Framework setups
    # lack the required netstandard facade assemblies. Set ZQ_ENABLE_PLAYWRIGHT=1
    # in an SDK/MSBuild environment to compile the full browser feature.
    $packagesDir = Join-Path $Root "packages"
    $pwRefs = New-Object System.Collections.Generic.List[string]
    function Add-PwRefTest($name) {
        $candidates = @(Get-ChildItem -Path $packagesDir -Recurse -Filter $name -ErrorAction SilentlyContinue)
        $dll = $null
        foreach ($tfm in @("\lib\net48\", "\lib\net472\", "\lib\net471\", "\lib\net47\", "\lib\net462\", "\lib\net461\", "\lib\netstandard2.0\", "\build\netstandard2.0\ref\")) {
            $dll = $candidates | Where-Object { $_.FullName.Contains($tfm) } | Sort-Object FullName -Descending | Select-Object -First 1
            if ($dll) { break }
        }
        if (-not $dll) { $dll = $candidates | Sort-Object FullName -Descending | Select-Object -First 1 }
        if ($dll -and -not ($pwRefs.Contains($dll.FullName))) { $pwRefs.Add($dll.FullName) }
    }
    $compileDefines = @()
    if ($env:ZQ_ENABLE_PLAYWRIGHT -eq "1") {
        Add-PwRefTest "Microsoft.Playwright.dll"
        Add-PwRefTest "netstandard.dll"
        Add-PwRefTest "Microsoft.Bcl.AsyncInterfaces.dll"
        Add-PwRefTest "System.Buffers.dll"
        Add-PwRefTest "System.ComponentModel.Annotations.dll"
        Add-PwRefTest "System.Memory.dll"
        Add-PwRefTest "System.Numerics.Vectors.dll"
        Add-PwRefTest "System.Runtime.CompilerServices.Unsafe.dll"
        Add-PwRefTest "System.Text.Encodings.Web.dll"
        Add-PwRefTest "System.Text.Json.dll"
        Add-PwRefTest "System.Threading.Tasks.Extensions.dll"
        if (-not (Get-ChildItem -Path $packagesDir -Directory -Filter "Microsoft.Playwright*" -ErrorAction SilentlyContinue)) {
            Write-Step "ERROR: Microsoft.Playwright not restored. Run: nuget restore src/packages.config" Red
            Save-Verification 1
            exit 1
        }
        $compileDefines += "/define:PLAYWRIGHT"
        if ($pwRefs.Count -gt 0) { $Refs = ($Refs, ($pwRefs -join ";")) -join ";" }
    }

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
    $compileOutput = & $Csc /nologo /target:exe /out:$Out /main:TestRunner /reference:$Refs $compileDefines $Src 2>&1
    foreach ($line in $compileOutput) { Write-Step ([Convert]::ToString($line)) Gray }
    if ($LASTEXITCODE -ne 0) { throw "Test compilation failed" }

    # Copy Playwright + transitive DLLs next to the test EXE so the validation-only
    # tests can JIT the browser client without a full browser install.
    $testExeDir = Split-Path -Parent $Out
    foreach ($dll in $pwRefs) { Copy-Item $dll $testExeDir -Force }

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

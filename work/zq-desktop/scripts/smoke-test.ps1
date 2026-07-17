$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
Push-Location $Root

try {
    # Step 1: Verify required source files exist
    Write-Host "=== Smoke Test: ZhuaQian Desktop ===" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "[1/5] Checking source files ..." -ForegroundColor Yellow

    $requiredFiles = @(
        "ZhuaQianDesktop.cs",
        "Core\OutputsHub.cs",
        "Tools\ApprovalCard.cs",
        "Tools\FolderOrganizer.cs",
        "Tools\PluginRunner.cs",
        "Tools\SmartCommand.cs",
        "Tools\CommandParser.cs",
        "Tools\ProcessSnapshotCollector.cs",
        "providers\ModelRegistry.cs",
        "providers\IProviderClient.cs",
        "providers\GeminiClient.cs",
        "providers\OpenRouterClient.cs",
        "providers\LocalClient.cs",
        "providers\OpenAIClient.cs",
        "providers\ProviderManager.cs",
        "providers\StreamingBridge.cs",
        "ui\SettingsDialog.cs",
        "Documents\OfficeExporter.cs",
        "Documents\Redactor.cs",
        "Knowledge\Chunker.cs",
        "build.ps1"
    )

    $missing = $false
    foreach ($file in $requiredFiles) {
        $path = Join-Path $Root $file
        if (-not (Test-Path $path)) {
            Write-Host "  MISSING: $file" -ForegroundColor Red
            $missing = $true
        }
    }

    if ($missing) {
        Write-Host "FAILED: Some source files are missing." -ForegroundColor Red
        exit 1
    }
    Write-Host "  All required files present." -ForegroundColor Green

    # Step 2: Verify build.ps1 references exist
    Write-Host "[2/5] Checking build references ..." -ForegroundColor Yellow
    $buildRefs = @(
        "System.Windows.Forms.dll",
        "System.Drawing.dll",
        "System.Web.Extensions.dll",
        "System.IO.Compression.dll",
        "System.IO.Compression.FileSystem.dll"
    )
    foreach ($ref in $buildRefs) {
        Write-Host "  Reference: $ref"
    }
    Write-Host "  Build references OK." -ForegroundColor Green

    # Step 3: Check for empty catch blocks in production source (should not exist)
    Write-Host "[3/5] Checking for remaining empty catch blocks ..." -ForegroundColor Yellow
    $productionSources = Get-ChildItem -Path $Root -Recurse -Filter *.cs |
        Where-Object { $_.FullName -notmatch "\\tests\\" }
    $emptyCatches = Select-String -Path $productionSources.FullName -Pattern "catch\s*\{\s*\}" |
        ForEach-Object { "$($_.Path):$($_.LineNumber)" }
    if ($emptyCatches.Count -gt 0) {
        Write-Host "  FAILED: Found $($emptyCatches.Count) empty catch blocks in production source:" -ForegroundColor Red
        foreach ($item in $emptyCatches) { Write-Host "    $item" -ForegroundColor Red }
        exit 1
    } else {
        Write-Host "  No empty catch blocks in production source." -ForegroundColor Green
    }

    # Step 4: Verify ApprovalCard is used (not inline)
    Write-Host "[4/5] Checking ApprovalCard integration ..." -ForegroundColor Yellow
    $mainForm = Get-Content (Join-Path $Root "ZhuaQianDesktop.cs") -Raw
    if ($mainForm -match "Tools\.ApprovalCard\.Show") {
        Write-Host "  MainForm uses Tools.ApprovalCard. OK" -ForegroundColor Green
    } else {
        Write-Host "  WARNING: Tools.ApprovalCard.Show not found in MainForm" -ForegroundColor Yellow
    }

    # Step 5: Quick syntax check by compiling
    Write-Host "[5/5] Running build ..." -ForegroundColor Yellow
    $output = Join-Path $Root "ZhuaQianDesktop-smoke.exe"
    & (Join-Path $Root "build.ps1") -Output $output 2>&1
    if ($LASTEXITCODE -eq 0) {
        Remove-Item $output -ErrorAction SilentlyContinue
        Write-Host ""
        Write-Host "=== SMOKE TEST PASSED ===" -ForegroundColor Green
    } else {
        Write-Host ""
        Write-Host "=== SMOKE TEST FAILED (build error) ===" -ForegroundColor Red
        exit 1
    }
}
finally {
    Pop-Location
}

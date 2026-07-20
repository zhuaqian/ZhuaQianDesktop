param(
    [string]$Output = ""
)

# Default output: <repo-root>/dist/ZhuaQianDesktop.exe (kept out of git via .gitignore /dist/).
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrEmpty($Output)) { $Output = Join-Path $repoRoot "dist\ZhuaQianDesktop.exe" }

# Resolve csc.exe across common .NET Framework locations so the same script works
# on a dev machine and on the GitHub Actions windows-latest runner (instead of a
# hard-coded path that may not exist on either).
$cscCandidates = @(
    "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)
$csc = $cscCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) { Write-Host "ERROR: csc.exe not found under $env:WINDIR\Microsoft.NET\Framework*\v4.0.30319\" -ForegroundColor Red; exit 1 }
# Dynamic enumeration: always in sync with the filesystem and with csproj.
# Excludes test files and the three legacy/dead duplicate files that would
# otherwise cause CS0101/CS0262/CS0017 if compiled into the EXE:
#   Program.cs        -> duplicate entry point (ZhuaQianDesktop.cs is the EXE entry)
#   MainForm.cs        -> non-partial MainForm clashes with the partial MainForm split
#   TaskInfo.cs        -> duplicate of the TaskInfo type already defined in ZhuaQianDesktop.cs
$deadFiles = @('Program.cs', 'MainForm.cs', 'TaskInfo.cs')
$src = @(Get-ChildItem -Path $PSScriptRoot -Recurse -Filter *.cs |
    Where-Object {
        ($_.FullName -notmatch '[\\/]tests[\\/]') -and
        ($deadFiles -notcontains $_.Name)
    } |
    ForEach-Object { $_.FullName })

$refs = @(
    "System.Windows.Forms.dll"
    "System.Drawing.dll"
    "System.Web.Extensions.dll"
    "System.Security.dll"
    "System.IO.Compression.dll"
    "System.IO.Compression.FileSystem.dll"
    "System.Net.Http.dll"
    "System.Xml.dll"
)

# --- Optional Playwright for .NET --------------------------------------------
# The default raw-csc build keeps browser rendering disabled because this
# package targets netstandard2.0 and some Windows machines lack the matching
# .NET Framework facade assemblies. Set ZQ_ENABLE_PLAYWRIGHT=1 in an SDK/MSBuild
# environment to compile the full browser-rendering feature.
$packagesDir = Join-Path $PSScriptRoot "packages"
$pwRefs = New-Object System.Collections.Generic.List[string]
function Add-PwRef($name) {
    $candidates = @(Get-ChildItem -Path $packagesDir -Recurse -Filter $name -ErrorAction SilentlyContinue)
    $dll = $null
    foreach ($tfm in @("\lib\net48\", "\lib\net472\", "\lib\net471\", "\lib\net47\", "\lib\net462\", "\lib\net461\", "\lib\netstandard2.0\", "\build\netstandard2.0\ref\")) {
        $dll = $candidates | Where-Object { $_.FullName.Contains($tfm) } | Sort-Object FullName -Descending | Select-Object -First 1
        if ($dll) { break }
    }
    if (-not $dll) { $dll = $candidates | Sort-Object FullName -Descending | Select-Object -First 1 }
    if ($dll -and -not ($pwRefs.Contains($dll.FullName))) { $pwRefs.Add($dll.FullName) }
}
if ($env:ZQ_ENABLE_PLAYWRIGHT -eq "1") {
    Add-PwRef "Microsoft.Playwright.dll"
    Add-PwRef "netstandard.dll"
    Add-PwRef "Microsoft.Bcl.AsyncInterfaces.dll"
    Add-PwRef "System.Buffers.dll"
    Add-PwRef "System.ComponentModel.Annotations.dll"
    Add-PwRef "System.Memory.dll"
    Add-PwRef "System.Numerics.Vectors.dll"
    Add-PwRef "System.Runtime.CompilerServices.Unsafe.dll"
    Add-PwRef "System.Text.Encodings.Web.dll"
    Add-PwRef "System.Text.Json.dll"
    Add-PwRef "System.Threading.Tasks.Extensions.dll"
    if (-not (Get-ChildItem -Path $packagesDir -Directory -Filter "Microsoft.Playwright*" -ErrorAction SilentlyContinue)) {
        Write-Host "ERROR: Microsoft.Playwright NuGet package not found in $packagesDir" -ForegroundColor Red
        Write-Host "Run: nuget restore src/packages.config   (then re-run build.ps1)" -ForegroundColor Yellow
        Write-Host "Browsers install automatically on first browser fetch." -ForegroundColor Yellow
        exit 1
    }
    $refs = $refs + $pwRefs.ToArray()
    $src = @("/define:PLAYWRIGHT") + $src
}

$argsList = @(
    "/target:winexe"
    "/out:$Output"
    "/nologo"
    "/reference:$($refs -join ';')"
    $src
)

# Ensure the output directory exists (csc will not create it).
$outDir = Split-Path -Parent $Output
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }

Write-Host "Compiling $Output ..."
Push-Location $PSScriptRoot
try {
    & $csc $argsList

    if ($LASTEXITCODE -eq 0) {
        # Copy Playwright + transitive runtime DLLs and the native driver next to
        # the EXE so the app runs without Visual Studio.
        $outDir = Split-Path -Parent (Resolve-Path $Output)
        foreach ($dll in $pwRefs) { Copy-Item $dll $outDir -Force }
        Get-ChildItem -Path $packagesDir -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match "[\\/]runtimes[\\/]win-" } |
            ForEach-Object { Copy-Item $_.FullName $outDir -Force }
        Write-Host "Build OK: $Output" -ForegroundColor Green
    } else {
        Write-Host "Build FAILED" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}

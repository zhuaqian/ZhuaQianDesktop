param(
    [string]$Output = "ZhuaQianDesktop.exe"
)

$csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
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
)

# --- Playwright for .NET (first external NuGet dependency) -------------------
# Resolve the managed DLL + its transitive deps from the restored packages folder
# so the raw-csc build can compile/run the browser-rendering feature.
$packagesDir = Join-Path $PSScriptRoot "packages"
$pwRefs = New-Object System.Collections.Generic.List[string]
function Add-PwRef($name) {
    $dll = Get-ChildItem -Path $packagesDir -Recurse -Filter $name -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($dll -and -not ($pwRefs.Contains($dll.FullName))) { $pwRefs.Add($dll.FullName) }
}
Add-PwRef "Microsoft.Playwright.dll"
Add-PwRef "System.Text.Json.dll"
Add-PwRef "Microsoft.Bcl.AsyncInterfaces.dll"
Add-PwRef "System.Runtime.CompilerServices.Unsafe.dll"
Add-PwRef "System.Threading.Tasks.Extensions.dll"
if (-not (Get-ChildItem -Path $packagesDir -Directory -Filter "Microsoft.Playwright*" -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: Microsoft.Playwright NuGet package not found in $packagesDir" -ForegroundColor Red
    Write-Host "Run: nuget restore src/packages.config   (then re-run build.ps1)" -ForegroundColor Yellow
    Write-Host "Browsers install automatically on first browser fetch." -ForegroundColor Yellow
    exit 1
}
$refs = $refs + $pwRefs.ToArray()

$argsList = @(
    "/target:winexe"
    "/out:$Output"
    "/nologo"
    "/reference:$($refs -join ';')"
    $src
)

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

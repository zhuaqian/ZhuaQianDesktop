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
        Write-Host "Build OK: $Output" -ForegroundColor Green
    } else {
        Write-Host "Build FAILED" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}
finally {
    Pop-Location
}

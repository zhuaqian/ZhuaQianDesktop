$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = Split-Path -Parent $ScriptDir
$failures = New-Object System.Collections.Generic.List[string]

$nestedOutputs = Get-ChildItem -Path $Root -Recurse -Directory -Filter outputs -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -ne (Join-Path $Root "outputs") }
foreach ($dir in $nestedOutputs) {
    $failures.Add("Nested outputs directory found: " + $dir.FullName)
}

$zip = Join-Path (Split-Path -Parent $Root) "outputs\ZhuaQianDesktop-open-source.zip"
if (Test-Path $zip) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path $zip))
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -like "outputs/*" -or $entry.FullName -like "outputs\*") {
                $failures.Add("Zip contains nested outputs entry: " + $entry.FullName)
                break
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

if ($failures.Count -gt 0) {
    Write-Output "PACKAGE CHECK FAILED"
    foreach ($failure in $failures) { Write-Output "  $failure" }
    exit 1
}

Write-Output "Package checks passed."

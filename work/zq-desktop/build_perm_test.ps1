param(
    [string]$Output = "Tests\PermissionEngineTest.exe"
)

$csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"

$src = @(
    "Tests\PermissionEngineTest.cs"
    "Core\PermissionGate.cs"
)

$refs = @(
    "System.dll"
    "System.Core.dll"
    "System.Web.Extensions.dll"
)

$argsList = @(
    "/target:exe"
    "/out:$Output"
    "/nologo"
    "/reference:$($refs -join ';')"
    $src
)

Write-Host "Compiling permission engine test $Output ..."
& $csc $argsList
$cscExit = $LASTEXITCODE

if ($cscExit -eq 0) {
    Write-Host "Build OK: $Output" -ForegroundColor Green
    & $Output
    $runExit = $LASTEXITCODE
    if ($runExit -eq 0) { Write-Host "PERMISSION ENGINE TEST PASSED" -ForegroundColor Green } else { Write-Host "PERMISSION ENGINE TEST FAILED" -ForegroundColor Red; exit $runExit }
} else {
    Write-Host "Build FAILED" -ForegroundColor Red
    exit $cscExit
}

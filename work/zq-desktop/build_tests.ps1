param(
    [string]$Output = "tests\SelfTest.exe"
)

$csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"

# SelfTest is fully self-contained (inline mirrors of the module logic) so the
# build is deterministic regardless of on-disk module reverts.
$src = @(
    "tests\SelfTest.cs"
)

$refs = @(
    "System.Windows.Forms.dll"
    "System.Drawing.dll"
    "System.Web.Extensions.dll"
    "System.IO.Compression.dll"
    "System.IO.Compression.FileSystem.dll"
)

$argsList = @(
    "/target:exe"
    "/out:$Output"
    "/nologo"
    "/reference:$($refs -join ';')"
    $src
)

Write-Host "Compiling self-test $Output ..."
& $csc $argsList
$cscExit = $LASTEXITCODE

if ($cscExit -eq 0) {
    Write-Host "Build OK: $Output" -ForegroundColor Green
    & $Output
    $runExit = $LASTEXITCODE
    if ($runExit -eq 0) { Write-Host "SELFTEST PASSED" -ForegroundColor Green } else { Write-Host "SELFTEST FAILED" -ForegroundColor Red; exit $runExit }
} else {
    Write-Host "Build FAILED" -ForegroundColor Red
    exit $cscExit
}

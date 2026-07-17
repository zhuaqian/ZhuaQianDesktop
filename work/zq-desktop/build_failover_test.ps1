param(
    [string]$Output = "tests\FailoverTest.exe"
)

$csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"

$src = @(
    "tests\FailoverTest.cs"
    "providers\IProviderClient.cs"
    "providers\ModelRegistry.cs"
    "providers\ProviderManager.cs"
    "providers\GeminiClient.cs"
    "providers\OpenRouterClient.cs"
    "providers\OpenAIClient.cs"
    "providers\LocalClient.cs"
    "providers\TencentWorkBuddyClient.cs"
    "providers\AlibabaQianwenClient.cs"
    "providers\ZhipuAIGLMClient.cs"
)

$refs = @(
    "System.dll"
    "System.Core.dll"
    "System.Net.Http.dll"
    "System.Web.Extensions.dll"
    "System.Xml.dll"
    "System.Xml.Linq.dll"
)

$argsList = @(
    "/target:exe"
    "/out:$Output"
    "/nologo"
    "/reference:$($refs -join ';')"
    $src
)

Write-Host "Compiling failover test $Output ..."
& $csc $argsList
$cscExit = $LASTEXITCODE

if ($cscExit -eq 0) {
    Write-Host "Build OK: $Output" -ForegroundColor Green
    & $Output
    $runExit = $LASTEXITCODE
    if ($runExit -eq 0) { Write-Host "FAILOVER TEST PASSED" -ForegroundColor Green } else { Write-Host "FAILOVER TEST FAILED" -ForegroundColor Red; exit $runExit }
} else {
    Write-Host "Build FAILED" -ForegroundColor Red
    exit $cscExit
}

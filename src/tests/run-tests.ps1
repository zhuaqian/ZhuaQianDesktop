# Test configuration file for xUnit tests
# This should be updated based on the actual test structure

<GlobalJson>
{
  "version": "8.0.0",
  "projects": [
    "../tests/ZhuaQianDesktop.Tests/ZhuaQianDesktop.Tests.csproj"
  ]
}
</GlobalJson>

# Test runner configuration
# Create a simple test runner script
$testScript = @"
# Test runner script
tests\test-runner.ps1

param(
    [string]$Filter = "*"
)

# Load xUnit test runner
Write-Host "Loading xUnit tests with filter: $Filter"
# This will be populated when xUnit test project is created
"@"

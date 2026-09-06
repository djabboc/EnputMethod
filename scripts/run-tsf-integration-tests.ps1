param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "EnputMethod.Tsf.IntegrationTests\EnputMethod.Tsf.IntegrationTests.csproj"
$output = Join-Path $projectRoot "EnputMethod.Tsf.IntegrationTests\bin\$Configuration\net9.0-windows\EnputMethod.Tsf.IntegrationTests.exe"

dotnet build $project --configuration $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "TSF integration test build failed with exit code $LASTEXITCODE." }

$testExitCode = 2
for ($attempt = 1; $attempt -le 3 -and $testExitCode -eq 2; $attempt++) {
    & $output
    $testExitCode = $LASTEXITCODE
    if ($testExitCode -eq 2 -and $attempt -lt 3) {
        Write-Warning "The integration-test host lost foreground focus; retrying after user input settles (attempt $($attempt + 1)/3)."
        Start-Sleep -Seconds 2
    }
}
if ($testExitCode -ne 0) { throw "Installed TSF integration tests failed with exit code $testExitCode." }

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

& $output
if ($LASTEXITCODE -ne 0) { throw "Installed TSF integration tests failed with exit code $LASTEXITCODE." }

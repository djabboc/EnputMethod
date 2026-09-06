param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "run-tsf-tests.ps1") -Configuration $Configuration
dotnet build (Join-Path $projectRoot "EnputMethod.Overlay\EnputMethod.Overlay.csproj") -c $Configuration -p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw "Overlay build failed with exit code $LASTEXITCODE." }

dotnet run --project (Join-Path $projectRoot "EnputMethod.Overlay.ProtocolTests\EnputMethod.Overlay.ProtocolTests.csproj") -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Overlay protocol tests failed with exit code $LASTEXITCODE." }
$overlayExecutable = Join-Path $projectRoot "EnputMethod.Overlay\bin\x64\$Configuration\net9.0-windows\EnputMethod.Overlay.exe"
dotnet run --project (Join-Path $projectRoot "EnputMethod.Overlay.AutomationTests\EnputMethod.Overlay.AutomationTests.csproj") -c $Configuration -- $overlayExecutable
if ($LASTEXITCODE -ne 0) { throw "Overlay automation tests failed with exit code $LASTEXITCODE." }

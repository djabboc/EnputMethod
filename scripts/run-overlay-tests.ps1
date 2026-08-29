param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "run-tsf-tests.ps1") -Configuration $Configuration
dotnet build (Join-Path $projectRoot "EnputMethod.Overlay\EnputMethod.Overlay.csproj") -c $Configuration -p:Platform=x64

dotnet run --project (Join-Path $projectRoot "EnputMethod.Overlay.ProtocolTests\EnputMethod.Overlay.ProtocolTests.csproj") -c $Configuration
dotnet run --project (Join-Path $projectRoot "EnputMethod.Overlay.AutomationTests\EnputMethod.Overlay.AutomationTests.csproj") -c $Configuration

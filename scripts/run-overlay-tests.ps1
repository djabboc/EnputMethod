param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot

dotnet run --project (Join-Path $projectRoot "EnputMethod.Overlay.ProtocolTests\EnputMethod.Overlay.ProtocolTests.csproj") -c $Configuration
dotnet run --project (Join-Path $projectRoot "EnputMethod.Overlay.AutomationTests\EnputMethod.Overlay.AutomationTests.csproj") -c $Configuration

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "EnputMethod.Overlay.AutomationTests\EnputMethod.Overlay.AutomationTests.csproj"

# This explicit acceptance test temporarily moves the real pointer and uses the clipboard.
dotnet run --project $project -c $Configuration -- --real-input
if ($LASTEXITCODE -ne 0) {
    throw "Real overlay mouse-input acceptance test failed with exit code $LASTEXITCODE."
}

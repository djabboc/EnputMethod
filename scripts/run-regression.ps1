param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "build-local-package.ps1") -Configuration $Configuration
& (Join-Path $PSScriptRoot "run-overlay-tests.ps1") -Configuration $Configuration
$installer = Join-Path $projectRoot "artifacts\local\$Configuration\Install Enput Method.exe"
$uninstaller = Join-Path $projectRoot "artifacts\local\$Configuration\Uninstall Enput Method.exe"
& (Join-Path $PSScriptRoot "install-and-verify.ps1") -Configuration $Configuration -InstallerPath $installer
& (Join-Path $PSScriptRoot "uninstall-and-verify.ps1") -Configuration $Configuration -UninstallerPath $uninstaller
& (Join-Path $PSScriptRoot "install-and-verify.ps1") -Configuration $Configuration -InstallerPath $installer

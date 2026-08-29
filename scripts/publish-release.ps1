param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "build-local-package.ps1") -Configuration Release
if (-not $?) { throw "Release local package build failed." }

$name = "EnputMethod-$Version-win-x64"
$destination = Join-Path $projectRoot "artifacts\release\$name"
if (Test-Path -LiteralPath $destination) { throw "Release directory already exists and will not be overwritten: $destination" }
New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot "artifacts\local\Release") -Destination $destination -Recurse

$readme = @"
Enput Method $Version (Windows x64)

1. Keep this directory intact while you install or uninstall.
2. Run "Install Enput Method.exe" and approve Windows UAC.
3. Close and reopen target editors before testing the updated input method.
4. To remove Enput, close target editors, run "Uninstall Enput Method.exe", then delete this directory.

Installed binaries and static resources are deployed to Program Files\Enput Method.
Your configuration and learned ranking are retained under LocalAppData\Enput Method\UserData and are not overwritten by an upgrade.
"@
Set-Content -LiteralPath (Join-Path $destination "README.txt") -Value $readme -Encoding utf8NoBOM

$installer = Join-Path $destination "Install Enput Method.exe"
$verification = Start-Process -FilePath $installer -ArgumentList "--verify-package" -Wait -PassThru
if ($verification.ExitCode -ne 0) { throw "Release package verification failed with exit code $($verification.ExitCode)." }

if ($Zip) {
    $zipPath = Join-Path $projectRoot "artifacts\release\$name.zip"
    if (Test-Path -LiteralPath $zipPath) { throw "Release archive already exists and will not be overwritten: $zipPath" }
    Compress-Archive -LiteralPath $destination -DestinationPath $zipPath
    Write-Host "Release archive created: $zipPath"
}
Write-Host "Release directory created: $destination"
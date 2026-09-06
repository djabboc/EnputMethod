param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$ResetCandidateFrequency
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$msbuild = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path -LiteralPath $msbuild)) { throw "Visual Studio MSBuild was not found: $msbuild" }

function Invoke-ProcessAndWait {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$ArgumentList,
        [Parameter(Mandatory)] [string]$WorkingDirectory
    )

    Push-Location -LiteralPath $WorkingDirectory
    try {
        & $FilePath @ArgumentList | Out-Host
        return $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}

$destination = Join-Path $projectRoot "artifacts\local\$Configuration"
if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Recurse -Force }
New-Item -ItemType Directory -Path $destination | Out-Null

foreach ($project in "EnputMethod.Installer\EnputMethod.Installer.csproj", "EnputMethod.Uninstaller\EnputMethod.Uninstaller.csproj") {
    $exitCode = Invoke-ProcessAndWait -FilePath $msbuild -ArgumentList @(
        (Join-Path $projectRoot $project), "/t:Rebuild", "/m", "/nr:false", "/nologo", "/verbosity:minimal", "/p:Configuration=$Configuration", "/p:Platform=x64"
    ) -WorkingDirectory $projectRoot
    if ($exitCode -ne 0) { throw "Build failed for $project with exit code $exitCode." }
}

$installerOutput = Join-Path $projectRoot "EnputMethod.Installer\bin\x64\$Configuration\net9.0-windows"
$uninstallerOutput = Join-Path $projectRoot "EnputMethod.Uninstaller\bin\x64\$Configuration\net9.0-windows"
foreach ($file in "EnputMethod.Installer.dll", "EnputMethod.Installer.deps.json", "EnputMethod.Installer.runtimeconfig.json") {
    Copy-Item -LiteralPath (Join-Path $installerOutput $file) -Destination $destination
}
Copy-Item -LiteralPath (Join-Path $installerOutput "EnputMethod.Installer.exe") -Destination (Join-Path $destination "Install Enput Method.exe")
foreach ($file in "EnputMethod.Uninstaller.dll", "EnputMethod.Uninstaller.deps.json", "EnputMethod.Uninstaller.runtimeconfig.json") {
    Copy-Item -LiteralPath (Join-Path $uninstallerOutput $file) -Destination $destination
}
Copy-Item -LiteralPath (Join-Path $uninstallerOutput "EnputMethod.Uninstaller.exe") -Destination (Join-Path $destination "Uninstall Enput Method.exe")
Copy-Item -LiteralPath (Join-Path $installerOutput "payload") -Destination (Join-Path $destination "payload") -Recurse

$installer = Join-Path $destination "Install Enput Method.exe"
$verification = Start-Process -FilePath $installer -ArgumentList "--verify-package" -Verb RunAs -Wait -PassThru
if ($verification.ExitCode -ne 0) { throw "Local package verification failed with exit code $($verification.ExitCode)." }
if ($ResetCandidateFrequency) {
    & (Join-Path $PSScriptRoot "reset-candidate-frequency.ps1") -Confirm:$false
}
Write-Host "Local $Configuration package created: $destination"

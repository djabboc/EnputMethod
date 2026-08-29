param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$InstallerPath = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$installer = if ([string]::IsNullOrWhiteSpace($InstallerPath)) {
    Join-Path $projectRoot "EnputMethod.Installer\bin\x64\$Configuration\net9.0-windows\EnputMethod.Installer.exe"
} else {
    [IO.Path]::GetFullPath($InstallerPath)
}
$verificationLog = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "Enput Method\UserData\install-verification.log"
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Build the x64 installer before running system installation verification: $installer"
}

$userData = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "Enput Method\UserData"
$configPath = Join-Path $userData "config.json"
$shortcutPath = Join-Path $userData "shortcut.json"
$configHashBefore = if (Test-Path -LiteralPath $configPath) { (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash } else { $null }
$shortcutHashBefore = if (Test-Path -LiteralPath $shortcutPath) { (Get-FileHash -LiteralPath $shortcutPath -Algorithm SHA256).Hash } else { $null }

$process = Start-Process -FilePath $installer -ArgumentList "--install-and-verify" -Verb RunAs -Wait -PassThru
if ($process.ExitCode -ne 0) {
    $diagnostic = if (Test-Path -LiteralPath $verificationLog) { Get-Content -LiteralPath $verificationLog -Raw } else { "No verification log was written." }
    throw "Installation verification failed with exit code $($process.ExitCode).`n$diagnostic"
}

if (-not (Test-Path -LiteralPath $configPath) -or -not (Test-Path -LiteralPath $shortcutPath)) {
    throw "Installation did not initialize the required UserData configuration files."
}
if ($null -ne $configHashBefore -and $configHashBefore -ne (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash) {
    throw "Installation unexpectedly overwrote the existing UserData config.json."
}
if ($null -ne $shortcutHashBefore -and $shortcutHashBefore -ne (Get-FileHash -LiteralPath $shortcutPath -Algorithm SHA256).Hash) {
    throw "Installation unexpectedly overwrote the existing UserData shortcut.json."
}
if ($null -eq $configHashBefore) {
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    if ($config.fontSize -ne 18) { throw "Installed default font size was not initialized to 18." }
}

$lexiconVerification = Start-Process -FilePath $installer -ArgumentList "--verify-lexicon" -Wait -PassThru
if ($lexiconVerification.ExitCode -ne 0) {
    $diagnostic = if (Test-Path -LiteralPath $verificationLog) { Get-Content -LiteralPath $verificationLog -Raw } else { "No SQLite verification log was written." }
    throw "SQLite lexicon verification failed with exit code $($lexiconVerification.ExitCode).`n$diagnostic"
}

$staticDirectory = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) "Enput Method\Resources"
foreach ($file in "enput.db", "enput.db.ready", "themes\dark.json", "wordnet-phrases.txt") {
    if (-not (Test-Path -LiteralPath (Join-Path $staticDirectory $file))) { throw "Installed static resource is missing: $file" }
}
Write-Host "Installation, static-resource, and SQLite lexicon verification passed."
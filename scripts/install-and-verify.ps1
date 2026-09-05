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
$userDataRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "Enput Method\UserData"
$verificationLog = Join-Path $userDataRoot "install-verification.log"
$lexiconVerificationLog = Join-Path $userDataRoot "lexicon-verification.log"
if (-not (Test-Path -LiteralPath $installer)) {
    throw "Build the x64 installer before running system installation verification: $installer"
}

$userData = $userDataRoot
$configPath = Join-Path $userData "config.json"
$shortcutPath = Join-Path $userData "shortcut.json"
$configHashBefore = if (Test-Path -LiteralPath $configPath) { (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash } else { $null }
$shortcutBefore = if (Test-Path -LiteralPath $shortcutPath) { Get-Content -LiteralPath $shortcutPath -Raw | ConvertFrom-Json } else { $null }

$process = Start-Process -FilePath $installer -ArgumentList "--install-and-verify" -Verb RunAs -Wait -PassThru
if ($process.ExitCode -ne 0) {
    $diagnostic = if (Test-Path -LiteralPath $verificationLog) { Get-Content -LiteralPath $verificationLog -Raw } else { "No verification log was written." }
    throw "Installation verification failed with exit code $($process.ExitCode).
$diagnostic"
}

if (-not (Test-Path -LiteralPath $configPath) -or -not (Test-Path -LiteralPath $shortcutPath)) {
    throw "Installation did not initialize the required UserData configuration files."
}
if ($null -ne $configHashBefore -and $configHashBefore -ne (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash) {
    throw "Installation unexpectedly overwrote the existing UserData config.json."
}
if ($null -ne $shortcutBefore) {
    $shortcutAfter = Get-Content -LiteralPath $shortcutPath -Raw | ConvertFrom-Json
    foreach ($property in $shortcutBefore.PSObject.Properties) {
        $afterProperty = $shortcutAfter.PSObject.Properties[$property.Name]
        if ($null -eq $afterProperty) { throw "Installation removed existing UserData shortcut setting: $($property.Name)." }
        $beforeValue = $property.Value | ConvertTo-Json -Depth 16 -Compress
        $afterValue = $afterProperty.Value | ConvertTo-Json -Depth 16 -Compress
        if ($beforeValue -ne $afterValue) { throw "Installation overwrote existing UserData shortcut setting: $($property.Name)." }
    }
}
if ($null -eq $shortcutAfter) { $shortcutAfter = Get-Content -LiteralPath $shortcutPath -Raw | ConvertFrom-Json }
if ($shortcutAfter.bypassCandidateSelectionModifiers.Count -lt 1) {
    throw "Installation did not initialize bypassCandidateSelectionModifiers in UserData shortcut.json."
}
if ($null -eq $configHashBefore) {
    $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
    if ($config.fontSize -ne 18) { throw "Installed default font size was not initialized to 18." }
}

$lexiconVerification = Start-Process -FilePath $installer -ArgumentList "--verify-lexicon" -Wait -PassThru
if ($lexiconVerification.ExitCode -ne 0) {
    $diagnostic = if (Test-Path -LiteralPath $lexiconVerificationLog) { Get-Content -LiteralPath $lexiconVerificationLog -Raw } else { "No SQLite verification log was written." }
    throw "SQLite lexicon verification failed with exit code $($lexiconVerification.ExitCode).
$diagnostic"
}

$staticDirectory = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) "Enput Method\Resources"
foreach ($file in "enput.db", "enput.db.ready", "themes\dark.json", "wordnet-phrases.txt") {
    if (-not (Test-Path -LiteralPath (Join-Path $staticDirectory $file))) { throw "Installed static resource is missing: $file" }
}
Write-Host "Installation, static-resource, and SQLite lexicon verification passed."

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [string]$UninstallerPath = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$uninstaller = if ([string]::IsNullOrWhiteSpace($UninstallerPath)) {
    Join-Path $projectRoot "artifacts\local\$Configuration\Uninstall Enput Method.exe"
} else {
    [IO.Path]::GetFullPath($UninstallerPath)
}
if (-not (Test-Path -LiteralPath $uninstaller)) {
    throw "Build the x64 uninstaller before running system uninstallation verification: $uninstaller"
}

$textServiceClsid = "{9C8945D5-01DF-48F4-A8DB-57E8B6A1EB10}"
$userData = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "Enput Method\UserData"
$verificationLog = Join-Path $userData "uninstall-verification.log"
$programFilesProduct = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) "Enput Method"
$machineComRegistration = "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\$textServiceClsid\InprocServer32"
$installedTsfDll = (Get-Item -LiteralPath $machineComRegistration).GetValue("")
$preservedFiles = @(
    $installedTsfDll,
    (Join-Path $programFilesProduct "Overlay\EnputMethod.Overlay.exe"),
    (Join-Path $programFilesProduct "Resources\enput.db")
)
$missingBefore = @($preservedFiles | Where-Object { -not (Test-Path -LiteralPath $_) })
if ($missingBefore.Count -ne 0) {
    throw "Uninstall verification needs an installed product. Missing before uninstall: $($missingBefore -join ', ')"
}

$configPath = Join-Path $userData "config.json"
$configHashBefore = if (Test-Path -LiteralPath $configPath) { (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash } else { $null }
$registryPaths = @(
    "Registry::HKEY_CURRENT_USER\SOFTWARE\Classes\CLSID\$textServiceClsid",
    "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Classes\CLSID\$textServiceClsid",
    "Registry::HKEY_CURRENT_USER\SOFTWARE\Microsoft\CTF\TIP\$textServiceClsid",
    "Registry::HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\CTF\TIP\$textServiceClsid"
)

function Get-NonEnputInputMethodTips {
    $windowsPowerShell = Join-Path $env:SystemRoot "System32\WindowsPowerShell\v1.0\powershell.exe"
    $command = @'
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
Get-WinUserLanguageList |
    ForEach-Object { $_.InputMethodTips } |
    Where-Object { $_ -notmatch "\{9C8945D5-01DF-48F4-A8DB-57E8B6A1EB10\}" } |
    Sort-Object -Unique
'@
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $tips = & $windowsPowerShell -NoProfile -NonInteractive -EncodedCommand $encodedCommand
    if ($LASTEXITCODE -ne 0) { throw "Windows PowerShell could not read the current input-method list." }
    return @($tips)
}

$inputMethodsBefore = Get-NonEnputInputMethodTips
$process = Start-Process -FilePath $uninstaller -ArgumentList "--uninstall-and-verify" -Verb RunAs -Wait -PassThru
if ($process.ExitCode -ne 0) {
    $diagnostic = if (Test-Path -LiteralPath $verificationLog) { Get-Content -LiteralPath $verificationLog -Raw } else { "No verification log was written." }
    throw "Uninstallation verification failed with exit code $($process.ExitCode).
$diagnostic"
}

$remainingRegistration = @($registryPaths | Where-Object { Test-Path -LiteralPath $_ })
if ($remainingRegistration.Count -ne 0) {
    throw "Uninstallation left Enput TSF registration behind: $($remainingRegistration -join ', ')"
}
foreach ($file in $preservedFiles) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Uninstallation unexpectedly removed a shared product file: $file" }
}
if ($null -ne $configHashBefore -and $configHashBefore -ne (Get-FileHash -LiteralPath $configPath -Algorithm SHA256).Hash) {
    throw "Uninstallation unexpectedly modified the existing UserData config.json."
}

$inputMethodsAfter = Get-NonEnputInputMethodTips
$inputMethodDifference = @(Compare-Object -ReferenceObject $inputMethodsBefore -DifferenceObject $inputMethodsAfter)
if ($inputMethodDifference.Count -ne 0) {
    throw "Uninstallation unexpectedly changed a non-Enput Windows input method: $($inputMethodDifference | Out-String)"
}
Write-Host "Uninstallation removed Enput registration while preserving shared files, user data, and other input methods."

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $projectRoot "EnputMethod.Installer\bin\x64\$Configuration\net9.0-windows\EnputMethod.Installer.exe"
$verificationLog = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "Enput Method\install-verification.log"
if (-not (Test-Path -LiteralPath $installer))
{
    throw "Build the x64 installer before running system installation verification: $installer"
}

$process = Start-Process -FilePath $installer -ArgumentList "--install-and-verify" -Verb RunAs -Wait -PassThru
if ($process.ExitCode -ne 0)
{
    $diagnostic = if (Test-Path -LiteralPath $verificationLog) { Get-Content -LiteralPath $verificationLog -Raw } else { "No verification log was written." }
    throw "Installation verification failed with exit code $($process.ExitCode).`n$diagnostic"
}
$configPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "Enput Method\config.json"
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
if ($config.fontSize -ne 18) { throw "Installed default font size was not migrated to 18." }

$emojiPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "Enput Method\emoji.json"
$emoji = Get-Content -LiteralPath $emojiPath -Raw | ConvertFrom-Json
foreach ($keyword in "fire", "water", "bucket", "cat", "dog") {
    $preferred = @($emoji.entries | Where-Object { $_.priority -eq 100 -and $_.keywords -contains $keyword })
    if ($preferred.Count -ne 1) { throw "Installed Emoji dictionary is missing the preferred '$keyword' entry." }
}
Write-Host "Installation and system verification passed."

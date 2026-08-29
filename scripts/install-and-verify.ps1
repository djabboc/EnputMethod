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
Write-Host "Installation and system verification passed."

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

$lexiconVerification = Start-Process -FilePath $installer -ArgumentList "--verify-lexicon" -Wait -PassThru
if ($lexiconVerification.ExitCode -ne 0)
{
    $diagnostic = if (Test-Path -LiteralPath $verificationLog) { Get-Content -LiteralPath $verificationLog -Raw } else { "No SQLite verification log was written." }
    throw "SQLite lexicon verification failed with exit code $($lexiconVerification.ExitCode).`n$diagnostic"
}
Write-Host "Installation and SQLite lexicon verification passed."
return

$emojiPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "Enput Method\emoji.json"
$emoji = Get-Content -LiteralPath $emojiPath -Raw | ConvertFrom-Json
foreach ($keyword in "fire", "water", "bucket", "cat", "dog") {
    $preferred = @($emoji.entries | Where-Object { $_.priority -eq 100 -and $_.keywords -contains $keyword })
    if ($preferred.Count -ne 1) { throw "Installed Emoji dictionary is missing the preferred '$keyword' entry." }
}
Write-Host "Installation and system verification passed."

$suggestionPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "Enput Method\suggestions.json"
$suggestions = Get-Content -LiteralPath $suggestionPath -Raw | ConvertFrom-Json
$can = @($suggestions.entries | Where-Object { $_.text -ieq "can" })
if ($can.Count -ne 1 -or -not ($can[0].phrases -contains "can i help you?")) { throw "Installed suggestion dictionary is missing the Can I help you? continuation." }

$translationPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "Enput Method\translations.json"
$translations = Get-Content -LiteralPath $translationPath -Raw | ConvertFrom-Json
$braces = @($translations.entries | Where-Object { $_.text -ieq "braces" })
if ($braces.Count -ne 1 -or -not ($braces[0].translations.'zh-CN' -contains "牙套；牙齿矫正器")) { throw "Installed translation dictionary is missing the braces dental-appliance sense." }
$packageConfigPath = Join-Path (Split-Path -Parent $installer) "config.json"
$packageConfig = Get-Content -Raw -LiteralPath $packageConfigPath | ConvertFrom-Json
if ($packageConfig.translationLanguages -notcontains "en" -or $packageConfig.translationLanguages -notcontains "zh-CN" -or $packageConfig.translationLanguages -contains "ja-JP") { throw "Packaged default configuration must enable English and Chinese translations only." }
if ($packageConfig.translationWindowWidth -lt 260 -or $packageConfig.translationWindowHeight -lt 160) { throw "Packaged translation window dimensions are invalid." }
$saw = @($emoji.entries | Where-Object { $_.keywords -contains "saw" })
if ($saw.Count -lt 1 -or $saw[0].emoji -ne ([char]::ConvertFromUtf32(0x1FA9A))) { throw "Installed saw Emoji must be Unicode U+1FA9A." }
$saintHelena = @($emoji.entries | Where-Object { $_.keywords -contains "helena" })
if ($saintHelena.Count -ne 1 -or $saintHelena[0].emoji -ne (([char]::ConvertFromUtf32(0x1F1F8)) + ([char]::ConvertFromUtf32(0x1F1ED)))) { throw "Installed Saint Helena flag must use regional indicators S and H." }
$ccCedictIndexPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "Enput Method\translations.cc-cedict.jsonl"
if (-not (Test-Path -LiteralPath $ccCedictIndexPath) -or (Get-Item -LiteralPath $ccCedictIndexPath).Length -le 1024) { throw "CC-CEDICT supplemental translation index was not installed." }
if (-not (Select-String -LiteralPath $ccCedictIndexPath -SimpleMatch '"key":"braces"' -Quiet)) { throw "CC-CEDICT supplemental translation index is missing braces." }
$ccCedictAttributionPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "Enput Method\CC-CEDICT-ATTRIBUTION.txt"
if (-not (Test-Path -LiteralPath $ccCedictAttributionPath) -or -not ((Get-Content -LiteralPath $ccCedictAttributionPath -Raw) -match 'CC BY-SA 4.0')) { throw "CC-CEDICT attribution file is missing." }

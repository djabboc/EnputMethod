param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [switch]$NoZip,
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
if ($NoZip -and $Zip) { throw "-Zip and -NoZip cannot be used together." }

function Test-ReleaseArchive {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ArchivePath,
        [Parameter(Mandatory = $true)]
        [string]$ExpectedRootName
    )

    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("EnputMethod-release-" + [Guid]::NewGuid().ToString("N"))
    try {
        New-Item -ItemType Directory -Path $temporaryRoot | Out-Null
        Expand-Archive -LiteralPath $ArchivePath -DestinationPath $temporaryRoot
        $topLevel = @(Get-ChildItem -LiteralPath $temporaryRoot -Force)
        if ($topLevel.Count -ne 1 -or -not $topLevel[0].PSIsContainer -or $topLevel[0].Name -ne $ExpectedRootName) {
            throw "Release archive must contain exactly one top-level directory named $ExpectedRootName."
        }

        $installer = Join-Path $topLevel[0].FullName "Install Enput Method.exe"
        if (-not (Test-Path -LiteralPath $installer)) { throw "Release archive is missing Install Enput Method.exe." }
        $verification = Start-Process -FilePath $installer -ArgumentList "--verify-package" -WorkingDirectory $topLevel[0].FullName -Wait -PassThru
        if ($verification.ExitCode -ne 0) {
            throw "Extracted release archive package verification failed with exit code $($verification.ExitCode)."
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
    }
}

$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "build-local-package.ps1") -Configuration Release
if (-not $?) { throw "Release local package build failed." }

$name = "EnputMethod-$Version-win-x64"
$destination = Join-Path $projectRoot "artifacts\release\$name"
if (Test-Path -LiteralPath $destination) { throw "Release directory already exists and will not be overwritten: $destination" }
New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot "artifacts\local\Release") -Destination $destination -Recurse

$readme = @"
Enput Method $Version（Windows x64）

1. 安装或卸载前请保持此目录完整。
2. 运行“Install Enput Method.exe”，并接受 Windows UAC 提示。
3. 更新后，请关闭并重新打开目标编辑器再测试输入法。
4. 如需移除 Enput，请运行“Uninstall Enput Method.exe”。卸载仅注销输入法；已部署的运行资源和个人配置会保留，便于已打开应用继续运行和之后立即重装。卸载成功后可以删除此解压目录。

安装后的运行文件和静态资源位于 Program Files\Enput Method。
用户配置和学习排序保存在 LocalAppData\Enput Method\UserData；升级不会覆盖它们。
"@
Set-Content -LiteralPath (Join-Path $destination "README.txt") -Value $readme -Encoding utf8NoBOM

$installer = Join-Path $destination "Install Enput Method.exe"
$verification = Start-Process -FilePath $installer -ArgumentList "--verify-package" -WorkingDirectory $destination -Wait -PassThru
if ($verification.ExitCode -ne 0) { throw "Release package verification failed with exit code $($verification.ExitCode)." }

if (-not $NoZip) {
    $zipPath = Join-Path $projectRoot "artifacts\release\$name.zip"
    if (Test-Path -LiteralPath $zipPath) { throw "Release archive already exists and will not be overwritten: $zipPath" }
    Compress-Archive -LiteralPath $destination -DestinationPath $zipPath
    Test-ReleaseArchive -ArchivePath $zipPath -ExpectedRootName $name
    Write-Host "Release archive created and verified: $zipPath"
}
Write-Host "Release directory created: $destination"
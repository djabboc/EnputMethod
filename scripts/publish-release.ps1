param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$")]
    [string]$Version
)

$ErrorActionPreference = "Stop"

function Invoke-GitText {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = & git -C $projectRoot @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed: git $($Arguments -join ' ')`n$($output -join [Environment]::NewLine)"
    }
    return @($output | ForEach-Object { $_.ToString().TrimEnd() })
}

function Test-ReleaseSource {
    param([Parameter(Mandatory = $true)][string]$TagName)

    $branch = @((Invoke-GitText -Arguments @("branch", "--show-current")))[0]
    if ($branch -ne "main") { throw "A release can only be created from main. Current branch: $branch" }

    $changes = @(Invoke-GitText -Arguments @("status", "--porcelain=v1", "--untracked-files=all"))
    if ($changes.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace($changes[0])) {
        throw "The working tree must be clean before release. Commit, remove, or ignore: $($changes -join '; ')"
    }

    & git -C $projectRoot show-ref --verify --quiet "refs/tags/$TagName"
    if ($LASTEXITCODE -eq 0) { throw "Local Git tag already exists: $TagName" }
    if ($LASTEXITCODE -ne 1) { throw "Unable to check local Git tag: $TagName" }
    $forbiddenFiles = @(Invoke-GitText -Arguments @("ls-files") | Where-Object { $_ -match "(^|/)(\.env(\.[^/]+)?|\.npmrc|[^/]+\.(pem|key|pfx|p12))$" })
    if ($forbiddenFiles.Count -gt 0) {
        throw "Tracked files may contain sensitive material and cannot be released: $($forbiddenFiles -join ', ')"
    }

    $secretMatches = & git -C $projectRoot grep -n -I -i -E -e "-----BEGIN ([A-Z ]+ )?PRIVATE KEY-----|gh[pous]_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|(github_token|openai_api_key|aws_secret_access_key|password)[[:space:]]*[:=][[:space:]]*[^[:space:]]{8,}" -- 2>&1
    if ($LASTEXITCODE -gt 1) { throw "Git sensitive-content scan failed: $($secretMatches -join [Environment]::NewLine)" }
    if ($LASTEXITCODE -eq 0 -and $secretMatches.Count -gt 0) {
        throw "Potential credential content found. Remove it before release: $($secretMatches -join '; ')"
    }
}

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
$tagName = "v$Version"
Test-ReleaseSource -TagName $tagName

& (Join-Path $PSScriptRoot "build-local-package.ps1") -Configuration Release
if (-not $?) { throw "Release local package build failed." }

$name = "EnputMethod-$Version-win-x64"
$destination = Join-Path $projectRoot "artifacts\release\$name"
if (Test-Path -LiteralPath $destination) { throw "Release directory already exists and will not be overwritten: $destination" }
New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot "artifacts\local\Release") -Destination $destination -Recurse

$readmeLines = @(
    ('Enput Method {0}（Windows x64）' -f $Version),
    '',
    '1. 安装或卸载前请保持此目录完整。',
    '2. 运行“Install Enput Method.exe”，并接受 Windows UAC 提示。',
    '3. 此安装包不包含 .NET Runtime。若系统提示缺少 .NET 9 Desktop Runtime（x64），请按 Windows 提示完成安装后重试。',
    '4. 更新后，请关闭并重新打开目标编辑器再测试输入法。',
    '5. 如需移除 Enput，请运行“Uninstall Enput Method.exe”。卸载仅注销输入法；已部署的运行资源和个人配置会保留，便于已打开应用继续运行和之后立即重装。卸载成功后可以删除此解压目录。',
    '',
    '安装后的运行文件和静态资源位于 Program Files\Enput Method。',
    '用户配置和学习排序保存在 LocalAppData\Enput Method\UserData；升级不会覆盖它们。',
    ''
)
$readme = [string]::Join([Environment]::NewLine, [string[]]$readmeLines)
Set-Content -LiteralPath (Join-Path $destination "README.txt") -Value $readme -Encoding utf8NoBOM

$installer = Join-Path $destination "Install Enput Method.exe"
$verification = Start-Process -FilePath $installer -ArgumentList "--verify-package" -WorkingDirectory $destination -Wait -PassThru
if ($verification.ExitCode -ne 0) { throw "Release package verification failed with exit code $($verification.ExitCode)." }

$zipPath = Join-Path $projectRoot "artifacts\release\$name.zip"
if (Test-Path -LiteralPath $zipPath) { throw "Release archive already exists and will not be overwritten: $zipPath" }
Compress-Archive -LiteralPath $destination -DestinationPath $zipPath
Test-ReleaseArchive -ArchivePath $zipPath -ExpectedRootName $name

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = "$zipPath.sha256"
Set-Content -LiteralPath $checksumPath -Value "$hash *$([System.IO.Path]::GetFileName($zipPath))" -Encoding ascii

& git -C $projectRoot tag -a $tagName -m "Enput Method $Version"
if ($LASTEXITCODE -ne 0) { throw "Failed to create local Git tag: $tagName" }

Write-Host "Release directory created: $destination"
Write-Host "Release archive created and verified: $zipPath"
Write-Host "SHA-256 checksum created: $checksumPath"
Write-Host "Local Git tag created: $tagName"
Write-Host "No commit, push, or GitHub Release was created by this script."

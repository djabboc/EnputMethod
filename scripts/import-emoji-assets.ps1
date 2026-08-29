[CmdletBinding()]
param(
    [string]$EmojiDataPath,
    [string]$TwemojiArchivePath,
    [switch]$Download
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$installerDictionary = Join-Path $repoRoot 'EnputMethod.Installer\emoji.json'
$assetDirectory = Join-Path $repoRoot 'EnputMethod.Overlay\EmojiAssets'
$licensePath = Join-Path $repoRoot 'EnputMethod.Overlay\TWEMOJI-LICENSE.txt'
$cacheDirectory = Join-Path $env:LOCALAPPDATA 'Enput Method\SourceCache'

if ($Download) {
    New-Item -ItemType Directory -Force -Path $cacheDirectory | Out-Null
    $EmojiDataPath = Join-Path $cacheDirectory 'emoji-data-15.1.2.json'
    $TwemojiArchivePath = Join-Path $cacheDirectory 'twemoji-14.1.2.zip'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://raw.githubusercontent.com/iamcal/emoji-data/v15.1.2/emoji.json' -OutFile $EmojiDataPath
    Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/jdecked/twemoji/archive/refs/tags/v14.1.2.zip' -OutFile $TwemojiArchivePath
}

if ([string]::IsNullOrWhiteSpace($EmojiDataPath) -or [string]::IsNullOrWhiteSpace($TwemojiArchivePath)) {
    throw 'Provide -EmojiDataPath and -TwemojiArchivePath, or use -Download.'
}
if (!(Test-Path -LiteralPath $EmojiDataPath -PathType Leaf) -or !(Test-Path -LiteralPath $TwemojiArchivePath -PathType Leaf)) {
    throw 'The Emoji data JSON or Twemoji archive was not found.'
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $TwemojiArchivePath))
try {
    $pngEntries = @{}
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -match '/assets/72x72/([^/]+\.png)$') { $pngEntries[$Matches[1].ToLowerInvariant()] = $entry }
    }

    New-Item -ItemType Directory -Force -Path $assetDirectory | Out-Null
    Get-ChildItem -LiteralPath $assetDirectory -Filter '*.png' -File | Remove-Item -Force
    $catalog = Get-Content -Raw -LiteralPath $EmojiDataPath | ConvertFrom-Json
    $entries = [System.Collections.Generic.List[object]]::new()
    foreach ($record in $catalog) {
        if ([string]::IsNullOrWhiteSpace($record.unified)) { continue }
        $codes = @($record.unified.Split('-') | Where-Object { $_ -ne 'FE0F' })
        $fileName = (($codes -join '-').ToLowerInvariant() + '.png')
        if (!$pngEntries.ContainsKey($fileName)) { continue }

        $builder = [System.Text.StringBuilder]::new()
        foreach ($code in $record.unified.Split('-')) { [void]$builder.Append([char]::ConvertFromUtf32([Convert]::ToInt32($code, 16))) }
        $keywords = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
        foreach ($term in @($record.short_name) + @($record.short_names) + @($record.name)) {
            if ([string]::IsNullOrWhiteSpace($term)) { continue }
            $normalized = $term.ToLowerInvariant().Replace('_', ' ') -replace '[^a-z0-9 ]+', ' '
            [void]$keywords.Add($term.ToLowerInvariant())
            [void]$keywords.Add($normalized.Trim())
            foreach ($word in $normalized.Split(' ', [System.StringSplitOptions]::RemoveEmptyEntries)) { [void]$keywords.Add($word) }
        }
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($pngEntries[$fileName], (Join-Path $assetDirectory $fileName), $true)
        $entries.Add([ordered]@{ emoji = $builder.ToString(); keywords = @($keywords | Where-Object { ![string]::IsNullOrWhiteSpace($_) } | Sort-Object) })
    }

    [ordered]@{
        source = 'emoji-data v15.1.2; color artwork from Twemoji v14.1.2 (CC-BY 4.0)'
        entries = @($entries)
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $installerDictionary -Encoding utf8

    $licenseEntry = $archive.Entries | Where-Object { $_.FullName -match '/LICENSE-GRAPHICS$' } | Select-Object -First 1
    if ($null -eq $licenseEntry) { throw 'Twemoji LICENSE-GRAPHICS was not found in the archive.' }
    $reader = [IO.StreamReader]::new($licenseEntry.Open())
    try { $license = $reader.ReadToEnd() } finally { $reader.Dispose() }
    @(
        'Twemoji v14.1.2',
        'Source: https://github.com/jdecked/twemoji',
        'Graphics licensed under CC-BY 4.0: https://creativecommons.org/licenses/by/4.0/',
        '',
        $license
    ) | Set-Content -LiteralPath $licensePath -Encoding utf8
    Write-Host ("Imported {0} color Emoji entries and PNG assets." -f $entries.Count)
}
finally {
    $archive.Dispose()
}
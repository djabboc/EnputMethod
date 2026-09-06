[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = "Medium")]
param()

$ErrorActionPreference = "Stop"
$registryPath = "HKCU:\Software\Enput Method\CandidateFrequency"

if (-not (Test-Path -LiteralPath $registryPath)) {
    Write-Host "Candidate adaptive frequency is already empty."
    return
}

$registryKey = Get-Item -LiteralPath $registryPath
try {
    $entryCount = $registryKey.ValueCount
}
finally {
    $registryKey.Close()
}

if ($PSCmdlet.ShouldProcess($registryPath, "Reset $entryCount candidate adaptive-frequency entries")) {
    Remove-Item -LiteralPath $registryPath -Recurse -Force
    if (Test-Path -LiteralPath $registryPath) {
        throw "Candidate adaptive-frequency reset failed: $registryPath still exists."
    }

    Write-Host "Candidate adaptive frequency reset completed ($entryCount entries removed)."
    Write-Warning "Fully close and reopen each input host before testing so it reloads the cleared frequency state."
}

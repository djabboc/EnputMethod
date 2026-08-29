param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$msbuild = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
$installerProject = Join-Path $projectRoot "EnputMethod.Installer\EnputMethod.Installer.csproj"
if (-not (Test-Path -LiteralPath $msbuild)) { throw "Visual Studio MSBuild was not found: $msbuild" }

$buildProcess = [System.Diagnostics.ProcessStartInfo]::new()
$buildProcess.FileName = $msbuild
$buildProcess.Arguments = "`"$installerProject`" /m /nologo /p:Configuration=$Configuration /p:Platform=x64"
$buildProcess.WorkingDirectory = $projectRoot
$buildProcess.UseShellExecute = $false
$buildProcess.Environment.Clear()
foreach ($entry in [Environment]::GetEnvironmentVariables().GetEnumerator()) {
    if (-not [string]::Equals([string]$entry.Key, "Path", [StringComparison]::OrdinalIgnoreCase)) {
        $buildProcess.Environment[[string]$entry.Key] = [string]$entry.Value
    }
}
$machinePath = [Environment]::GetEnvironmentVariable("Path", "Machine")
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
$buildProcess.Environment["Path"] = "$machinePath;$userPath"
$build = [System.Diagnostics.Process]::Start($buildProcess)
$build.WaitForExit()
if ($build.ExitCode -ne 0) { throw "Installer build failed with exit code $($build.ExitCode)." }

& (Join-Path $PSScriptRoot "run-overlay-tests.ps1") -Configuration $Configuration
& (Join-Path $PSScriptRoot "install-and-verify.ps1") -Configuration $Configuration

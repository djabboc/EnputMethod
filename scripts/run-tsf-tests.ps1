param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$msbuild = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
$project = Join-Path $projectRoot "EnputMethod.Tsf.Tests\EnputMethod.Tsf.Tests.vcxproj"
if (-not (Test-Path -LiteralPath $msbuild)) { throw "Visual Studio MSBuild was not found: $msbuild" }

& $msbuild $project /m /nologo "/p:Configuration=$Configuration" /p:Platform=x64
if ($LASTEXITCODE -ne 0) { throw "TSF candidate selection test build failed with exit code $LASTEXITCODE." }

$test = Join-Path $projectRoot "bin\$Configuration\EnputMethod.Tsf.Tests.exe"
& $test
if ($LASTEXITCODE -ne 0) { throw "TSF candidate selection tests failed with exit code $LASTEXITCODE." }

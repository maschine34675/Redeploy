param([switch]$ContractOnly, [string]$PublishedDll)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!$PublishedDll) { $PublishedDll = Join-Path $repo 'artifacts/blocked-mod-review-20261113/other/isbsf-release/BepInEx/plugins/ISBSpecialForcesPlugin/ISBNotify.dll' }
dotnet build (Join-Path $PSScriptRoot 'IsbNotifierChecks.csproj') -c Release --nologo
if ($LASTEXITCODE) { throw 'ISB linked-source harness build failed' }
$exe = Join-Path $PSScriptRoot 'bin/Release/net472/IsbNotifierChecks.exe'
& $exe published $PublishedDll
if ($LASTEXITCODE) { throw 'Released ISB notifier contract check failed' }
if ($ContractOnly) { exit 0 }
dotnet build (Join-Path $PSScriptRoot 'RealApi/RealApi.csproj') -c Release --nologo
if ($LASTEXITCODE) { throw 'Real EFT API compilation failed' }
foreach ($testMode in @('absent', 'main-only', 'normal', 'newer', 'older', 'drift', 'main-guid', 'assembly', 'fault', 'missing-hook', 'metadata-change', 'destroyed', 'singleton-change')) {
    & $exe $testMode
    if ($LASTEXITCODE) { throw ('ISB runtime check failed: ' + $testMode) }
}
Write-Output 'ISB_NOTIFIER_CHECKS_PASSED'

param([string]$SptRoot = 'D:\SPT41')
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$arena = [IO.Path]::GetFullPath((Join-Path $repo '../ArenaMode'))
$spt = (Resolve-Path -LiteralPath $SptRoot).Path
$noDeploy = Join-Path $arena 'tools/AirPitChecks/NoDeploy.targets'
if (!(Test-Path -LiteralPath $noDeploy -PathType Leaf)) { throw 'Sibling ArenaMode checkout and its no-deploy targets are required.' }
$evidence = Join-Path $PSScriptRoot ('artifacts/' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$before = @(Get-ChildItem (Join-Path $spt 'BepInEx/plugins') -Filter 'maschine-*.dll' -Recurse -File |
    Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash)
$before | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'installed-before.json')

function Run([string]$Name, [string]$Command, [string[]]$Arguments, [string]$Expected = '') {
    $log = Join-Path $evidence ($Name + '.log')
    & $Command @Arguments *> $log
    $code = $LASTEXITCODE
    $output = [IO.File]::ReadAllText($log)
    if ($code -ne 0 -or ($Expected -and !$output.Contains($Expected))) {
        throw "$Name failed (exit $code): $log`n$($output.Substring([Math]::Max(0,$output.Length-6000)))"
    }
    Write-Output "SHARED_BODY_CHECK_PASSED: $Name ($log)"
}

$oldRoot = $env:REDEPLOY_TEST_SPT_ROOT
$oldShared = $env:UseSharedCompilation
$oldServer = $env:DOTNET_CLI_USE_MSBUILD_SERVER
Push-Location $repo
try {
    $env:REDEPLOY_TEST_SPT_ROOT = $spt
    $env:UseSharedCompilation = 'false'
    $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
    $build = @('-c','Release','--disable-build-servers','-p:UseSharedCompilation=false',
        '-p:SPTModType=Library',"-p:SPTPath=$spt","-p:SPTUserPath=$spt\SPT_Runtime",'-v:minimal')
    Run 'build-redeploy' 'dotnet' (@('build', (Join-Path $repo 'Redeploy.Client/Redeploy.Client.csproj')) + $build)
    Run 'build-arena' 'dotnet' (@('build', (Join-Path $arena 'ArenaMode.Client/ArenaMode.Client.csproj')) + $build + @("-p:CustomAfterMicrosoftCommonTargets=$noDeploy"))
    Run 'unit-redeploy' 'dotnet' @('test','Redeploy.Tests/Redeploy.Tests.csproj','-c','Release',
        '--disable-build-servers','-p:UseSharedCompilation=false','--logger','trx;LogFileName=redeploy.trx','--results-directory',$evidence,'-v:minimal')
    [xml]$trx = Get-Content -Raw -LiteralPath (Join-Path $evidence 'redeploy.trx')
    $counters = $trx.TestRun.ResultSummary.Counters
    if ([int]$counters.total -lt 1 -or [int]$counters.failed -gt 0 -or [int]$counters.passed -ne [int]$counters.total) {
        throw "Redeploy suite must execute and pass every case: $($counters.OuterXml)"
    }
    Push-Location $arena
    try {
        Run 'admission-arena' 'pwsh' @('-NoLogo','-NoProfile','-File','tools/OptionalModAdmissionChecks/Verify.ps1') 'OPTIONAL_MOD_ADMISSION_VERIFY_PASSED'
        Run 'session-arena' 'dotnet' @('run','--project','tools/ArenaSessionClientChecks/ArenaSessionClientChecks.csproj',
            '-c','Release','--disable-build-servers','-p:UseSharedCompilation=false') 'ARENA_SESSION_CLIENT_CHECKS_PASSED'
        # This inspected wrapper overrides client/server deployment and checks installed hashes.
        Run 'unit-arena' 'pwsh' @('-NoLogo','-NoProfile','-File','tools/Run-ArenaVerification.ps1','-SptRoot',$spt) 'ARENA_VERIFICATION_SUMMARY passed=1 failed=0'
    } finally { Pop-Location }
    foreach ($family in @('ArenaRecoilLegacy','ArenaContinuousLoadAmmo','ArenaContinuousHealing')) {
        if (Get-ChildItem (Join-Path $arena 'ArenaMode.Client/Compat') -Filter ($family + '*.cs')) {
            throw "Duplicate Arena adapter remains: $family"
        }
    }
    Write-Output "SHARED_BODY_INTEGRATION_CHECKS_PASSED; Redeploy tests=$($counters.passed); evidence: $evidence"
} finally {
    Pop-Location
    $env:REDEPLOY_TEST_SPT_ROOT = $oldRoot
    $env:UseSharedCompilation = $oldShared
    $env:DOTNET_CLI_USE_MSBUILD_SERVER = $oldServer
    $after = @(Get-ChildItem (Join-Path $spt 'BepInEx/plugins') -Filter 'maschine-*.dll' -Recurse -File |
        Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash)
    if (Compare-Object $before $after -Property Path,Hash) { throw 'Installed DLLs changed during no-deploy verification.' }
}

param([string]$PublishedDll, [string]$EvidenceRoot, [switch]$SkipMutants)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'WttArmoryBodyChecks.csproj'
if (-not $EvidenceRoot) { $EvidenceRoot = Join-Path $repo 'artifacts\wtt-armory-body-checks' }
$evidence = Join-Path $EvidenceRoot ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'NativeCompile.csproj') -c Release --nologo -p:BaseIntermediateOutputPath=obj-native/ > (Join-Path $evidence 'native-build.txt')
if ($LASTEXITCODE) { throw 'Actual-EFT adapter compilation failed' }
dotnet build $project -c Release --nologo > (Join-Path $evidence 'build.txt')
if ($LASTEXITCODE) { throw 'Harness compilation failed' }
$exe = Join-Path $PSScriptRoot 'bin\Release\net472\WTT-ArmoryClient.exe'
foreach ($testMode in @('normal','failure','aborted','destroyed','older')) {
    & $exe $testMode 2>&1 | Tee-Object (Join-Path $evidence ($testMode + '.txt'))
    if ($LASTEXITCODE) { throw ('Checks failed: ' + $testMode) }
}
if ($PublishedDll) {
    & $exe published $PublishedDll 2>&1 | Tee-Object (Join-Path $evidence 'published.txt')
    if ($LASTEXITCODE) { throw 'Actual release contract differs from reviewed fields or cleanup methods' }
    Get-FileHash -LiteralPath $PublishedDll -Algorithm SHA256 | Format-List | Out-File (Join-Path $evidence 'published-sha256.txt')
}
if (-not $SkipMutants) {
    $source = [IO.File]::ReadAllText((Join-Path $repo 'Redeploy.Core\Compat\WttArmoryBodyCompatibility.cs'))
    $mutants = @(
        @{ Name='stale-body-cache'; Before='_you.SetValue(plugin, body);'; After='// cache rebind removed'; Expected='new body resets all outgoing burst counters' },
        @{ Name='missing-counter-reset'; Before='_recoil.SetValue(null, 0); _rof.SetValue(null, 0);'; After='// counter reset removed'; Expected='new body resets all outgoing burst counters' },
        @{ Name='cleared-new-hyperburst'; Before='bool hyperburst = template != null && Array.IndexOf(ids, template) >= 0;'; After='bool hyperburst = false;'; Expected='new hyperburst weapon classification survives' },
        @{ Name='late-old-write'; Before='&& ReferenceEquals(game.LocalPlayer, body) && ReferenceEquals(world.MainPlayer, body);'; After=';'; Expected='late old shoot callbacks preserve new burst' },
        @{ Name='uncommitted-body'; Before='|| !ReferenceEquals(_game.LocalPlayer, newBody) || !ReferenceEquals(_world.MainPlayer, newBody)'; After=''; Expected='uncommitted candidate leaves old state untouched' },
        @{ Name='cancel-retains-world'; Before='_game = _previousGame; _world = _previousWorld;'; After='// provisional context leak'; Expected='cancel releases first provisional game reference' },
        @{ Name='untracked-failed-candidate'; Before=' && !BodyReplacementContext.TryOwnCandidate(body)'; After=''; Expected='construction callback claims and fences exact candidate'; Mode='aborted' },
        @{ Name='destroyed-strong-reference'; Before='if (_active == null) { _active = null; _game = null; _world = null; }'; After='if (_active == null) { _game = null; _world = null; }'; Expected='destroyed committed body releases strong reference after failed attempt'; Mode='destroyed' }
    )
    foreach ($mutant in $mutants) {
        if (-not $source.Contains($mutant.Before)) { throw ('Missing mutation anchor: ' + $mutant.Name) }
        $folder = Join-Path $evidence $mutant.Name
        New-Item -ItemType Directory -Path $folder | Out-Null
        $path = Join-Path $folder 'Compat.cs'
        [IO.File]::WriteAllText($path, $source.Replace($mutant.Before, $mutant.After))
        dotnet build $project -c Release --nologo "-p:CompatSource=$path" "-p:BaseIntermediateOutputPath=$folder\obj\" "-p:OutputPath=$folder\bin\" -p:AppendTargetFrameworkToOutputPath=false > (Join-Path $folder 'build.txt')
        if ($LASTEXITCODE) { throw ('Mutant did not compile: ' + $mutant.Name) }
        # Windows PowerShell 5 treats redirected native stderr as an error record.
        # A mutant's nonzero exit is the expected result, then validated below.
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $testMode = if ($mutant.Mode) { $mutant.Mode } else { 'normal' }
        & (Join-Path $folder 'bin\WTT-ArmoryClient.exe') $testMode > (Join-Path $folder 'test.txt') 2>&1
        $mutantExit = $LASTEXITCODE
        $ErrorActionPreference = $previousPreference
        if ($mutantExit -eq 0) { throw ('Surviving mutant: ' + $mutant.Name) }
        if (-not ([IO.File]::ReadAllText((Join-Path $folder 'test.txt')).Contains('System.Exception: ' + $mutant.Expected))) { throw ('Mutant failed for unexpected reason: ' + $mutant.Name) }
        Write-Output ('REJECTED ' + $mutant.Name)
    }
}
Write-Output ('Evidence: ' + $evidence)
Write-Output 'WTT_ARMORY_BODY_VERIFIED'

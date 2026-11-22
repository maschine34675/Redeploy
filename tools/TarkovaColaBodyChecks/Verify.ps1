param([string]$PublishedDll, [string]$EvidenceRoot, [switch]$SkipMutants)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'TarkovaColaBodyChecks.csproj'
if (-not $EvidenceRoot) { $EvidenceRoot = Join-Path $repo 'artifacts\tarkova-cola-body-20261122' }
$evidence = Join-Path $EvidenceRoot ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'NativeCompile.csproj') -c Release --nologo -p:BaseIntermediateOutputPath=obj-native/ > (Join-Path $evidence 'native-build.txt')
if ($LASTEXITCODE) { throw 'Actual-EFT adapter compilation failed' }
dotnet build $project -c Release --nologo > (Join-Path $evidence 'build.txt')
if ($LASTEXITCODE) { throw 'Harness compilation failed' }
$exe = Join-Path $PSScriptRoot 'bin\Release\net472\TarkovaCola.Client.exe'
foreach ($testMode in @('normal','failed-candidate','failed-contract','failed-commit','post-commit-hook-loss','older')) {
    & $exe $testMode 2>&1 | Tee-Object (Join-Path $evidence ($testMode + '.txt'))
    if ($LASTEXITCODE) { throw ('Checks failed: ' + $testMode) }
}
if ($PublishedDll) {
    & $exe published $PublishedDll 2>&1 | Tee-Object (Join-Path $evidence 'published.txt')
    if ($LASTEXITCODE) { throw 'Actual release contract differs from reviewed fields or methods' }
    Get-FileHash -LiteralPath $PublishedDll -Algorithm SHA256 | Format-List | Out-File (Join-Path $evidence 'published-sha256.txt')
}
if (-not $SkipMutants) {
    $compat = [IO.File]::ReadAllText((Join-Path $repo 'Redeploy.Core\Compat\TarkovaColaBodyCompatibility.cs'))
    $contract = [IO.File]::ReadAllText((Join-Path $repo 'Redeploy.Core\Compat\TarkovaColaBodyContract.cs'))
    $mutants = @(
        @{ Name='no-transient-retirement'; Before='_contract.RetireBodyState(newBody);'; After='// missing body retirement'; Expected='outgoing health and item references retired' },
        @{ Name='repeat-event'; Before='|| ReferenceEquals(_active, newBody) || !ReferenceEquals(oldBody, _outgoing)'; After='|| !ReferenceEquals(oldBody, _outgoing)'; Expected='replacement physical perk cache refreshed once' },
        @{ Name='cancel-retains-game'; Before='_game = _previousGame; _world = _previousWorld;'; After='// leaked admission references'; Expected='cancel releases provisional game' },
        @{ Name='unsafe-construction-tick'; Before='return _failure == null && !BodyReplacementContext.IsCreating && BodyReplacementContext.OwnsRaid(game, world)'; After='return BodyReplacementContext.OwnsRaid(game, world)'; Expected='construction cannot tick old state on new candidate'; ReplaceExtra=$true },
        @{ Name='old-body-callback'; Before='&& ReferenceEquals(game.LocalPlayer, __0) && ReferenceEquals(world.MainPlayer, __0);'; After=';'; Expected='late retained old body is fenced and new body remains active' },
        @{ Name='rearm-revive'; Contract=$true; Before='"Downed", "Bypass", "_regenOn", "_noStimShown", "_noKitShown"'; After='"Downed", "Bypass", "_regenOn", "_noStimShown", "_noKitShown", "_reviveUsed"'; Expected='revive charge and raid/bot counters preserved' },
        @{ Name='uncommitted-event'; Before='|| !ReferenceEquals(_game.LocalPlayer, newBody) || !ReferenceEquals(_world.MainPlayer, newBody)'; After=''; Expected='uncommitted event preserves outgoing state' },
        @{ Name='remove-surviving-guards'; Before='if (ReferenceEquals(_active, null) && ReferenceEquals(_outgoing, null)) Clear();'; After='Clear();'; Expected='post-commit hook loss retains surviving transient fence'; Mode='post-commit-hook-loss' }
    )
    foreach ($mutant in $mutants) {
        $source = if ($mutant.Contract) { $contract } else { $compat }
        if (-not $source.Contains($mutant.Before)) { throw ('Missing mutation anchor: ' + $mutant.Name) }
        $source = $source.Replace($mutant.Before, $mutant.After)
        if ($mutant.ReplaceExtra) { $source = $source.Replace('&& ReferenceEquals(game.LocalPlayer, body) && ReferenceEquals(world.MainPlayer, body);', ';') }
        $folder = Join-Path $evidence $mutant.Name
        New-Item -ItemType Directory -Path $folder | Out-Null
        $path = Join-Path $folder 'Mutant.cs'
        [IO.File]::WriteAllText($path, $source)
        $property = if ($mutant.Contract) { "-p:ContractSource=$path" } else { "-p:CompatSource=$path" }
        dotnet build $project -c Release --nologo $property "-p:BaseIntermediateOutputPath=$folder\obj\" "-p:OutputPath=$folder\bin\" -p:AppendTargetFrameworkToOutputPath=false > (Join-Path $folder 'build.txt')
        if ($LASTEXITCODE) { throw ('Mutant did not compile: ' + $mutant.Name) }
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $testMode = if ($mutant.Mode) { $mutant.Mode } else { 'normal' }
        & (Join-Path $folder 'bin\TarkovaCola.Client.exe') $testMode > (Join-Path $folder 'test.txt') 2>&1
        $mutantExit = $LASTEXITCODE
        $ErrorActionPreference = $previousPreference
        if ($mutantExit -eq 0) { throw ('Surviving mutant: ' + $mutant.Name) }
        if (-not ([IO.File]::ReadAllText((Join-Path $folder 'test.txt')).Contains('System.Exception: ' + $mutant.Expected))) { throw ('Mutant failed for unexpected reason: ' + $mutant.Name) }
        Write-Output ('REJECTED ' + $mutant.Name)
    }
}
Write-Output ('Evidence: ' + $evidence)
Write-Output 'TARKOVA_COLA_BODY_VERIFIED'

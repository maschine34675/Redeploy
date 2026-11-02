param([string]$PublishedDll, [string]$SourceRoot, [string]$EvidenceRoot, [switch]$SkipMutants)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$reference = Join-Path $repo '../_reference/raid-review-audit-1.10.0'
$sha = 'b2a7a9b46fbfb46137a76b0e190f318d230d3208'
if (-not $EvidenceRoot) { $EvidenceRoot = Join-Path $repo 'artifacts/rr-compat-20261102/rr-impl' }
$evidence = Join-Path $EvidenceRoot ('checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $evidence | Out-Null
if (-not $PublishedDll) { $PublishedDll = Join-Path $reference 'release/BepInEx/plugins/RAID_REVIEW.dll' }
if (-not (Test-Path -LiteralPath $PublishedDll)) {
    $release = Invoke-RestMethod 'https://api.github.com/repos/Chazut/SPT-RaidReview/releases/tags/1.10.0'
    $asset = @($release.assets | Where-Object name -eq 'raid_review__1.10.0.zip')
    if ($asset.Count -ne 1) { throw 'Pinned Raid Review release asset unavailable; pass -PublishedDll explicitly.' }
    $zip = Join-Path $evidence 'release.zip'
    Invoke-WebRequest $asset[0].browser_download_url -OutFile $zip
    $releaseFolder = Join-Path $evidence 'release'
    Expand-Archive -LiteralPath $zip -DestinationPath $releaseFolder
    $PublishedDll = Join-Path $releaseFolder 'BepInEx/plugins/RAID_REVIEW.dll'
}
if ((Get-FileHash -LiteralPath $PublishedDll).Hash -ne '1D10AFA713CB520FE2D43D271526C817FAF51D58071F76D9CE85FF3BA4777872') { throw 'Raid Review 1.10.0 client SHA mismatch' }
if (-not $SourceRoot) { $SourceRoot = Join-Path $reference "SPT-RaidReview-$sha" }
if (-not (Test-Path -LiteralPath $SourceRoot)) {
    $zip = Join-Path $evidence 'source.zip'
    Invoke-WebRequest "https://github.com/Chazut/SPT-RaidReview/archive/$sha.zip" -OutFile $zip
    Expand-Archive -LiteralPath $zip -DestinationPath (Join-Path $evidence 'source')
    $SourceRoot = Join-Path $evidence "source/SPT-RaidReview-$sha"
}
$helpers = @{
    'playerStatuses.ts'='8CC51F88F2A6CA5060E41FA7CD086DAFC098F9810E4EA383B72016D3C8DA1BA2'
    'playerPresence.ts'='A741C70B869168A44107C11234D4B0902E789315F477E23F4F9BB6C9FB9EF36F'
    'playerIdentity.ts'='070B4FF2B194CCC0CCD5F2368C134CE13CE7268BBC571E1A1BBFB867BCF653B9'
}
foreach ($name in $helpers.Keys) {
    if ((Get-FileHash -LiteralPath (Join-Path $SourceRoot "Private/src/helpers/$name")).Hash -ne $helpers[$name]) { throw "Upstream helper changed: $name" }
}
# Integration guard: RR must bind in the shared GameStarted prefix, before foreign
# postfixes; the Core reset/postfix must not itself retire the newly armed adapter.
$lifecycle = [IO.File]::ReadAllText((Join-Path $repo 'Redeploy.Core/Engine/RaidLifecycle.cs'))
if ($lifecycle -notmatch '(?s)\[PatchPrefix\]\s*private static void Prefix\(GameWorld __instance\).*?RaidReviewCompat.BeforeRaidStart\(__instance\)' -or
    $lifecycle -notmatch 'RespawnEngineState.ResetForRaidBoundary\(\)' -or
    $lifecycle -match 'RaidReviewCompat.RetireWorld') { throw 'Shared raid-start integration contract changed' }
$project = Join-Path $PSScriptRoot 'RaidReviewChecks.csproj'
dotnet build (Join-Path $repo 'Redeploy.Core/Redeploy.Core.csproj') -c Release -p:SPTModType=Library --nologo > (Join-Path $evidence 'native-build.txt')
if ($LASTEXITCODE) { throw 'Actual EFT Core compile failed' }
dotnet build $project -c Release -p:SPTModType=Library --nologo > (Join-Path $evidence 'runtime-build.txt')
if ($LASTEXITCODE) { throw 'Harness build failed' }
& (Join-Path $PSScriptRoot 'bin/Release/net472/RaidReviewChecks.exe') $PublishedDll $evidence > (Join-Path $evidence 'runtime.txt') 2>&1
if ($LASTEXITCODE) { throw "Runtime harness failed: $evidence" }
node (Join-Path $PSScriptRoot 'ReplayOracle.mjs') $SourceRoot (Join-Path $evidence 'projected-packets.json') > (Join-Path $evidence 'replay.txt') 2>&1
if ($LASTEXITCODE) { throw "Upstream replay failed: $evidence" }
if (-not $SkipMutants) {
    $mutants = @(
        @{Name='absent-unpatch';File='RaidReviewCompat.cs';Before='if (_patched) { Patches.UnpatchSelf(); _patched = false; }';After='Patches.UnpatchSelf(); _patched = false;';Expected='absent plugin does not patch or enumerate for unpatch'},
        @{Name='failed-bind-retry';File='RaidReviewCompat.cs';Before='ReferenceEquals(_rejectedPlugin, info.Instance) && _rejectedVersion == version';After='false';Expected='failed bind cached; only held cleanup retries'},
        @{Name='availability-not-claimant';File='RaidReviewCompat.cs';Before='same && RespawnApi.IsAvailable && DeathInterception.FindClaimant(_game) != null';After='same && RespawnApi.IsAvailable';Expected='claimant controls aliases: inactive Arena'},
        @{Name='old-body-confirm';File='RaidReviewCompat.cs';Before='return !Block(__0);';After='return true;';Expected='old late death cannot poison current life'},
        @{Name='uncommitted-candidate';File='RaidReviewCompat.cs';Before='return owned;';After='return false;';Expected='uncommitted candidate telemetry suppressed'},
        @{Name='live-history-retention';File='RaidReviewLifeLedger.cs';Before='Current.Body = null;';After='';Expected='retired physical bodies not retained by alias history'},
        @{Name='mutable-snapshot';File='RaidReviewPacketSchema.cs';Before='var copy = Clone(payload);';After='var copy = payload;';Expected='claimant controls aliases: inactive Arena'},
        @{Name='passive-ballistic-traversal';File='RaidReviewCompat.cs';Before='if (!_active && !_pending) return true;';After='// MUTANT traverse unowned ballistic before upstream guards';Expected='ordinary ballistic exception remains upstream event-local: ordinary'},
        @{Name='missing-final-status';File='RaidReviewCompat.cs';Before='EmitStatus(life, status); _ended = true;';After='_ended = true;';Expected='same-frame END active emits terminal status before END'},
        @{Name='repeat-session-end';File='RaidReviewCompat.cs';Before='&& __1 == ExitStatus.Killed && RespawnApi.IsRespawnPending(__0)';After='&& false';Expected='partial PMC statistics end keeps recording open'},
        @{Name='start-before-candidate-commit';File='RaidReviewCompat.cs';Before='if (!ReferenceEquals(__0, _world) || !BodyReplacementContext.IsCreating';After='if (true || !ReferenceEquals(__0, _world) || !BodyReplacementContext.IsCreating';Expected='in-flight entry replacement defers original RR start'}
    )
    foreach ($mutant in $mutants | Where-Object { -not $_.Skip }) {
        $folder = Join-Path $evidence $mutant.Name
        $production = Join-Path $folder 'Compat'
        New-Item -ItemType Directory -Force $production | Out-Null
        Copy-Item -LiteralPath (Join-Path $repo 'Redeploy.Core/Compat/RaidReview') -Destination $production -Recurse
        New-Item -ItemType Directory -Force (Join-Path $production 'QuestTracker') | Out-Null
        foreach ($name in @('StandaloneHudContract.cs','StandaloneHudScope.cs')) { Copy-Item -LiteralPath (Join-Path $repo "Redeploy.Core/Compat/QuestTracker/$name") -Destination (Join-Path $production 'QuestTracker') }
        Copy-Item -LiteralPath (Join-Path $repo 'Redeploy.Core/Compat/BodyReplacementContext.cs') -Destination $production
        $file = Join-Path $production ('RaidReview/' + $mutant.File)
        $text = [IO.File]::ReadAllText($file)
        if (-not $text.Contains($mutant.Before)) { throw "Mutation anchor missing: $($mutant.Name)" }
        [IO.File]::WriteAllText($file, $text.Replace($mutant.Before, $mutant.After))
        $output = Join-Path $folder 'bin'
        dotnet build $project -c Release -p:SPTModType=Library "-p:ProductionRoot=$production" "-p:OutputPath=$output/" -p:AppendTargetFrameworkToOutputPath=false --nologo > (Join-Path $folder 'build.txt')
        if ($LASTEXITCODE) { throw "Mutant failed to compile: $($mutant.Name)" }
        & (Join-Path $output 'RaidReviewChecks.exe') $PublishedDll $folder > (Join-Path $folder 'runtime.txt') 2>&1
        if ($LASTEXITCODE -eq 0) { throw "Surviving mutant: $($mutant.Name)" }
        if (-not ([IO.File]::ReadAllText((Join-Path $folder 'runtime.txt'))).Contains('CHECK FAILED: ' + $mutant.Expected)) { throw "Unexpected mutant failure: $($mutant.Name), see $folder" }
        Write-Output "REJECTED $($mutant.Name)"
    }
}
Get-Content (Join-Path $evidence 'runtime.txt'), (Join-Path $evidence 'replay.txt')
Write-Output "RAID_REVIEW_CHECKS_PASSED; evidence=$evidence"

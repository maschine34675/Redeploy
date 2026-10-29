param(
    [string]$PublishedDll = '',
    [string]$EvidenceDirectory = ''
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (!$PublishedDll) {
    $PublishedDll = 'D:\SPT41\Development\ArenaMode\artifacts\compat-first-20261020\recoil\release\BepInEx\plugins\SPTRecoilReworkLegacy\SPTRecoilReworkLegacyClient.dll'
}
if (!(Test-Path -LiteralPath $PublishedDll -PathType Leaf)) { throw 'Published 2.0.0 client DLL required for binary checks.' }
if (!$EvidenceDirectory) {
    $EvidenceDirectory = Join-Path $PSScriptRoot ('artifacts\checks-' + [guid]::NewGuid().ToString('N'))
}
$taskEvidence = [IO.Path]::GetFullPath($EvidenceDirectory)
if (!$taskEvidence.StartsWith((Join-Path $PSScriptRoot 'artifacts\'), [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Evidence must stay within this harness artifacts directory.'
}
New-Item -ItemType Directory -Path $taskEvidence -Force | Out-Null
$taskProject = Join-Path $PSScriptRoot 'RecoilLegacyCompatChecks.csproj'
$taskProduction = Join-Path $taskRoot 'Redeploy.Core\Compat'
$taskNames = @('RecoilLegacyCompat.cs','RecoilLegacyContract.cs','RecoilLegacyScope.cs')
$taskHashes = @{}
foreach ($taskName in $taskNames) { $taskHashes[$taskName] = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $taskProduction $taskName)).Hash }
$taskContext = Join-Path $taskProduction 'BodyReplacementContext.cs'
$taskContextHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $taskContext).Hash
$taskRule = Join-Path $taskProduction 'ReplacementProfileRule.cs'
$taskRuleHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $taskRule).Hash
$taskRows = [Collections.Generic.List[object]]::new()
$taskRunId = [guid]::NewGuid().ToString('N')
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'obj\project.assets.json') -PathType Leaf)) {
    & dotnet restore $taskProject --disable-build-servers '-p:UseSharedCompilation=false'
    if ($LASTEXITCODE -ne 0) { throw 'Offline harness restore failed.' }
}
function Invoke-Check([string]$Name, [string]$Source, [string]$Expected) {
    $taskLog = Join-Path $taskEvidence ($Name + '.log')
    & dotnet run --project $taskProject -c ("RecoilLegacy_" + $taskRunId + '_' + $Name) --no-restore --disable-build-servers '-p:UseSharedCompilation=false' "-p:ProductionRoot=$Source" -- $PublishedDll *> $taskLog
    $taskExit = $LASTEXITCODE
    $taskText = Get-Content -Raw -LiteralPath $taskLog
    if ($Expected) {
        if ($taskExit -eq 0 -or $taskText -notmatch [regex]::Escape('ASSERT: ' + $Expected)) {
            throw "Mutant $Name was not killed by the expected behavioral assertion; see $taskLog"
        }
    } elseif ($taskExit -ne 0 -or $taskText -notmatch 'checks passed') {
        throw "Baseline failed; see $taskLog"
    }
    $taskRows.Add([pscustomobject]@{name=$Name;exit=$taskExit;expected=$Expected;log=$taskLog})
}
Invoke-Check 'baseline' $taskProduction ''
$taskMutations = @(
    @{name='destructive-singletons';file='RecoilLegacyCompat.cs';from='_contract.RecoilInstance.SetValue(null, null); _contract.CameraInstance.SetValue(null, null);';to='/* negative control: singleton handoff missing */';expected='replacement initialized without singleton destruction 0'},
    @{name='retired-updates';file='RecoilLegacyCompat.cs';from='return lease.Committed && Current(lease);';to='return true;';expected='retired body cannot rotate 0'},
    @{name='previous-controller';file='RecoilLegacyCompat.cs';from='_contract.Cache[0].SetValue(null, __3);';to='/* negative control: previous controller retained */';expected='recalculation uses incoming weapon 0'},
    @{name='lost-rollback';file='RecoilLegacyCompat.cs';from='if (restore)';to='if (false && restore)';expected='old singleton pair restored'},
    @{name='dead-recovery';file='RecoilLegacyCompat.cs';from='if (Authoritative(lease.Previous))';to='if (Current(lease.Previous))';expected='dead authoritative body restores singletons for retry'},
    @{name='foreign-cache-adoption';file='RecoilLegacyCompat.cs';from='if (Exists(cached) && !ReferenceEquals(cached.GetComponent<Player>(), oldBody))';to='if (false && Exists(cached) && !ReferenceEquals(cached.GetComponent<Player>(), oldBody))';expected='unrelated cached controller rejects before construction'},
    @{name='canceled-adoption';file='RecoilLegacyCompat.cs';from='if (_newAdoption) Scope.AbandonAdoption(_preparedLease, _adoptedHands);';to='if (_newAdoption) { /* negative control: forget cancellation */ }';expected='canceled adoption leaves ordinary component updates untouched True'},
    @{name='unfiltered-world-cleanup';file='RecoilLegacyCompat.cs';from='RetireWhere(l => ReferenceEquals(l.World, world));';to='RetireWhere(_ => true);';expected='unrelated world cleanup is inert'},
    @{name='unfiltered-world-cache';file='RecoilLegacyCompat.cs';from='if (owner != null && included(owner)) _contract.ClearCache();';to='if (owner != null) _contract.ClearCache();';expected='unrelated world cleanup is inert'},
    @{name='game-profile-only';file='RecoilLegacyCompat.cs';from='ReplacementProfileRule.CarriesRaidProfile(body.Profile, game.Profile, BodyReplacementContext.ExpectedProfile)';to='ReferenceEquals(body.Profile, game.Profile)';expected='own-Scav replacement and old body survive foreign initialization'},
    @{name='profile-gate-removed';file='RecoilLegacyCompat.cs';from='!ReplacementProfileRule.CarriesRaidProfile(body.Profile, game.Profile, BodyReplacementContext.ExpectedProfile)';to='false';expected='unrelated profile body stays outside the recoil handoff'}
)
foreach ($taskMutation in $taskMutations) {
    $taskCopy = Join-Path $taskEvidence $taskMutation.name
    New-Item -ItemType Directory -Path $taskCopy -Force | Out-Null
    foreach ($taskName in $taskNames) { Copy-Item -LiteralPath (Join-Path $taskProduction $taskName) -Destination (Join-Path $taskCopy $taskName) }
    $taskFile = Join-Path $taskCopy $taskMutation.file
    $taskSource = Get-Content -Raw -LiteralPath $taskFile
    if ([regex]::Matches($taskSource,[regex]::Escape($taskMutation.from)).Count -ne 1) { throw "Mutation seam drifted: $($taskMutation.name)" }
    Set-Content -LiteralPath $taskFile -Value $taskSource.Replace($taskMutation.from,$taskMutation.to)
    Invoke-Check $taskMutation.name $taskCopy $taskMutation.expected
}
if (((Get-FileHash -Algorithm SHA256 -LiteralPath $taskContext).Hash -ne $taskContextHash) -or ((Get-FileHash -Algorithm SHA256 -LiteralPath $taskRule).Hash -ne $taskRuleHash)) {
    throw 'Shared construction context changed during verification; rerun against a stable revision.'
}
$taskHashes['BodyReplacementContext.cs'] = $taskContextHash
$taskHashes['ReplacementProfileRule.cs'] = $taskRuleHash
Invoke-Check 'final-baseline' $taskProduction ''
foreach ($taskName in $taskNames) {
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $taskProduction $taskName)).Hash -ne $taskHashes[$taskName]) {
        throw 'Production sources changed during verification; rerun against a stable revision.'
    }
}
$taskHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskEvidence 'production-sha256.json')
$taskRows | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $taskEvidence 'results.json')
Write-Output ("Recoil Legacy baseline and eleven behavioral mutants verified: " + $taskEvidence)

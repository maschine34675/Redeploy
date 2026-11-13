$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$source = Join-Path $repo 'Redeploy.Core/Compat/PlayerEncumbranceBar/PlayerEncumbranceBarCompat.cs'
$original = [IO.File]::ReadAllText($source)
$dest = Join-Path $repo ('artifacts/encumbrance-mutants/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $dest -Force | Out-Null
function Run([string]$path) {
    $output = & dotnet run --project $PSScriptRoot -c Release "-p:EncumbranceCompatSource=$path" 2>&1 | Out-String
    return @{ Code = $LASTEXITCODE; Output = $output }
}
$baseline = Run $source
if ($baseline.Code -ne 0 -or !$baseline.Output.Contains('PLAYER_ENCUMBRANCE_BAR_CHECKS_PASSED')) { throw $baseline.Output }
$cases = @(
    @{ Name='backend-profile'; Anchor='__result = body.Profile;'; Change='return true;'; Expected='replacement panel reads new physical profile' },
    @{ Name='outgoing-events'; Anchor='_ownedBar.Dispose();'; Change='// removed disposal'; Expected='dispose fixture did not throw' },
    @{ Name='unrelated-owner'; Anchor='&& ReferenceEquals(game.PlayerOwner?.Player, body)'; Change=''; Expected='same-profile unrelated owner' },
    @{ Name='late-callback'; Anchor='!RetiredBars.TryGetValue(__instance, out _)'; Change='true'; Expected='late callback to closed raid panel' },
    @{ Name='forget-completed-rollback'; Anchor='_closeStarted = true;'; Change='_closeStarted = false;'; Expected='completed preparation rollback restores ordinary live inventory callbacks' },
    @{ Name='ignore-new-show'; Anchor=' && _showEpoch == _pendingShowEpoch'; Change=''; Expected='new same-body Show between capture and cleanup is not disposed' },
    @{ Name='lose-prior-lease'; Anchor='_ownedBar = _previousOwnedBar;'; Change='_ownedBar = null;'; Expected='same-profile unrelated owner cannot refresh tracked bar' },
    @{ Name='hidden-subscriptions'; Anchor='if (_pendingHadSubscriptions)'; Change='if (_pendingWasVisible)'; Expected='hidden subscribed display restores subscriptions independently of visibility' }
)
foreach ($case in $cases) {
    if (!$original.Contains($case.Anchor)) { throw "Missing mutant anchor: $($case.Name)" }
    $path = Join-Path $dest ($case.Name + '.cs')
    [IO.File]::WriteAllText($path, $original.Replace($case.Anchor, $case.Change))
    $result = Run $path
    [IO.File]::WriteAllText((Join-Path $dest ($case.Name + '.log')), $result.Output)
    if ($result.Code -eq 0 -or !$result.Output.Contains($case.Expected) -or $result.Output.Contains('error CS')) { throw "Mutant did not fail the intended assertion: $($case.Name)`n$($result.Output)" }
    Write-Output "PASS mutant $($case.Name)"
}
$final = Run $source
if ($final.Code -ne 0) { throw $final.Output }
Write-Output 'PLAYER_ENCUMBRANCE_BAR_VERIFIED baseline=128 mutants=8'

param([string]$PublishedAssembly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'ContinuousHealingCompatChecks.csproj'
$source = Join-Path $repo 'Redeploy.Core/Compat'
if (!$PublishedAssembly) {
    $PublishedAssembly = 'D:/SPT41/Development/ArenaMode/artifacts/health-loot-compat-20261019/releases/continuous-healing/extracted/BepInEx/plugins/ContinuousHealing/ContinuousHealing.dll'
}
if (!(Test-Path -LiteralPath $PublishedAssembly -PathType Leaf)) {
    throw 'Supply -PublishedAssembly with the separately downloaded Continuous Healing 1.6.3 DLL. It is inspected as metadata, never installed or executed.'
}
$run = [Guid]::NewGuid().ToString('N')
$evidence = Join-Path $PSScriptRoot "artifacts/$run"
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$files = @('ContinuousHealingCompat.cs', 'ContinuousHealingContract.cs', 'ContinuousHealingScope.cs')
$original = @{}
foreach ($name in $files) { $original[$name] = [IO.File]::ReadAllText((Join-Path $source $name)) }
# Resolve SDK framework assets once; all variants share the same dependency set.
# Source-path and configuration changes do not need another NuGet network pass.
& dotnet restore $project -p:NuGetAudit=false --ignore-failed-sources
if ($LASTEXITCODE -ne 0) { throw 'Harness restore failed.' }

function Check([string]$Name, [string]$File = '', [string]$Find = '', [string]$Replace = '', [string]$Failure = '') {
    $copy = Join-Path $evidence $Name
    [IO.Directory]::CreateDirectory($copy) | Out-Null
    foreach ($sourceName in $files) {
        $content = $original[$sourceName]
        if ($sourceName -eq $File) {
            if ([regex]::Matches($content, [regex]::Escape($Find)).Count -ne 1) { throw "Ambiguous mutation anchor: $Name" }
            $content = $content.Replace($Find, $Replace)
        }
        [IO.File]::WriteAllText((Join-Path $copy $sourceName), $content)
    }
    $log = Join-Path $copy 'result.log'
    & dotnet run --no-restore --disable-build-servers --project $project -p:UseSharedCompilation=false -c "ContinuousHealing_${run}_$Name" "-p:ProductionRoot=$copy" -- $PublishedAssembly *> $log
    $exit = $LASTEXITCODE
    $output = [IO.File]::ReadAllText($log)
    if ($Failure) {
        if ($exit -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains("ASSERT: $Failure")) {
            throw "Fault did not compile and fail its intended assertion: $Name ($log)`n$output"
        }
        Write-Output "CONTINUOUS_HEALING_MUTANT_REJECTED: $Name"
    } elseif ($exit -ne 0 -or $output -notmatch 'CONTINUOUS_HEALING_CHECKS_PASSED \(\d+\)') {
        throw "Production checks failed: $log`n$output"
    } else { Write-Output $output.Trim() }
}

Check 'before'
Check 'ordinary-leak' 'ContinuousHealingScope.cs' 'if (!exactOwnedBody || !ReferenceEquals(bodyWorld, currentWorld)) return false;' 'if (!ReferenceEquals(bodyWorld, currentWorld)) return false;' 'ordinary bot foreign gate'
Check 'wrong-world' 'ContinuousHealingScope.cs' 'if (!exactOwnedBody || !ReferenceEquals(bodyWorld, currentWorld)) return false;' 'if (!exactOwnedBody) return false;' 'scope grant rejects unrelated world'
Check 'lost-retirement-lease' 'ContinuousHealingScope.cs' 'if (ReferenceEquals(lease.World, bodyWorld)) return true;' 'if (false) return true;' 'leased consumer body survives consumer fault foreign gate'
Check 'body-reuse-leak' 'ContinuousHealingScope.cs' 'if (ReferenceEquals(lease.World, bodyWorld)) return true;' 'return true;' 'reused body loses old-world ownership foreign gate'
Check 'local-body-identity' 'ContinuousHealingScope.cs' '!ReferenceEquals(body, currentLocalBody)' 'false' 'retired living same-profile body foreign gate'
Check 'dead-local-leak' 'ContinuousHealingScope.cs' '|| !isYourPlayer || !alive;' '|| !isYourPlayer;' 'dead old MainPlayer foreign gate'
Check 'native-veto' 'ContinuousHealingCompat.cs' '__result = true;' '__result = false;' 'dead old MainPlayer native fallback'
Check 'old-cancel-leak' 'ContinuousHealingCompat.cs' '=> _foreign == null || !ShouldBypass(__0, __originalMethod.DeclaringType.Name);' '=> true;' 'retired body cannot cancel replacement healing'
Check 'missing-core-lease' 'ContinuousHealingCompat.cs' 'owned = coreOwned || Scope.Owns' 'owned = Scope.Owns' 'dead old MainPlayer foreign gate'
Check 'unowned-fault-poison' 'ContinuousHealingCompat.cs' 'if (owned)' 'if (true)' 'unowned scope fault does not poison replacements'
Check 'missing-receipt' 'ContinuousHealingCompat.cs' 'if (!OwnsPatches()) throw new InvalidOperationException("Continuous Healing compatibility hooks did not install");' '' 'missing installation receipt rejects Arena'
Check 'native-receipt' 'ContinuousHealingContract.cs' 'info.Prefixes.Count(p => p.PatchMethod == prefixes[i]) != 1' 'false' 'missing foreign native receipt rejects Arena'
Check 'runtime-fail-open' 'ContinuousHealingCompat.cs' 'return owned;' 'return false;' 'proved body survives health-read fault foreign gate'
Check 'scope-cleanup' 'ContinuousHealingScope.cs' 'internal void Clear() => _bodies = new ConditionalWeakTable<object, Lease>();' 'internal void Clear() { }' 'scope cleanup releases ownership'
Check 'after'
foreach ($name in $files) {
    if ([IO.File]::ReadAllText((Join-Path $source $name)) -ne $original[$name]) { throw "Production source changed during checks: $name" }
}
Write-Output "CONTINUOUS_HEALING_COMPAT_VERIFIED; 14 compiled negative controls; evidence: $evidence"

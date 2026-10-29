[CmdletBinding()]
param([switch]$Mutations)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$project = Join-Path $PSScriptRoot 'BodyReplacementChecks.csproj'
$coreRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../Redeploy.Core/Compat'))
$arenaSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../ArenaMode/ArenaMode.Client/Compat/ArenaMedicalBodyScope.cs'))
$sources = @{
    'BodyReplacementContext.cs' = Join-Path $coreRoot 'BodyReplacementContext.cs'
    'BodyReplacementCompatibility.cs' = Join-Path $coreRoot 'BodyReplacementCompatibility.cs'
    'ArenaMedicalBodyScope.cs' = $arenaSource
}
$hashes = @{}
foreach ($name in $sources.Keys) { $hashes[$name] = (Get-FileHash -LiteralPath $sources[$name] -Algorithm SHA256).Hash }
$runId = [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')
$artifacts = Join-Path $PSScriptRoot "artifacts/$runId"
[IO.Directory]::CreateDirectory($artifacts) | Out-Null

function Invoke-DotnetChecked {
    param([string[]]$Arguments, [string]$LogName)
    $output = @(& dotnet @Arguments 2>&1)
    $code = $LASTEXITCODE
    [IO.File]::WriteAllLines((Join-Path $artifacts $LogName), [string[]]$output)
    $output | ForEach-Object { Write-Host $_ }
    if ($code -ne 0) { throw "dotnet failed ($code); see $LogName" }
}

# This self-contained SDK project has no package references, project references,
# deployment targets or imported repository props. Its NuGet config has no feeds.
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'obj/project.assets.json'))) {
    Invoke-DotnetChecked -Arguments @('restore', $project, '--configfile', (Join-Path $PSScriptRoot 'NuGet.Config'), '--disable-build-servers') -LogName 'restore.log'
}
Invoke-DotnetChecked -Arguments @('build', $project, '--no-restore', '--disable-build-servers', '-p:UseSharedCompilation=false', '-c', 'Release', '--nologo') -LogName 'baseline-build.log'
$baselineDll = Join-Path $PSScriptRoot 'bin/Release/net10.0/BodyReplacementChecks.dll'
Invoke-DotnetChecked -Arguments @($baselineDll) -LogName 'baseline-checks.log'
$baselineText = Get-Content -LiteralPath (Join-Path $artifacts 'baseline-checks.log') -Raw
if ($baselineText -notmatch 'BODY_REPLACEMENT_CHECKS_PASSED cases=19 assertions=\d+') { throw 'Baseline success token missing' }

if ($Mutations) {
    $mutants = @(
        @{
            Name = 'legacy-arena-double-hooks'; File = 'BodyReplacementCompatibility.cs'; Case = 'legacy-arena-conflict'
            Before = 'if (legacyArena != null) return CachedRejectionReason = legacyArena;'
            After = '// MUTANT: admit shared Core hooks alongside older Arena-owned adapters.'
        },
        @{
            Name = 'same-id-profile'; File = 'BodyReplacementContext.cs'; Case = 'exact-candidate'
            Before = '!ReferenceEquals(body.Profile, _expectedProfile)'
            After = 'body.Profile.Id != _expectedProfile.Id'
        },
        @{
            Name = 'cancel-leaks-provisional'; File = 'BodyReplacementContext.cs'; Case = 'cancel-first'
            Before = 'Bodies.Remove(PreviousBody);'
            After = '// MUTANT: leave the provisional body lease behind.'
        },
        @{
            Name = 'cancel-erases-committed'; File = 'BodyReplacementContext.cs'; Case = 'cancel-prior'
            Before = 'if (_previousEntryLease != null) Bodies.Add(PreviousBody, _previousEntryLease);'
            After = '// MUTANT: do not restore the earlier committed body lease.'
        },
        @{
            Name = 'no-mod-adoption'; File = 'BodyReplacementCompatibility.cs'; Case = 'no-mod'
            Before = 'if (!HasOptionalMods()) return;'
            After = '// MUTANT: optional mods no longer guard adoption.'
        },
        @{
            Name = 'ordinary-bot-scope'; File = 'ArenaMedicalBodyScope.cs'; Case = 'arena-scope'
            Before = '|| !ArenaSession.IsExactActiveRaid(game, world)'
            After = '|| false'
        },
        @{
            Name = 'null-scope-quarantine'; File = 'ArenaMedicalBodyScope.cs'; Case = 'arena-scope'
            Before = 'if (ReferenceEquals(body, null)) return false;'
            After = '// MUTANT: null input faults and silently quarantines the registered scope.'
        },
        @{
            Name = 'late-world-clears-new'; File = 'BodyReplacementCompatibility.cs'; Case = 'pump-old'
            Before = 'if (!ReferenceEquals(world, BodyReplacementContext.CurrentWorld)) return;'
            After = '// MUTANT: old disposal can retire current context.'
        },
        @{
            Name = 'current-world-leaks'; File = 'BodyReplacementCompatibility.cs'; Case = 'pump-current'
            Before = 'BodyReplacementContext.Reset();'
            After = '// MUTANT: leave current world context alive.'
        }
    )
    $index = 0
    foreach ($mutant in $mutants) {
        $index++
        $dir = Join-Path $artifacts $mutant.Name
        [IO.Directory]::CreateDirectory($dir) | Out-Null
        foreach ($name in $sources.Keys) {
            $content = [IO.File]::ReadAllText($sources[$name])
            if ($name -eq $mutant.File) {
                $count = ([regex]::Matches($content, [regex]::Escape($mutant.Before))).Count
                if ($count -ne 1) { throw "Mutation anchor for $($mutant.Name) must match exactly once, found $count" }
                $content = $content.Replace($mutant.Before, $mutant.After)
            }
            [IO.File]::WriteAllText((Join-Path $dir $name), $content)
        }
        # A distinct configuration isolates generated compiler and executable outputs.
        $configuration = "CompositionMutant${index}_$runId"
        Invoke-DotnetChecked -Arguments @('build', $project, '--no-restore', '--disable-build-servers', '-p:UseSharedCompilation=false', '-c', $configuration, "-p:ProductionCoreRoot=$dir", "-p:ArenaScopePath=$(Join-Path $dir 'ArenaMedicalBodyScope.cs')", '--nologo') -LogName "$($mutant.Name)-build.log"
        $mutantDll = Join-Path $PSScriptRoot "bin/$configuration/net10.0/BodyReplacementChecks.dll"
        $output = @(& dotnet $mutantDll --case $mutant.Case 2>&1)
        $code = $LASTEXITCODE
        [IO.File]::WriteAllLines((Join-Path $artifacts "$($mutant.Name)-check.log"), [string[]]$output)
        $combined = $output -join "`n"
        if ($code -ne 1 -or $combined -notmatch "BODY_REPLACEMENT_CHECK_FAILED $([regex]::Escape($mutant.Case)):" -or $combined -match 'BODY_REPLACEMENT_CHECKS_PASSED') {
            throw "Mutation $($mutant.Name) was not rejected by its behavioral assertion (exit $code): $combined"
        }
        Write-Host "MUTATION_KILLED $($mutant.Name) by $($mutant.Case)"
    }
    Write-Host "BODY_REPLACEMENT_MUTATIONS_PASSED killed=$($mutants.Count)"
}

foreach ($name in $sources.Keys) {
    if ((Get-FileHash -LiteralPath $sources[$name] -Algorithm SHA256).Hash -ne $hashes[$name]) {
        throw "Production source changed during verification: $($sources[$name]); rerun against the final sources"
    }
}
[IO.File]::WriteAllText((Join-Path $artifacts 'source-hashes.json'), ($hashes | ConvertTo-Json))
Write-Host "BODY_REPLACEMENT_COMPOSITION_VERIFIED artifacts=$artifacts"

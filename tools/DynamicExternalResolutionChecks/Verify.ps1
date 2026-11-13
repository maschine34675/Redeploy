param()
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'DynamicExternalResolutionChecks.csproj'
$production = Join-Path $repo 'Redeploy.Core\Compat\DynamicExternalResolution'
$source = Get-Content -LiteralPath (Join-Path $production 'DynamicExternalResolutionCompat.cs') -Raw
$artifact = Join-Path $repo ('artifacts\derp-checks\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $artifact -Force | Out-Null
$faults = @(
    @{ Name='missing-cache-handoff'; From='            SetCache(current);'; To='            // fault: no committed cache'; Oracle='private cache follows committed body' },
    @{ Name='stale-optic-callback'; From='if (_callbacks?.Count > 0 && RetiredOwners.TryGetValue(_callbacks.Peek().Owner, out _)) return false;'; To='// fault: retired callbacks admitted'; Oracle='delayed retired body callbacks' },
    @{ Name='empty-scope'; From='animation != null && animation.ScopeAimTransforms?.Count > 0 && animation.AimIndex >= 0'; To='animation != null && animation.AimIndex >= 0'; Oracle='DivideByZeroException' },
    @{ Name='negative-aim'; From=' && animation.AimIndex >= 0'; To=''; Oracle='negative aim index is not a ready native scope' },
    @{ Name='null-scope'; From='&& animation.ScopeAimTransforms[animation.AimIndex % animation.ScopeAimTransforms.Count] != null;'; To='&& true;'; Oracle='null selected scope is not native readiness' }
)
& dotnet run --project $project -c Release
if ($LASTEXITCODE) { throw 'DERP baseline failed' }
foreach ($fault in $faults) {
    if (($source.Split([string[]]@($fault.From), [StringSplitOptions]::None).Count - 1) -ne 1) {
        throw ('Mutant anchor is not unique: ' + $fault.Name)
    }
    $folder = Join-Path $artifact $fault.Name
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $folder 'DynamicExternalResolutionCompat.cs'), $source.Replace($fault.From, $fault.To))
    Copy-Item -LiteralPath (Join-Path $production 'DynamicExternalResolutionContract.cs') -Destination $folder
    $output = & dotnet run --project $project -c Release ('-p:CompatSource=' + $folder + '\*.cs') 2>&1 | Out-String
    $exit = $LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $folder 'output.txt'), $output)
    if ($exit -eq 0 -or !$output.Contains($fault.Oracle) -or $output.Contains('error CS')) {
        throw ('Mutant did not fail its runtime assertion: ' + $fault.Name + [Environment]::NewLine + $output)
    }
    Write-Output ('Rejected ' + $fault.Name)
}
& dotnet run --project $project -c Release
if ($LASTEXITCODE) { throw 'DERP baseline rerun failed' }

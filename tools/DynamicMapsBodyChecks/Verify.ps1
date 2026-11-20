param([Parameter(Mandatory = $true)][string]$ReleasedDll)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $PSScriptRoot 'DynamicMapsBodyChecks.csproj'
$source = Join-Path $repo 'Redeploy.Core\Compat'
$scratch = Join-Path $repo ('artifacts\dynamicmaps-body-checks\' + [Guid]::NewGuid().ToString('N'))
$mutants = @(
    @{ Name = 'missing-hide'; File = 'DynamicMapsProviderRebind.cs'; Before = 'contract.Hide.Invoke(provider, args);'; After = '/* mutant: missing unsubscribe */'; Expected = 'Old event handlers leaked' },
    @{ Name = 'inactive-map'; File = 'DynamicMapsProviderRebind.cs'; Before = 'if (!(bool)_active.GetValue(screen) || !(bool)_inRaid.GetValue(screen))'; After = 'if (false)'; Expected = 'Hidden map received provider show' },
    @{ Name = 'stale-body'; File = 'DynamicMapsCompatHelper.cs'; Before = '!ReferenceEquals(Singleton<GameWorld>.Instance.MainPlayer, newPlayer) ||'; After = ''; Expected = 'Late old-body callback stole current provider ownership' }
)
dotnet run --project $project -c Release -- $ReleasedDll
if ($LASTEXITCODE -ne 0) { throw 'DynamicMaps lifecycle/released metadata failed' }
dotnet run --project $project -c Release -- --legacy
if ($LASTEXITCODE -ne 0) { throw 'DynamicMaps legacy behavior failed' }
foreach ($mutant in $mutants) {
    $directory = Join-Path $scratch $mutant.Name
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    foreach ($name in @('DynamicMapsCompatHelper.cs', 'DynamicMapsProviderRebind.cs')) {
        $body = [IO.File]::ReadAllText((Join-Path $source $name))
        if ($name -eq $mutant.File) {
            if ($body.Split(@($mutant.Before), [StringSplitOptions]::None).Length -ne 2) { throw ('Mutation anchor drift: ' + $mutant.Name) }
            $body = $body.Replace($mutant.Before, $mutant.After)
        }
        [IO.File]::WriteAllText((Join-Path $directory $name), $body)
    }
    $transcript = & dotnet run --project $project -c Release "-p:CompatSource=$directory" 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0 -or -not $transcript.Contains($mutant.Expected)) { throw ('Mutation was not killed by its intended assertion: ' + $mutant.Name + "`n" + $transcript) }
    Write-Output ('MUTANT_KILLED ' + $mutant.Name)
}
# Leave the build output pointing at the real production files after mutation runs.
dotnet run --project $project -c Release -- $ReleasedDll
if ($LASTEXITCODE -ne 0) { throw 'Restored-production DynamicMaps checks failed' }
Write-Output 'DYNAMICMAPS_BODY_OFFLINE_VERIFIED'

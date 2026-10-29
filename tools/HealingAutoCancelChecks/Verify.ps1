param([string]$PublishedDll, [switch]$SkipMutants)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $PublishedDll) { $PublishedDll = Join-Path $repo 'artifacts\standalone-compat-20261029\healing\release\BepInEx\plugins\HealingAutoCancel.dll' }
if (-not (Test-Path -LiteralPath $PublishedDll)) { throw 'Supply -PublishedDll for the reviewed Forge Healing Autocancel 5.0.1 release (plugin 1.1.0).' }
$evidence = Join-Path $repo ('artifacts\standalone-compat-20261029\healing\checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$project = Join-Path $PSScriptRoot 'HealingAutoCancelChecks.csproj'
dotnet build (Join-Path $PSScriptRoot 'NativeCompile.csproj') -c Release --nologo -p:SPTModType=Library -p:BaseIntermediateOutputPath=obj-native/ > (Join-Path $evidence 'native-build.txt')
if ($LASTEXITCODE) { Get-Content (Join-Path $evidence 'native-build.txt'); throw 'Actual EFT API compile failed' }
dotnet build $project -c Release --nologo -p:SPTModType=Library > (Join-Path $evidence 'build.txt')
if ($LASTEXITCODE) { Get-Content (Join-Path $evidence 'build.txt'); throw 'Harness build failed' }
$exe = Join-Path $PSScriptRoot 'bin\Release\net472\HealingAutoCancelChecks.exe'
foreach ($testMode in @('normal','newer','drift','older','entry','recovery')) {
    & $exe $testMode 2>&1 | Tee-Object (Join-Path $evidence ($testMode + '.txt'))
    if ($LASTEXITCODE) { throw ('Healing Autocancel check failed: ' + $testMode) }
}
& $exe published $PublishedDll (Join-Path $repo '..\..\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll') 2>&1 | Tee-Object (Join-Path $evidence 'published.txt')
if ($LASTEXITCODE) { throw 'Released contract check failed' }
if (-not $SkipMutants) {
    $source = [IO.File]::ReadAllText((Join-Path $repo 'Redeploy.Core\Compat\HealingAutoCancel\HealingAutoCancelCompat.cs'))
    $mutants = @(
        @{ Name='skip-declared-unregister'; Before="target = target?.DeclaringType?.GetMethod(nameof(GameWorld.UnregisterPlayer),`n            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new[] { typeof(IPlayer) }, null);"; After='// disabled inherited-method normalization'; Expected='inherited runtime unregister normalized to declared implementation' },
        @{ Name='no-retired-source-fence'; Before='if (_dispatchSource != null && Retired.TryGetValue(_dispatchSource, out _)) return false;'; After='// disabled source fence'; Expected='retired native source blocked before next body commits' },
        @{ Name='no-closure-fence'; Before='if (!Live(lease)) return;'; After='// disabled old closure fence'; BeforeAlso='if (_wrapper != null) return Live(_wrapper)'; AfterAlso='if (_wrapper != null) return true'; Expected='late old closure cannot heal new body' },
        @{ Name='skip-old-detach'; Before='health.HealthChangedEvent -= _original;'; After='// disabled old native event detach'; Expected='actual old body subscription detached' },
        @{ Name='eager-prepare-mutation'; Before='// Preparation is read-only.'; After='oldBody.ActiveHealthController.HealthChangedEvent -= _original; // Preparation is read-only.'; Expected='Prepare does not mutate foreign state' },
        @{ Name='no-replacement-binding'; Before='lease.Health.HealthChangedEvent += lease.Callback;'; After='// disabled binding'; Expected='committed replacement medical event works' }
    )
    foreach ($mutant in $mutants) {
        if (-not $source.Contains($mutant.Before)) { throw ('Missing mutant anchor: ' + $mutant.Name) }
        $folder = Join-Path $evidence $mutant.Name; New-Item -ItemType Directory -Path $folder | Out-Null
        $path = Join-Path $folder 'Compat.cs'; $mutated = $source.Replace($mutant.Before, $mutant.After)
        if ($mutant.BeforeAlso) { if (-not $mutated.Contains($mutant.BeforeAlso)) { throw 'Missing secondary mutation anchor' }; $mutated = $mutated.Replace($mutant.BeforeAlso, $mutant.AfterAlso) }
        [IO.File]::WriteAllText($path, $mutated)
        dotnet build $project -c Release --nologo -p:SPTModType=Library "-p:CompatSource=$path" "-p:BaseIntermediateOutputPath=$folder\obj\" "-p:OutputPath=$folder\bin\" -p:AppendTargetFrameworkToOutputPath=false > (Join-Path $folder 'build.txt')
        if ($LASTEXITCODE) { throw ('Mutant failed to compile: ' + $mutant.Name) }
        & (Join-Path $folder 'bin\HealingAutoCancelChecks.exe') normal > (Join-Path $folder 'test.txt') 2>&1
        if ($LASTEXITCODE -eq 0) { throw ('Surviving mutant: ' + $mutant.Name) }
        if (-not ([IO.File]::ReadAllText((Join-Path $folder 'test.txt')).Contains('System.Exception: ' + $mutant.Expected))) { throw ('Wrong mutant failure: ' + $mutant.Name) }
        Write-Output ('REJECTED ' + $mutant.Name)
    }
}
Write-Output ('Evidence: ' + $evidence)
Write-Output 'HEALING_AUTOCANCEL_CHECKS_PASSED'

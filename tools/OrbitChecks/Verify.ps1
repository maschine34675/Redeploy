param([string]$PublishedDll, [string]$EvidenceRoot, [switch]$SkipMutants)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'OrbitChecks.csproj'
if (-not $EvidenceRoot) { $EvidenceRoot = Join-Path $repo 'artifacts\blocked-mod-review-20261113\orbit' }
if (-not $PublishedDll) { $PublishedDll = Join-Path $repo 'artifacts\blocked-mod-review-20261113\orbit\release\BepInEx\plugins\ORBIT\ORBIT.dll' }
if (-not (Test-Path -LiteralPath $PublishedDll -PathType Leaf)) { throw 'Supply the released ORBIT 3.1.1 DLL with -PublishedDll; released metadata is mandatory.' }
$evidence = Join-Path $EvidenceRoot ('checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'NativeCompile.csproj') -c Release --nologo -p:SPTModType=Library -p:BaseIntermediateOutputPath=obj-native/ > (Join-Path $evidence 'native-build.txt')
if ($LASTEXITCODE) { throw 'ORBIT actual-EFT API build failed' }
dotnet build $project -c Release --nologo -p:SPTModType=Library > (Join-Path $evidence 'build.txt')
if ($LASTEXITCODE) { throw 'ORBIT harness build failed' }
$exe = Join-Path $PSScriptRoot 'bin\Release\net472\OrbitChecks.exe'
foreach ($testMode in @('normal','newer','drift','older','assembly','guid','metadata')) {
    & $exe $testMode 2>&1 | Tee-Object (Join-Path $evidence ($testMode + '.txt'))
    if ($LASTEXITCODE) { throw ('ORBIT check failed: ' + $testMode) }
}
& $exe published $PublishedDll 2>&1 | Tee-Object (Join-Path $evidence 'published.txt')
if ($LASTEXITCODE) { throw 'Published ORBIT differs from reviewed release' }
if (-not $SkipMutants) {
    $source = [IO.File]::ReadAllText((Join-Path $repo 'Redeploy.Core\Compat\Orbit\OrbitCompat.cs'))
    $mutants = @(
        @{ Name='eager-prepare'; Before='// Preparation is read-only.'; After='humans.Clear(); // Preparation is read-only.'; Expected='Prepare has no foreign mutation' },
        @{ Name='wrong-human'; Before='humans[oldIndex] = body;'; After='humans[0] = body;'; Expected='published replacement updates exact old physical slot' },
        @{ Name='skip-rebind'; Before='humans[oldIndex] = body;'; After='humans[oldIndex] = _oldBody;'; Expected='published replacement updates exact old physical slot' },
        @{ Name='adopt-unowned'; Before='&& BodyReplacementContext.OwnsBody(body)'; After=''; Expected='unowned replacement publication cannot steal cache' },
        @{ Name='creating-recovery'; Before='if (!_recover || BodyReplacementContext.IsCreating) return;'; After='if (!_recover) return;'; Expected='failure cannot adopt while creating' },
        @{ Name='ignore-hard-list'; Before='|| !ReferenceEquals(humans, _contract.HardHumans.GetValue(hard))'; After=''; Expected='invalid prepared cache rejected: hard' },
        @{ Name='foreign-retirement'; Before='if (ReferenceEquals(world, _world)) AbortPreparation();'; After='AbortPreparation();'; Expected='published replacement updates exact old physical slot' }
    )
    foreach ($mutant in $mutants) {
        if (-not $source.Contains($mutant.Before)) { throw ('Mutation anchor missing: ' + $mutant.Name) }
        $folder = Join-Path $evidence $mutant.Name
        New-Item -ItemType Directory -Path $folder | Out-Null
        [IO.File]::WriteAllText((Join-Path $folder 'OrbitCompat.cs'), $source.Replace($mutant.Before, $mutant.After))
        Copy-Item -LiteralPath (Join-Path $repo 'Redeploy.Core\Compat\Orbit\OrbitContract.cs') -Destination $folder
        dotnet build $project -c Release --nologo -p:SPTModType=Library "-p:CompatSource=$folder\*.cs" "-p:BaseIntermediateOutputPath=$folder\obj\" "-p:OutputPath=$folder\bin\" -p:AppendTargetFrameworkToOutputPath=false > (Join-Path $folder 'build.txt')
        if ($LASTEXITCODE) { throw ('Mutant did not compile: ' + $mutant.Name) }
        & (Join-Path $folder 'bin\OrbitChecks.exe') normal > (Join-Path $folder 'test.txt') 2>&1
        if ($LASTEXITCODE -eq 0) { throw ('Surviving mutant: ' + $mutant.Name) }
        if (-not ([IO.File]::ReadAllText((Join-Path $folder 'test.txt')).Contains('System.Exception: ' + $mutant.Expected))) {
            throw ('Mutant failed for an unexpected reason: ' + $mutant.Name)
        }
        Write-Output ('REJECTED ' + $mutant.Name)
    }
}
Write-Output ('Evidence: ' + $evidence)
Write-Output 'ORBIT_CHECKS_PASSED'

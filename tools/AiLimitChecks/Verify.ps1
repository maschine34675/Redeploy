param([string]$PublishedDll, [string]$EvidenceRoot, [switch]$SkipMutants)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'AiLimitChecks.csproj'
if (-not $EvidenceRoot) { $EvidenceRoot = Join-Path $repo 'artifacts\standalone-compat-20261029\ai' }
if (-not $PublishedDll) { $PublishedDll = Join-Path $repo 'artifacts\standalone-compat-20261029\ai\release\BepInEx\plugins\dvize.AILimit.dll' }
if (-not (Test-Path -LiteralPath $PublishedDll -PathType Leaf)) { throw 'Supply the released AI Limit 1.9.2 DLL with -PublishedDll; this check must not silently skip released metadata.' }
$evidence = Join-Path $EvidenceRoot ('checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'NativeCompile.csproj') -c Release --nologo -p:SPTModType=Library -p:BaseIntermediateOutputPath=obj-native/ | Set-Content (Join-Path $evidence 'native-build.txt')
if ($LASTEXITCODE) { throw 'AI Limit actual-EFT API build failed' }
dotnet build $project -c Release --nologo -p:SPTModType=Library | Set-Content (Join-Path $evidence 'build.txt')
if ($LASTEXITCODE) { throw 'AI Limit harness build failed' }
$exe = Join-Path $PSScriptRoot 'bin\Release\net472\AiLimitChecks.exe'
foreach ($testMode in @('normal','newer','drift','older')) {
    & $exe $testMode 2>&1 | Tee-Object (Join-Path $evidence ($testMode + '.txt'))
    if ($LASTEXITCODE) { throw ('AI Limit check failed: ' + $testMode) }
}
& $exe published $PublishedDll 2>&1 | Tee-Object (Join-Path $evidence 'published.txt')
if ($LASTEXITCODE) { throw 'Published AI Limit differs from reviewed release' }
if (-not $SkipMutants) {
    $source = [IO.File]::ReadAllText((Join-Path $repo 'Redeploy.Core\Compat\AiLimit\AiLimitCompat.cs'))
    $mutants = @(
        @{ Name='eager-prepare'; Before='// Preparation is read-only.'; After='players.Clear(); // Preparation is read-only.'; Expected='Prepare has no foreign mutation' },
        @{ Name='wrong-human'; Before='players[oldIndex] = body;'; After='players[0] = body;'; Expected='published replacement updates exact old physical slot' },
        @{ Name='skip-rebind'; Before='players[oldIndex] = body;'; After='players[oldIndex] = _oldBody;'; Expected='published replacement updates exact old physical slot' },
        @{ Name='adopt-unowned'; Before='&& BodyReplacementContext.OwnsBody(body)'; After=''; Expected='unowned replacement publication cannot steal cache' },
        @{ Name='creating-recovery'; Before='if (!_recover || BodyReplacementContext.IsCreating) return;'; After='if (!_recover) return;'; Expected='failure cannot adopt while creating' },
        @{ Name='foreign-retirement'; Before='if (ReferenceEquals(world, _world)) AbortPreparation();'; After='AbortPreparation();'; Expected='published replacement updates exact old physical slot' }
    )
    foreach ($mutant in $mutants) {
        if (-not $source.Contains($mutant.Before)) { throw ('Mutation anchor missing: ' + $mutant.Name) }
        $folder = Join-Path $evidence $mutant.Name; New-Item -ItemType Directory -Path $folder | Out-Null
        $path = Join-Path $folder 'AiLimitCompat.cs'; [IO.File]::WriteAllText($path, $source.Replace($mutant.Before, $mutant.After))
        Copy-Item -LiteralPath (Join-Path $repo 'Redeploy.Core\Compat\AiLimit\AiLimitContract.cs') -Destination $folder
        dotnet build $project -c Release --nologo -p:SPTModType=Library "-p:CompatSource=$folder\*.cs" "-p:BaseIntermediateOutputPath=$folder\obj\" "-p:OutputPath=$folder\bin\" -p:AppendTargetFrameworkToOutputPath=false > (Join-Path $folder 'build.txt')
        if ($LASTEXITCODE) { throw ('Mutant did not compile: ' + $mutant.Name) }
        & (Join-Path $folder 'bin\AiLimitChecks.exe') normal > (Join-Path $folder 'test.txt') 2>&1
        if ($LASTEXITCODE -eq 0) { throw ('Surviving mutant: ' + $mutant.Name) }
        if (-not ([IO.File]::ReadAllText((Join-Path $folder 'test.txt')).Contains('System.Exception: ' + $mutant.Expected))) { throw ('Mutant failed for an unexpected reason: ' + $mutant.Name) }
        Write-Output ('REJECTED ' + $mutant.Name)
    }
}
Write-Output ('Evidence: ' + $evidence)
Write-Output 'AI_LIMIT_CHECKS_PASSED'

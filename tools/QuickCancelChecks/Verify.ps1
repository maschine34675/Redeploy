param([string]$PublishedDll, [string]$EvidenceRoot, [switch]$SkipMutants)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'QuickCancelChecks.csproj'
if (-not $EvidenceRoot) { $EvidenceRoot = Join-Path $repo '..\ArenaMode\artifacts\compat-remaining-20261026\items' }
$evidence = Join-Path $EvidenceRoot ('quickcancel-checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'NativeCompile.csproj') -c Release --nologo -p:BaseIntermediateOutputPath=obj-native/ | Set-Content (Join-Path $evidence 'native-build.txt')
if ($LASTEXITCODE) { throw 'Quick Cancel actual-EFT API build failed' }
dotnet build $project -c Release --nologo | Set-Content (Join-Path $evidence 'build.txt')
if ($LASTEXITCODE) { throw 'Quick Cancel harness build failed' }
$exe = Join-Path $PSScriptRoot 'bin\Release\net472\QuickCancelChecks.exe'
foreach ($testMode in @('normal','newer','drift','older')) {
    & $exe $testMode 2>&1 | Tee-Object (Join-Path $evidence ($testMode + '.txt'))
    if ($LASTEXITCODE) { throw ('Quick Cancel check failed: ' + $testMode) }
}
if ($PublishedDll) { & $exe published $PublishedDll 2>&1 | Tee-Object (Join-Path $evidence 'published.txt'); if ($LASTEXITCODE) { throw 'Published Quick Cancel differs from reviewed release' } }
if (-not $SkipMutants) {
    $source = [IO.File]::ReadAllText((Join-Path $repo 'Redeploy.Core\Compat\QuickCancel\QuickCancelCompat.cs'))
    $mutants = @(
        @{ Name='global-clear'; Before='Pending.RemoveWhere(controller => !ReferenceEquals(controller, null) && ReferenceEquals(controller._player, body));'; After='Pending.Clear();'; Expected='actual derived unregister removes only retired physical controller' },
        @{ Name='eager-prepare-mutation'; Before='// Preparation is read-only.'; After='Retire(oldBody); // Preparation is read-only.'; Expected='Prepare has no foreign mutation' },
        @{ Name='skip-pending-retirement'; Before='Pending.RemoveWhere(controller => !ReferenceEquals(controller, null) && ReferenceEquals(controller._player, body));'; After='// no cleanup'; Expected='actual derived unregister removes only retired physical controller' },
        @{ Name='allow-late-mark'; Before='__result = false;'; After='return true; __result = false;'; Expected='delayed retired-owner command cannot pin old body again' }
    )
    foreach ($mutant in $mutants) {
        if (-not $source.Contains($mutant.Before)) { throw ('Mutation anchor missing: ' + $mutant.Name) }
        $folder = Join-Path $evidence $mutant.Name; New-Item -ItemType Directory -Path $folder | Out-Null
        $path = Join-Path $folder 'Compat.cs'; [IO.File]::WriteAllText($path, $source.Replace($mutant.Before, $mutant.After))
        dotnet build $project -c Release --nologo "-p:CompatSource=$path" "-p:BaseIntermediateOutputPath=$folder\obj\" "-p:OutputPath=$folder\bin\" -p:AppendTargetFrameworkToOutputPath=false > (Join-Path $folder 'build.txt')
        if ($LASTEXITCODE) { throw ('Mutant did not compile: ' + $mutant.Name) }
        & (Join-Path $folder 'bin\QuickCancelChecks.exe') normal > (Join-Path $folder 'test.txt') 2>&1
        if ($LASTEXITCODE -eq 0) { throw ('Surviving mutant: ' + $mutant.Name) }
        if (-not ([IO.File]::ReadAllText((Join-Path $folder 'test.txt')).Contains('System.Exception: ' + $mutant.Expected))) { throw ('Mutant failed for an unexpected reason: ' + $mutant.Name) }
        Write-Output ('REJECTED ' + $mutant.Name)
    }
}
Write-Output ('Evidence: ' + $evidence)

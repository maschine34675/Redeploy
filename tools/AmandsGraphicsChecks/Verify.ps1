param([string]$PublishedDll, [string]$GameDll, [string]$EvidenceRoot, [switch]$SkipMutants)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'AmandsGraphicsChecks.csproj'
if (!$EvidenceRoot) { $EvidenceRoot = Join-Path $repo '../ArenaMode/artifacts/compat-remaining-20261026/visual' }
if (!$PublishedDll) { $PublishedDll = Join-Path $repo '../ArenaMode/artifacts/compat-remaining-20261026/visual/amands-release/BepInEx/plugins/AmandsGraphics/AmandsGraphics.dll' }
if (!$GameDll) { $GameDll = Join-Path $repo '../../EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll' }
$evidence = Join-Path $EvidenceRoot ('amand-checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence | Out-Null
dotnet build (Join-Path $PSScriptRoot 'RealApi/RealApi.csproj') -c Release --nologo > (Join-Path $evidence 'real-api-build.txt')
if ($LASTEXITCODE) { throw 'Real EFT API compilation failed' }
dotnet build $project -c Release --nologo > (Join-Path $evidence 'build.txt')
if ($LASTEXITCODE) { throw 'Amand runtime harness build failed' }
$exe = Join-Path $PSScriptRoot 'bin/Release/net472/AmandsGraphicsChecks.exe'
foreach ($testMode in @('normal', 'newer', 'older', 'drift', 'missing-native', 'native-fault', 'early-native', 'early-native-off', 'dead-prism')) {
    & $exe $testMode 2>&1 | Tee-Object (Join-Path $evidence ($testMode + '.txt'))
    if ($LASTEXITCODE) { throw ('Amand check failed: ' + $testMode) }
}
& $exe published $PublishedDll $GameDll 2>&1 | Tee-Object (Join-Path $evidence 'published.txt')
if ($LASTEXITCODE) { throw 'Actual released Amand/EFT contracts differ from reviewed versions' }
if (!$SkipMutants) {
    $source = [IO.File]::ReadAllText((Join-Path $repo 'Redeploy.Core/Compat/AmandsGraphics/AmandsGraphicsCompat.cs'))
    $mutants = @(
        @{Name='discard-world-defaults'; Before='_contract.RestoreDefaults(_lease.Defaults);'; After='// Deliberately discard original world baselines.'; Mode='normal'; Expected='transformed scene value never replaces original baseline'},
        @{Name='eager-prepare-ownership'; Before='Mode = (bool)_contract.GraphicsMode.GetValue(instance)'; After='Committed = true, Mode = (bool)_contract.GraphicsMode.GetValue(instance)'; Mode='normal'; Expected='Prepare does not suppress foreign Update Reset Activate'},
        @{Name='drop-live-lamp-baseline'; Before='dictionary[entry.Key] = entry.Value;'; After='dictionary[entry.Key] = 999f;'; Mode='normal'; Expected='live original lamp values survive and destroyed registry keys retire'},
        @{Name='allow-redundant-prism-postfix'; Before='return camera == null || !ReferenceEquals(__0, camera.GetComponent<PrismEffects>());'; After='return true;'; Mode='normal'; Expected='native Prism reenable preserves chosen on mode after binding'},
        @{Name='lose-off-mode-on-failure'; Before='try { _contract.GraphicsMode.SetValue(_lease.Instance, _lease.Mode); }'; After='try { /* lost cleanup */ }'; Mode='native-fault'; Expected='native failure keeps original defaults and disabled mode and refuses further replacement'}
    )
    foreach ($mutant in $mutants) {
        if (!$source.Contains($mutant.Before)) { throw ('Mutation anchor missing: ' + $mutant.Name) }
        $folder = Join-Path $evidence $mutant.Name; New-Item -ItemType Directory -Path $folder | Out-Null
        [IO.File]::WriteAllText((Join-Path $folder 'Compat.cs'), $source.Replace($mutant.Before, $mutant.After))
        Copy-Item -LiteralPath (Join-Path $repo 'Redeploy.Core/Compat/AmandsGraphics/AmandsGraphicsContract.cs') -Destination (Join-Path $folder 'Contract.cs')
        dotnet build $project -c Release --nologo "-p:CompatSource=$folder/*.cs" "-p:BaseIntermediateOutputPath=$folder/obj/" "-p:OutputPath=$folder/bin/" -p:AppendTargetFrameworkToOutputPath=false > (Join-Path $folder 'build.txt')
        if ($LASTEXITCODE) { throw ('Mutant did not compile: ' + $mutant.Name) }
        & (Join-Path $folder 'bin/AmandsGraphicsChecks.exe') $mutant.Mode > (Join-Path $folder 'test.txt') 2>&1
        if ($LASTEXITCODE -eq 0) { throw ('Mutant survived: ' + $mutant.Name) }
        if (!([IO.File]::ReadAllText((Join-Path $folder 'test.txt')).Contains('System.Exception: ' + $mutant.Expected))) { throw ('Mutant failed for an unexpected reason: ' + $mutant.Name) }
        Write-Output ('REJECTED ' + $mutant.Name)
    }
}
Write-Output ('Evidence: ' + $evidence)

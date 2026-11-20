param([string]$PublishedDll, [string]$EvidenceRoot, [switch]$SkipMutants)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'AdvancedMovementBodyChecks.csproj'
if (-not $EvidenceRoot) { $EvidenceRoot = Join-Path $repo 'artifacts\advanced-movement-body-checks' }
$evidence = Join-Path $EvidenceRoot ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'NativeCompile.csproj') -c Release --nologo -p:BaseIntermediateOutputPath=obj-native/ > (Join-Path $evidence 'native-build.txt')
if ($LASTEXITCODE) { throw 'Actual-EFT adapter compilation failed' }
dotnet build $project -c Release --nologo > (Join-Path $evidence 'build.txt')
if ($LASTEXITCODE) { throw 'Harness compilation failed' }
$exe = Join-Path $PSScriptRoot 'bin\Release\net472\AdvancedMovement.exe'
foreach ($testMode in @('normal','failure','older')) {
    & $exe $testMode 2>&1 | Tee-Object (Join-Path $evidence ($testMode + '.txt'))
    if ($LASTEXITCODE) { throw ('Checks failed: ' + $testMode) }
}
if ($PublishedDll) {
    & $exe published $PublishedDll 2>&1 | Tee-Object (Join-Path $evidence 'published.txt')
    if ($LASTEXITCODE) { throw 'Actual release contract differs from reviewed fields or cleanup methods' }
    Get-FileHash -LiteralPath $PublishedDll -Algorithm SHA256 | Format-List | Out-File (Join-Path $evidence 'published-sha256.txt')
}
if (-not $SkipMutants) {
    $source = [IO.File]::ReadAllText((Join-Path $repo 'Redeploy.Core\Compat\AdvancedMovementBodyCompatibility.cs'))
    $mutants = @(
        @{ Name='eager-retirement'; Before='// Read-only until the engine actually unregisters this body.'; After='_contract.Retire(body); // Read-only until the engine actually unregisters this body.'; Expected='read-only Prepare' },
        @{ Name='wrong-physical-body'; Before='|| !ReferenceEquals(__0, _body)'; After=''; Expected='same-profile different body untouched' },
        @{ Name='new-gesture-cleared'; Before='if (!ReferenceEquals(_gesturePlayer.GetValue(null), body)) return;'; After='// owner guard removed'; Expected='new gesture and animator preserved' },
        @{ Name='audio-keeps-playing'; Before='if (loop != null) loop.Stop();'; After=''; Expected='old audio stopped synchronously and released' }
    )
    foreach ($mutant in $mutants) {
        if (-not $source.Contains($mutant.Before)) { throw ('Missing mutation anchor: ' + $mutant.Name) }
        $folder = Join-Path $evidence $mutant.Name
        New-Item -ItemType Directory -Path $folder | Out-Null
        $path = Join-Path $folder 'Compat.cs'
        [IO.File]::WriteAllText($path, $source.Replace($mutant.Before, $mutant.After))
        dotnet build $project -c Release --nologo "-p:CompatSource=$path" "-p:BaseIntermediateOutputPath=$folder\obj\" "-p:OutputPath=$folder\bin\" -p:AppendTargetFrameworkToOutputPath=false > (Join-Path $folder 'build.txt')
        if ($LASTEXITCODE) { throw ('Mutant did not compile: ' + $mutant.Name) }
        # Windows PowerShell 5 treats redirected native stderr as an error record.
        # A mutant's nonzero exit is the expected result, then validated below.
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        & (Join-Path $folder 'bin\AdvancedMovement.exe') normal > (Join-Path $folder 'test.txt') 2>&1
        $mutantExit = $LASTEXITCODE
        $ErrorActionPreference = $previousPreference
        if ($mutantExit -eq 0) { throw ('Surviving mutant: ' + $mutant.Name) }
        if (-not ([IO.File]::ReadAllText((Join-Path $folder 'test.txt')).Contains('System.Exception: ' + $mutant.Expected))) { throw ('Mutant failed for unexpected reason: ' + $mutant.Name) }
        Write-Output ('REJECTED ' + $mutant.Name)
    }
}
Write-Output ('Evidence: ' + $evidence)
Write-Output 'ADVANCED_MOVEMENT_BODY_VERIFIED'

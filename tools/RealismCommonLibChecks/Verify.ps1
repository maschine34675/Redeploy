param([string]$PublishedDll, [string]$EvidenceRoot, [switch]$SkipMutants)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = Join-Path $PSScriptRoot 'RealismCommonLibChecks.csproj'
if (-not $EvidenceRoot) { $EvidenceRoot = Join-Path $repo 'artifacts\realism-commonlib-20261122' }
$evidence = Join-Path $EvidenceRoot ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
dotnet build (Join-Path $PSScriptRoot 'NativeCompile.csproj') -c Release --nologo -p:BaseIntermediateOutputPath=obj-native/ > (Join-Path $evidence 'native-build.txt')
if ($LASTEXITCODE) { throw 'Actual-EFT adapter compilation failed' }
dotnet build $project -c Release --nologo > (Join-Path $evidence 'build.txt')
if ($LASTEXITCODE) { throw 'Harness compilation failed' }
$exe = Join-Path $PSScriptRoot 'bin\Release\net472\spt-realism-common-lib.exe'
foreach ($testMode in @('normal','partial')) {
    & $exe $testMode 2>&1 | Tee-Object (Join-Path $evidence ($testMode + '.txt'))
    if ($LASTEXITCODE) { throw ('Checks failed: ' + $testMode) }
}
if ($PublishedDll) {
    & $exe published $PublishedDll 2>&1 | Tee-Object (Join-Path $evidence 'published.txt')
    if ($LASTEXITCODE) { throw 'Actual release controller/cleanup metadata differs from reviewed contract' }
    Get-FileHash -LiteralPath $PublishedDll -Algorithm SHA256 | Format-List | Out-File (Join-Path $evidence 'published-sha256.txt')
}
if (-not $SkipMutants) {
    $source = [IO.File]::ReadAllText((Join-Path $repo 'Redeploy.Core\Compat\RealismCommonLibBodyCompatibility.cs'))
    $mutants = @(
        @{ Name='old-controller-enabled'; Before='Suspend(_outgoing);'; After='// old callbacks left active'; Expected='old bundle disabled at native init' },
        @{ Name='late-instance-write'; Before='=> !Controllers.TryGetValue(__instance, out var lease) || lease.Active;'; After='=> true;'; Expected='suspended old reload cannot write new animator' },
        @{ Name='stale-native-source'; Before='private static bool BeforeBodyCallback(object __0) => Allows(_contract?.Owner(__0));'; After='private static bool BeforeBodyCallback(object __0) => true;'; Expected='old body source cannot emit new firing event' },
        @{ Name='old-listener-leak'; Before='_contract.Detach(lease.Controllers);'; After='/* listener leak */'; Expected='retirement removes exact old static delegate targets' },
        @{ Name='rollback-globals'; Before='_contract.Restore(_outgoing.Controllers);'; After='// old globals not restored'; Expected='failed candidate restores previous globals and enabled flags' },
        @{ Name='stale-enabled-snapshot'; Before='_outgoing.Enabled = components.Select(c => c.enabled).ToArray();'; After='// reuse initial native enable flags'; Expected='failed candidate restores previous globals and enabled flags' },
        @{ Name='premature-commit'; Before='|| !ReferenceEquals(_game.LocalPlayer, body)'; After=''; Expected='uncommitted event does not retire old listeners' },
        @{ Name='active-destroy-siblings'; Before='if (lease.Active && ReferenceEquals(_contract.Globals[1].GetValue(null), lease.Controllers[1])) return true;'; After='// retire live siblings'; Expected='active component destroy after cancelled prepare preserves siblings' },
        @{ Name='partial-cleanup'; Before='if (_candidate != null) Retire(_candidate);'; After='// partial candidate retains handlers'; Expected='partial init handlers detached'; Mode='partial' }
    )
    foreach ($mutant in $mutants) {
        if (-not $source.Contains($mutant.Before)) { throw ('Missing mutation anchor: ' + $mutant.Name) }
        $folder = Join-Path $evidence $mutant.Name
        New-Item -ItemType Directory -Path $folder | Out-Null
        $path = Join-Path $folder 'Compat.cs'
        [IO.File]::WriteAllText($path, $source.Replace($mutant.Before, $mutant.After))
        dotnet build $project -c Release --nologo "-p:CompatSource=$path" "-p:BaseIntermediateOutputPath=$folder\obj\" "-p:OutputPath=$folder\bin\" -p:AppendTargetFrameworkToOutputPath=false > (Join-Path $folder 'build.txt')
        if ($LASTEXITCODE) { throw ('Mutant did not compile: ' + $mutant.Name) }
        $previousPreference = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        $testMode = if ($mutant.Mode) { $mutant.Mode } else { 'normal' }
        & (Join-Path $folder 'bin\spt-realism-common-lib.exe') $testMode > (Join-Path $folder 'test.txt') 2>&1
        $mutantExit = $LASTEXITCODE
        $ErrorActionPreference = $previousPreference
        if ($mutantExit -eq 0) { throw ('Surviving mutant: ' + $mutant.Name) }
        if (-not ([IO.File]::ReadAllText((Join-Path $folder 'test.txt')).Contains('System.Exception: ' + $mutant.Expected))) { throw ('Mutant failed for unexpected reason: ' + $mutant.Name) }
        Write-Output ('REJECTED ' + $mutant.Name)
    }
}
Write-Output ('Evidence: ' + $evidence)
Write-Output 'REALISM_COMMONLIB_BODY_VERIFIED'

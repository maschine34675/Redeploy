param([string]$PublishedHollywoodDll, [string]$PublishedHandsDll, [string]$EvidenceRoot, [switch]$SkipMutants)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$audit = Join-Path $repo 'artifacts/rr-compat-20261102/popular'
if (-not $PublishedHollywoodDll) { $PublishedHollywoodDll = Join-Path $audit 'HollywoodFX/HollywoodFX.dll' }
if (-not $PublishedHandsDll) { $PublishedHandsDll = Join-Path $audit 'HandsAreNotBusy/HandsAreNotBusy.dll' }
if (-not $EvidenceRoot) { $EvidenceRoot = Join-Path $repo 'artifacts/rr-compat-20261102/popular-impl' }
$evidence = Join-Path $EvidenceRoot ('checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $evidence | Out-Null
if ((Get-FileHash -LiteralPath $PublishedHollywoodDll -Algorithm SHA256).Hash -ne '2A703911E13A7AFD03DDE047B3441BB3A67735093EEE28AD575A073D1851507A') { throw 'HollywoodFX reviewed release hash mismatch' }
if ((Get-FileHash -LiteralPath $PublishedHandsDll -Algorithm SHA256).Hash -ne 'A03BAF23199ACCCED769F162CD256F2E8EB07D9063595927759A87EEB78F4189') { throw 'Hands Are Not Busy reviewed release hash mismatch' }
$project = Join-Path $PSScriptRoot 'PopularBodyChecks.csproj'
dotnet build (Join-Path $PSScriptRoot 'NativeCompile.csproj') -c Release -p:SPTModType=Library --nologo > (Join-Path $evidence 'native-build.txt')
if ($LASTEXITCODE) { throw 'Actual EFT compile failed' }
foreach ($future in @('false', 'true')) {
    dotnet build $project -c Release -p:SPTModType=Library "-p:FutureMods=$future" --nologo > (Join-Path $evidence "build-future-$future.txt")
    if ($LASTEXITCODE) { throw "Harness build failed (future=$future), see $evidence" }
    & (Join-Path $PSScriptRoot 'bin/Release/net472/PopularBodyChecks.exe') $PublishedHollywoodDll $PublishedHandsDll > (Join-Path $evidence "checks-future-$future.txt") 2>&1
    if ($LASTEXITCODE) { throw "Harness failed (future=$future), see $evidence" }
}
if (-not $SkipMutants) {
    $candidateFence = @'
        if (!ReferenceEquals(Scope.Prepared, null) && !ReferenceEquals(__0, Scope.Prepared)
            && BodyReplacementContext.OwnsRaid(Scope.Game, Scope.World)
            && (BodyReplacementContext.TryOwnCandidate(__0)
                || (BodyReplacementContext.OwnsBody(__0) && ReferenceEquals(__0.GameWorld, Scope.World)))) return false;
'@
    $mutants = @(
        @{ Name='stale-fx-owner'; File='HollywoodFx/HollywoodFxCompat.cs'; Before='if (ReferenceEquals(cached, oldBody)) _contract.LocalPlayer.SetValue(null, body);'; After='if (ReferenceEquals(cached, oldBody)) { /* MUTANT: keep old owner */ }'; Expected='FX cache follows committed replacement' },
        @{ Name='retired-reset'; File='HandsAreNotBusy/HandsAreNotBusyCompat.cs'; Before='if (Scope.Retired(__0)) return false;'; After='// MUTANT: no retired tombstone'; Expected='retired tombstone survives stale MainPlayer reassignment' },
        @{ Name='prepared-reset'; File='HandsAreNotBusy/HandsAreNotBusyCompat.cs'; Before='if (!ReferenceEquals(Scope.Prepared, null) && ReferenceEquals(__0, Scope.Prepared)) return false;'; After='// MUTANT: no prepared-body fence'; Expected='prepared old hands fenced' },
        @{ Name='release-before-recovery'; File='HandsAreNotBusy/HandsAreNotBusyCompat.cs'; Before='if (!ReferenceEquals(Scope.Prepared, null) && ReferenceEquals(__0, Scope.Prepared)) return false;'; After='if (Scope.Preparing && ReferenceEquals(__0, Scope.Prepared)) return false;'; Expected='prepared old reset remains fenced until recovery tick' },
        @{ Name='candidate-reset'; File='HandsAreNotBusy/HandsAreNotBusyCompat.cs'; Before=$candidateFence; After='// MUTANT: no candidate fence'; Expected='uncommitted exact candidate hands fenced' },
        @{ Name='orphan-reset'; File='HandsAreNotBusy/HandsAreNotBusyCompat.cs'; Before='if (BodyReplacementContext.OwnsBody(__0) && !ReferenceEquals(__0.GameWorld?.MainPlayer, __0)) return false;'; After='// MUTANT: no failed-candidate lease fence'; Expected='failed candidate is fenced after old-body recovery without prior callback' },
        @{ Name='old-component-active'; File='HandsAreNotBusy/HandsAreNotBusyCompat.cs'; Before='((Behaviour)component).enabled = false;'; After='// MUTANT: old component stays enabled'; Expected='only old native component disabled after commit' },
        @{ Name='destroyed-native-body-access'; File='HandsAreNotBusy/HandsAreNotBusyCompat.cs'; Before='ReferenceEquals(component.gameObject, _oldGameObject)'; After='ReferenceEquals(component.gameObject, oldBody.gameObject)'; Expected='failed attempt rebinds verified survivor despite destroyed old wrapper' },
        @{ Name='old-recovery-pending'; File='HandsAreNotBusy/HandsAreNotBusyCompat.cs'; Before='Scope.RecoverPrepared(); _oldComponents = null; _oldGameObject = null; _recover = false;'; After='_oldComponents = null; _oldGameObject = null; _recover = false;'; Expected='old-body recovery resumes original enabled states' },
        @{ Name='unguarded-unpatch'; File='HandsAreNotBusy/HandsAreNotBusyCompat.cs'; Before='if (_patched) { Patches.UnpatchSelf(); _patched = false; }'; After='Patches.UnpatchSelf(); _patched = false;'; Expected='absent polling never scans Harmony' },
        @{ Name='missing-held-flag'; File='HandsAreNotBusy/HandsAreNotBusyCompat.cs'; Before='_patched = true;'; After='// MUTANT: partial install is untracked'; Expected='partial install failure unpatches held hooks' },
        @{ Name='early-release-held-flag'; File='HandsAreNotBusy/HandsAreNotBusyCompat.cs'; Before='if (_patched) { Patches.UnpatchSelf(); _patched = false; }'; After='if (_patched) { _patched = false; Patches.UnpatchSelf(); }'; Expected='partial install cleanup failure retains hook responsibility' }
    )
    foreach ($mutant in $mutants) {
        $folder = Join-Path $evidence $mutant.Name
        $sourceRoot = Join-Path $folder 'Compat'
        New-Item -ItemType Directory -Force $sourceRoot | Out-Null
        Copy-Item (Join-Path $repo 'Redeploy.Core/Compat/HollywoodFx') $sourceRoot -Recurse
        Copy-Item (Join-Path $repo 'Redeploy.Core/Compat/HandsAreNotBusy') $sourceRoot -Recurse
        $file = Join-Path $sourceRoot $mutant.File
        $source = [IO.File]::ReadAllText($file).Replace("`r`n", "`n")
        $before = $mutant.Before.Replace("`r`n", "`n")
        if (-not $source.Contains($before)) { throw "Missing mutation anchor: $($mutant.Name)" }
        [IO.File]::WriteAllText($file, $source.Replace($before, $mutant.After))
        $output = Join-Path $folder 'bin'
        dotnet build $project -c Release -p:SPTModType=Library -p:FutureMods=false "-p:ProductionRoot=$sourceRoot" "-p:OutputPath=$output/" -p:AppendTargetFrameworkToOutputPath=false --nologo > (Join-Path $folder 'build.txt')
        if ($LASTEXITCODE) { throw "Mutant failed to compile: $($mutant.Name)" }
        # Native stderr is expected for rejected mutants; collect it through Process
        # so Windows PowerShell's NativeCommandError does not end the negative control.
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = Join-Path $output 'PopularBodyChecks.exe'
        $start.Arguments = '"' + $PublishedHollywoodDll + '" "' + $PublishedHandsDll + '"'
        $start.UseShellExecute = $false
        $start.RedirectStandardOutput = $true
        $start.RedirectStandardError = $true
        $process = [Diagnostics.Process]::Start($start)
        $stdout = $process.StandardOutput.ReadToEnd()
        $stderr = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        $result = $stdout + $stderr
        [IO.File]::WriteAllText((Join-Path $folder 'checks.txt'), $result)
        if ($process.ExitCode -eq 0) { throw "Surviving mutant: $($mutant.Name)" }
        if (-not $result.Contains('CHECK FAILED: ' + $mutant.Expected)) { throw "Unexpected mutant failure: $($mutant.Name), see $folder" }
        $process.Dispose()
        Write-Output "REJECTED $($mutant.Name)"
    }
}
Write-Output "POPULAR_BODY_CHECKS_PASSED (reviewed/future fixtures, released DLL metadata, actual Harmony, native compile); evidence=$evidence"

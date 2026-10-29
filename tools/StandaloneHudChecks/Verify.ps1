param([string]$PublishedQuestDll, [string]$PublishedAudioDll, [string]$EvidenceRoot, [switch]$SkipMutants)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifact = Join-Path $repo 'artifacts/standalone-compat-20261029/ui'
if (-not $EvidenceRoot) { $EvidenceRoot = $artifact }
$evidence = Join-Path $EvidenceRoot ('checks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $evidence | Out-Null
if (-not $PublishedQuestDll) { $PublishedQuestDll = Join-Path $artifact 'quest-release/BepInEx/Plugins/DrakiaXYZ-QuestTracker/DrakiaXYZ-QuestTracker.dll' }
if (-not $PublishedAudioDll) { $PublishedAudioDll = Join-Path $artifact 'audio-release/BepInEx/plugins/acidphantasm-accessibilityindicators/acidphantasm-accessibilityindicators.dll' }
if (-not (Test-Path -LiteralPath $PublishedQuestDll)) {
    $archive = Join-Path $artifact 'questtracker-1.7.0.7z'
    Invoke-WebRequest 'https://github.com/DrakiaXYZ/SPT-QuestTracker/releases/download/1.7.0/DrakiaXYZ-QuestTracker-1.7.0.7z' -OutFile $archive
    & 'C:/Program Files/7-Zip/7z.exe' x $archive "-o$(Join-Path $artifact 'quest-release')" -y > (Join-Path $evidence 'quest-extract.txt')
    if ($LASTEXITCODE) { throw 'Quest Tracker extraction failed' }
}
if (-not (Test-Path -LiteralPath $PublishedAudioDll)) {
    $archive = Join-Path $artifact 'audio-2.1.0.zip'
    Invoke-WebRequest 'https://github.com/acidphantasm/acidphantasm-accessibilityindicators/releases/download/2.1.0/acidphantasm-accessibilityindicators.zip' -OutFile $archive
    Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $artifact 'audio-release') -Force
}
if ((Get-FileHash -LiteralPath $PublishedQuestDll -Algorithm SHA256).Hash -ne 'A75CC4ADADDE28A7EDC6EF70F89A86AFA981FC6D4829BA0BF8A1E0E76DF1CAB1') { throw 'Quest Tracker release hash mismatch' }
if ((Get-FileHash -LiteralPath $PublishedAudioDll -Algorithm SHA256).Hash -ne '78902CFE2AB1CA47AA26B6277C46E1A440E518801D1584E11D506F15F1778B5F') { throw 'Audio Indicators release hash mismatch' }
$project = Join-Path $PSScriptRoot 'StandaloneHudChecks.csproj'
dotnet build (Join-Path $PSScriptRoot 'NativeCompile.csproj') -c Release -p:SPTModType=Library --nologo > (Join-Path $evidence 'native-build.txt')
if ($LASTEXITCODE) { throw 'Actual EFT compile failed' }
foreach ($future in @('false','true')) {
    dotnet build $project -c Release -p:SPTModType=Library "-p:FutureMods=$future" --nologo > (Join-Path $evidence "build-future-$future.txt")
    if ($LASTEXITCODE) { throw 'Harness build failed' }
    & (Join-Path $PSScriptRoot 'bin/Release/net472/StandaloneHudChecks.exe') $PublishedQuestDll $PublishedAudioDll > (Join-Path $evidence "checks-future-$future.txt") 2>&1
    if ($LASTEXITCODE) { throw "Harness failed (future=$future), see $evidence" }
}
if (-not $SkipMutants) {
    $mutants = @(
        @{ Name='profile-only-unregister'; File='QuestTracker/StandaloneHudScope.cs'; Before='!ReferenceEquals(Prepared, body)'; After='Prepared.Profile.Id != body.Profile.Id'; Expected='same-profile foreign physical unregister not suppressed' },
        @{ Name='retire-final-current'; File='QuestTracker/StandaloneHudScope.cs'; Before='Game = null; World = null; Body = null; Prepared = null; Frozen = false;'; After='if (Body != null) _retired.GetValue(Body, _ => new object()); Game = null; World = null; Body = null; Prepared = null; Frozen = false;'; Expected='ordinary final current-body cleanup still runs after retire' },
        @{ Name='old-quest-controller'; File='QuestTracker/QuestTrackerCompat.cs'; Before='_contract.Controller.SetValue(_component, body.QuestController);'; After='// MUTANT retain old controller'; Expected='quest references follow replacement' },
        @{ Name='old-map-quests'; File='QuestTracker/QuestTrackerCompat.cs'; Before='_contract.MapQuests.SetValue(_component, map);'; After='// MUTANT retain old map quests'; Expected='both quest caches rebuilt with new quest objects' },
        @{ Name='late-old-dispose'; File='QuestTracker/StandaloneHudScope.cs'; Before='if (Retired(body)) return true;'; After='// MUTANT late callbacks allowed'; Expected='late old-body disposal fenced' },
        @{ Name='cancel-keeps-prepared'; File='QuestTracker/StandaloneHudScope.cs'; Before='Prepared = null;'; After='// MUTANT keep preparation'; Expected='cancel leaves adapters ready for next replacement' },
        @{ Name='recovery-stays-frozen'; File='AudioIndicators/AudioIndicatorsCompat.cs'; Before='Scope.Prepared != null && Scope.Exact(Scope.Prepared)'; After='Scope.Prepared != null && !Scope.Frozen && Scope.Exact(Scope.Prepared)'; Expected='recovered old body resumes formerly-active compass' },
        @{ Name='recovery-retires-survivor'; File='QuestTracker/StandaloneHudScope.cs'; Before='Body = Prepared; Prepared = null; Frozen = false;'; After='Body = Prepared; _retired.GetValue(Body, _ => new object()); Prepared = null; Frozen = false;'; Expected='old-body recovery clears preparation without tombstone' },
        @{ Name='recovery-does-not-resume'; File='AudioIndicators/AudioIndicatorsCompat.cs'; Before='if (_resumeRotation) _contract.Active.SetValue(_rotation, true);'; After='// MUTANT leave compass stopped'; Expected='recovered old body resumes formerly-active compass' }
    )
    foreach ($mutant in $mutants) {
        $folder = Join-Path $evidence $mutant.Name
        $sourceRoot = Join-Path $folder 'Compat'
        New-Item -ItemType Directory -Force $sourceRoot | Out-Null
        Copy-Item (Join-Path $repo 'Redeploy.Core/Compat/QuestTracker') $sourceRoot -Recurse
        Copy-Item (Join-Path $repo 'Redeploy.Core/Compat/AudioIndicators') $sourceRoot -Recurse
        Copy-Item (Join-Path $repo 'Redeploy.Core/Compat/BodyReplacementContext.cs') $sourceRoot
        $file = Join-Path $sourceRoot $mutant.File
        $source = [IO.File]::ReadAllText($file)
        if (-not $source.Contains($mutant.Before)) { throw "Missing mutation anchor: $($mutant.Name)" }
        [IO.File]::WriteAllText($file, $source.Replace($mutant.Before, $mutant.After))
        $output = Join-Path $folder 'bin'
        dotnet build $project -c Release -p:SPTModType=Library -p:FutureMods=false "-p:ProductionRoot=$sourceRoot" "-p:OutputPath=$output/" -p:AppendTargetFrameworkToOutputPath=false --nologo > (Join-Path $folder 'build.txt')
        if ($LASTEXITCODE) { throw "Mutant failed to compile: $($mutant.Name)" }
        & (Join-Path $output 'StandaloneHudChecks.exe') $PublishedQuestDll $PublishedAudioDll > (Join-Path $folder 'checks.txt') 2>&1
        if ($LASTEXITCODE -eq 0) { throw "Surviving mutant: $($mutant.Name)" }
        if (-not ([IO.File]::ReadAllText((Join-Path $folder 'checks.txt'))).Contains('CHECK FAILED: ' + $mutant.Expected)) { throw "Unexpected mutant failure: $($mutant.Name)" }
        Write-Output "REJECTED $($mutant.Name)"
    }
}
Write-Output "STANDALONE_HUD_CHECKS_PASSED (reviewed/future releases, actual Harmony, native compile); evidence=$evidence"

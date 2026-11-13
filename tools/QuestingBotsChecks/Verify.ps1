param([string]$PublishedDll, [switch]$SkipMutants)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if (-not $PublishedDll) { $PublishedDll = Join-Path $repo 'artifacts\blocked-mod-review-20261113\questing\release\BepInEx\plugins\QuestingBots\QuestingBots-Client.dll' }
if (-not (Test-Path -LiteralPath $PublishedDll -PathType Leaf)) { throw 'Supply the audited released Questing Bots 1.2.0 client DLL with -PublishedDll.' }
dotnet build (Join-Path $PSScriptRoot 'NativeCompile.csproj') -c Release --nologo -p:BaseIntermediateOutputPath=obj-native/
if ($LASTEXITCODE) { throw 'Questing Bots adapter did not compile against installed EFT.' }
$project = Join-Path $PSScriptRoot 'QuestingBotsChecks.csproj'
dotnet build $project -c Release --nologo
if ($LASTEXITCODE) { throw 'Questing Bots harness build failed.' }
$exe = Join-Path $PSScriptRoot 'bin\Release\net472\QuestingBotsChecks.exe'
foreach ($mode in @('normal','newer','older','drift','identity')) {
    & $exe $mode
    if ($LASTEXITCODE) { throw ('Questing Bots behavior check failed: ' + $mode) }
}
& $exe published $PublishedDll
if ($LASTEXITCODE) { throw 'Questing Bots released metadata check failed.' }
if (-not $SkipMutants) {
    $sourcePath = Join-Path $repo 'Redeploy.Core\Compat\QuestingBots\QuestingIslandState.cs'
    $source = [IO.File]::ReadAllText($sourcePath)
    $mutants = @(
        @{ Name='retained-membership'; Before='Players.RemoveAll(player => ReferenceEquals(player, body));'; After='// Deliberately retain the stale physical member.'; Expected='unregistration retires exact body and preserves alias peer' },
        @{ Name='profile-alias-cleanup'; Before='row.Group.Allies.RemoveAll(ally => ReferenceEquals(ally, body));'; After='row.Group.Allies.RemoveAll(ally => ally?.Profile.Id == body.Profile.Id);'; Expected='unregistration retires exact body and preserves alias peer' },
        @{ Name='retained-enemy-snapshot'; Before='var enemyRows = Strip(Enemies, body);'; After='var enemyRows = new List<KeyValuePair<Player, IPlayer[]>>();'; Expected='snapshots drop exact old references and preserve alias peer' }
    )
    $evidence = Join-Path $PSScriptRoot ('artifacts\mutants-' + [guid]::NewGuid().ToString('N'))
    foreach ($mutant in $mutants) {
        if (-not $source.Contains($mutant.Before)) { throw ('Missing mutation anchor: ' + $mutant.Name) }
        $folder = Join-Path $evidence $mutant.Name
        New-Item -ItemType Directory -Path $folder -Force | Out-Null
        Copy-Item -Path (Join-Path $repo 'Redeploy.Core\Compat\QuestingBots\*.cs') -Destination $folder
        [IO.File]::WriteAllText((Join-Path $folder 'QuestingIslandState.cs'), $source.Replace($mutant.Before, $mutant.After))
        dotnet build $project -c Release --nologo "-p:CompatSource=$folder\*.cs" "-p:BaseIntermediateOutputPath=$folder\obj\" "-p:OutputPath=$folder\bin\" -p:AppendTargetFrameworkToOutputPath=false > (Join-Path $folder 'build.txt')
        if ($LASTEXITCODE) { throw ('Mutant build failed: ' + $mutant.Name) }
        & (Join-Path $folder 'bin\QuestingBotsChecks.exe') normal > (Join-Path $folder 'test.txt') 2>&1
        if ($LASTEXITCODE -eq 0) { throw ('Surviving mutant: ' + $mutant.Name) }
        if (-not ([IO.File]::ReadAllText((Join-Path $folder 'test.txt')).Contains('System.Exception: ' + $mutant.Expected))) { throw ('Mutant failed for unexpected reason: ' + $mutant.Name) }
        Write-Output ('REJECTED ' + $mutant.Name)
    }
}
Write-Output 'QUESTING_BOTS_CHECKS_PASSED'

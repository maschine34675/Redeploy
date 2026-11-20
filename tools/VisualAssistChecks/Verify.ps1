param([Parameter(Mandatory=$true)][string]$ReleasedDll)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Push-Location $repo
try {
    dotnet build Redeploy.Core/Redeploy.Core.csproj -c Release -p:SPTModType=Library
    if ($LASTEXITCODE -ne 0) { throw 'Core no-deploy build failed' }
    dotnet run --project tools/VisualAssistChecks -c Release -- $ReleasedDll
    if ($LASTEXITCODE -ne 0) { throw 'VisualAssist checks failed' }
    foreach ($fault in @('--creation-awake', '--creation-swallowed', '--creation-owner')) {
        dotnet run --project tools/VisualAssistChecks -c Release -- $fault
        if ($LASTEXITCODE -ne 0) { throw ('VisualAssist allocation rollback failed: ' + $fault) }
    }
    dotnet run --project tools/BodyReplacementChecks -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Body replacement coordinator checks failed' }
    # A path that must not exist disables the server's deploy target (AGENTS.md); it used to name the worktree
    # this harness was written in, which is gone.
    $neverDeploy = Join-Path $repo 'artifacts\never-deploy-visualassist'
    if (Test-Path -LiteralPath $neverDeploy) { throw ('No-deploy sentinel exists: ' + $neverDeploy) }
    dotnet test Redeploy.Tests/Redeploy.Tests.csproj -c Release -p:SPTModType=Library "-p:SPTUserMods=$neverDeploy" --filter FullyQualifiedName~SourceGuardTests
    if ($LASTEXITCODE -ne 0) { throw 'Core source guards failed' }
    Write-Output 'VISUAL_ASSIST_OFFLINE_VERIFIED'
}
finally { Pop-Location }

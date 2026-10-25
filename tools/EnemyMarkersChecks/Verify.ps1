$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
Push-Location $repo
try {
    dotnet run --project tools/EnemyMarkersChecks/EnemyMarkersChecks.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Production-linked Enemy Markers checks failed.' }
    dotnet test Redeploy.Tests/Redeploy.Tests.csproj -c Release --filter 'FullyQualifiedName~EnemyMarkersTests'
    if ($LASTEXITCODE -ne 0) { throw 'Pure Enemy Markers scope tests failed.' }
    Write-Output 'ENEMY_MARKERS_VERIFIED'
}
finally { Pop-Location }

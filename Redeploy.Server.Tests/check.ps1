$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$sentinel = Join-Path $repo 'artifacts\NO-DEPLOY'
if (Test-Path -LiteralPath $sentinel) { throw 'The no-deploy sentinel must not exist.' }
dotnet build (Join-Path $repo 'Redeploy.Server\Redeploy.Server.csproj') -c Release -p:SPTModType=Library "-p:SPTUserMods=$sentinel" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Server build failed.' }
dotnet test (Join-Path $PSScriptRoot 'Redeploy.Server.Tests.csproj') -c Release -p:SPTModType=Library "-p:SPTUserMods=$sentinel" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Server tests failed.' }
if (Test-Path -LiteralPath $sentinel) { throw 'Unexpected deploy target was created.' }
Write-Output 'SCAV_SERVER_CHECKS_OK'

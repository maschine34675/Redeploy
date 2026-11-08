param([string]$PublishedDll)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (!$PublishedDll) {
    $folder = Join-Path $repo 'artifacts/memods-20261108/plate-release'
    $PublishedDll = Join-Path $folder 'BepInEx/plugins/PLATE/PLATE.Client.dll'
    if (!(Test-Path -LiteralPath $PublishedDll)) {
        New-Item -ItemType Directory -Force $folder | Out-Null
        $zip = Join-Path $folder 'PLATE.zip'
        Invoke-WebRequest 'https://github.com/Anamelash/spt-plate/releases/download/v1.6.0/PLATE-1.6.0.zip' -OutFile $zip
        Expand-Archive -LiteralPath $zip -DestinationPath $folder -Force
    }
}
dotnet build (Join-Path $PSScriptRoot 'PlateChecks.csproj') -c Release -p:SPTModType=Library --nologo
if ($LASTEXITCODE) { throw 'PLATE fixture compile failed' }
& (Join-Path $PSScriptRoot 'bin/Release/net472/PLATE.Client.exe') $PublishedDll
if ($LASTEXITCODE) { throw 'PLATE fixture failed' }

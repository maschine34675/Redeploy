param([string]$PublishedDll)
$ErrorActionPreference = 'Stop'
if (-not $PublishedDll) {
    $audit = Join-Path $PSScriptRoot '../../../artifacts/optional-body-20261108/vicinity'
    $PublishedDll = Join-Path $audit 'package/BepInEx/plugins/Softwyx-LootInVicinity/Softwyx-LootInVicinity.dll'
    if (-not (Test-Path -LiteralPath $PublishedDll)) {
        New-Item -ItemType Directory -Path $audit -Force | Out-Null
        $archive = Join-Path $audit '3.0.6.zip'
        Invoke-WebRequest 'https://github.com/FallegaHQ/SPT-LootInVicinity/releases/download/3.0.6/Softwyx-LootInVicinity-3.0.6.zip' -OutFile $archive
        Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $audit 'package') -Force
    }
}
if ((Get-FileHash -LiteralPath $PublishedDll).Hash -ne '00960BD87F237E4C9EBEC961074778D3543DE5A96CA382A7C51A8A63316FD3E3') {
    throw 'Unexpected Loot In Vicinity release DLL'
}
dotnet run --project (Join-Path $PSScriptRoot 'VicinityChecks.csproj') -c Release -p:SPTModType=Library -- $PublishedDll
exit $LASTEXITCODE

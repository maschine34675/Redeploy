param([string]$PublishedDll)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
if (-not $PublishedDll) {
    $audit = Join-Path $repo 'artifacts/optional-body-20261108/ladders'
    $PublishedDll = Join-Path $audit 'package/BepInEx/plugins/tarkin-ladders/tarkin.ladders.bep.dll'
    if (-not (Test-Path -LiteralPath $PublishedDll)) {
        New-Item -ItemType Directory -Path $audit -Force | Out-Null
        $archive = Join-Path $audit 'v1.0.4.zip'
        Invoke-WebRequest 'https://github.com/bmpq/spt-ladders/releases/download/v1.0.4/tarkin-ladders-v1.0.4.zip' -OutFile $archive
        Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $audit 'package') -Force
    }
}
if ((Get-FileHash -LiteralPath $PublishedDll).Hash -ne 'D58953593254E98648B7FB5A9F2DA22ABED200FD3FEB5ABCBC9D81555655FB44') { throw 'Unexpected Climbable Ladders client' }
dotnet run --project (Join-Path $PSScriptRoot 'LaddersChecks.csproj') -c Release -p:SPTModType=Library -- $PublishedDll
exit $LASTEXITCODE

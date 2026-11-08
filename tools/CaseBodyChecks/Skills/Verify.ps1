param([string]$PublishedDll)
$ErrorActionPreference = 'Stop'
if (-not $PublishedDll) {
    $audit = Join-Path $env:TEMP 'redeploy-skills-314-audit'
    New-Item -ItemType Directory -Path $audit -Force | Out-Null
    $PublishedDll = Join-Path $audit 'reviewed-skills314.dll'
    if (-not (Test-Path -LiteralPath $PublishedDll)) {
        $archive = Join-Path $audit 'SkillsExtended-3.1.4.zip'
        if (-not (Test-Path -LiteralPath $archive)) {
            Invoke-WebRequest 'https://github.com/CJ-SPT/Skills-Extended/releases/download/V3.1.4/SkillsExtended-3.1.4-Release.zip' -OutFile $archive
        }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            $entry = $zip.GetEntry('BepInEx/plugins/SkillsExtended/SkillsExtended.Client.Skills.dll')
            [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $PublishedDll, $true)
        } finally { $zip.Dispose() }
    }
}
if ((Get-FileHash -LiteralPath $PublishedDll).Hash -ne '6AF2B7461E39D47B1482E8B16BBF6907BC9E6614DDA3A67188E75D5D4E0A3EC5') {
    throw 'Skills Extended 3.1.4 release DLL hash mismatch'
}
dotnet build (Join-Path $PSScriptRoot 'SkillsChecks.csproj') -c Release -p:SPTModType=Library --nologo
if ($LASTEXITCODE) { throw 'Skills checks build failed' }
& (Join-Path $PSScriptRoot 'bin/Release/net472/SkillsExtended.Client.Skills.exe') $PublishedDll
if ($LASTEXITCODE) { throw 'Skills checks failed' }

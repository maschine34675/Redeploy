param([string] $PublishedDll = "D:\SPT41\Development\ArenaMode\artifacts\compat-seven-20261025\visual\pip-release\BepInEx\plugins\PiP-Disabler\PiP-Disabler.dll")
$ErrorActionPreference = 'Stop'
& dotnet run --project (Join-Path $PSScriptRoot 'PipDisablerChecks.csproj') -c Release -- $PublishedDll
if ($LASTEXITCODE -ne 0) { throw 'PiP Disabler production adapter / released contract checks failed.' }
& dotnet build (Join-Path $PSScriptRoot 'NativeCompile.csproj') -c Release -p:MSBuildProjectExtensionsPath=obj-native/ -v:q
if ($LASTEXITCODE -ne 0) { throw 'PiP Disabler actual EFT API compile failed.' }

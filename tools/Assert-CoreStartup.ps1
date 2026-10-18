# Metadata-only check of shipped client entry points; no Unity/Core assembly load.
param([Parameter(Mandatory)][string[]]$ClientPaths,
      [string]$CecilPath = 'D:\SPT41\BepInEx\core\Mono.Cecil.dll')
$ErrorActionPreference = 'Stop'
Add-Type -Path $CecilPath
foreach ($path in $ClientPaths) {
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path -LiteralPath $path).Path)
    try {
        $plugins = @($assembly.MainModule.Types | Where-Object {
            @($_.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' }).Count -gt 0
        })
        if ($plugins.Count -ne 1) { throw "Expected one client plugin: $path" }
        $plugin = $plugins[0]
        $minimum = @($assembly.CustomAttributes | Where-Object {
            $_.AttributeType.FullName -eq 'System.Reflection.AssemblyMetadataAttribute' -and
            $_.ConstructorArguments[0].Value -eq 'RedeployCoreBuiltAgainst'
        })
        if ($minimum.Count -ne 1 -or -not [Version]::Parse($minimum[0].ConstructorArguments[1].Value)) { throw 'Missing minimum version' }
        $awake = @($plugin.Methods | Where-Object Name -eq Awake)[0]
        $instructions = @($awake.Body.Instructions | ForEach-Object { [string]$_.Operand })
        $calls = $instructions -join "`n"
        if ($calls -match 'Redeploy.Core\.') { throw 'Awake references Core before its JIT boundary' }
        $gate = @($instructions | Where-Object { $_ -match 'CoreVersionGate::Check' })
        $runtime = @($plugin.Methods | Where-Object Name -eq InitializeRuntime)
        if ($gate.Count -ne 1 -or $runtime.Count -ne 1 -or
            $calls.IndexOf('CoreVersionGate::Check') -gt $calls.IndexOf('::InitializeRuntime')) { throw 'Missing or late startup guard' }
        foreach ($name in @('InitializeRuntime') + $(if ($plugin.Namespace -eq 'ArenaMode') { @('StartRuntime', 'UpdateRuntime', 'ShutdownRuntime') })) {
            $method = @($plugin.Methods | Where-Object Name -eq $name)
            if ($method.Count -ne 1 -or ($method[0].ImplAttributes -band [Mono.Cecil.MethodImplAttributes]::NoInlining) -eq 0) {
                throw "Missing non-inlined runtime boundary: $name"
            }
        }
        if ($calls -notmatch 'set_enabled' -or $calls -notmatch 'DependencyErrors') { throw 'Missing user-facing rejection' }
        if ($plugin.Namespace -eq 'ArenaMode') {
            foreach ($name in @('Start','Update','OnDestroy')) {
                $method = @($plugin.Methods | Where-Object Name -eq $name)[0]
                $body = ($method.Body.Instructions | ForEach-Object { [string]$_.Operand }) -join "`n"
                if ($body -notmatch '_runtimeStarted' -or $body -match 'Redeploy.Core\.|ArenaBigBrainLibrary::') { throw "Unguarded callback: $name" }
            }
        }
        $shared = $assembly.MainModule.GetType('Redeploy.Drivers.CoreVersionGate')
        foreach ($method in $shared.Methods | Where-Object HasBody) {
            if ((($method.Body.Instructions | ForEach-Object { [string]$_.Operand }) -join "`n") -match 'Redeploy.Core\.[A-Za-z]+::') {
                throw 'Version gate references Core runtime members'
            }
        }
        Write-Output "CORE_BINARY_GUARD_OK $($plugin.FullName) minimum=$($minimum[0].ConstructorArguments[1].Value)"
    } finally { $assembly.Dispose() }
}
Write-Output 'CORE_BOUNDARIES_VERIFIED'

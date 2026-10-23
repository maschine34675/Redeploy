param([string]$PublishedAssembly)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project=Join-Path $PSScriptRoot 'ContinuousLoadAmmoCompatChecks.csproj'
$source=Join-Path $repo 'Redeploy.Core/Compat'
if(!$PublishedAssembly){$PublishedAssembly='D:/SPT41/Development/ArenaMode/artifacts/ammo-raid-health-compat-20261019/releases/cla/extracted/BepInEx/plugins/ozen.ContinuousLoadAmmo/ozen.ContinuousLoadAmmo.dll'}
if(!(Test-Path -LiteralPath $PublishedAssembly -PathType Leaf)){throw 'Supply the separately downloaded published CLA 1.2.0 DLL; metadata only, never installed or loaded.'}
$run=[guid]::NewGuid().ToString('N');$evidence=Join-Path $repo "artifacts/continuous-load-ammo-checks/$run"
[IO.Directory]::CreateDirectory($evidence)|Out-Null
$files=@('ContinuousLoadAmmoCompat.cs','ContinuousLoadAmmoContract.cs','ContinuousLoadAmmoScope.cs');$original=@{}
foreach($name in $files){$original[$name]=[IO.File]::ReadAllText((Join-Path $source $name))}
$faults=0
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'obj/project.assets.json') -PathType Leaf)) {
 & dotnet restore $project --disable-build-servers -p:UseSharedCompilation=false -p:NuGetAudit=false --ignore-failed-sources
 if ($LASTEXITCODE -ne 0) { throw 'Offline harness restore failed.' }
}
function Check([string]$Name,[string]$File='',[string]$Find='',[string]$Replace='',[string]$Failure=''){
 $copy=Join-Path $evidence $Name;[IO.Directory]::CreateDirectory($copy)|Out-Null
 foreach($sourceName in $files){$content=$original[$sourceName];if($sourceName -eq $File){if([regex]::Matches($content,[regex]::Escape($Find)).Count -ne 1){throw "Ambiguous mutation: $Name"};$content=$content.Replace($Find,$Replace)};[IO.File]::WriteAllText((Join-Path $copy $sourceName),$content)}
 $log=Join-Path $copy 'result.log'
 & dotnet run --project $project -c Release --no-restore --disable-build-servers -p:UseSharedCompilation=false "-p:ProductionRoot=$copy" -- $PublishedAssembly *> $log
 $exit=$LASTEXITCODE;$output=[IO.File]::ReadAllText($log)
 if($Failure){if($exit -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains("ASSERT: $Failure")){throw "Fault failed wrong check: $Name ($log)`n$output"};$script:faults++;Write-Output "CLA_MUTANT_REJECTED: $Name"}
 elseif($exit -ne 0 -or $output -notmatch 'CONTINUOUS_LOAD_AMMO_COMPAT_PASSED \(\d+\)'){throw "Production checks failed ($log)`n$output"}
 else{Write-Output $output.Trim()}
}
Check 'before'
Check 'ordinary-authority' 'ContinuousLoadAmmoScope.cs' '|| !exactRaid || !local' '|| !local' 'ordinary scope is not captured'
Check 'body-world' 'ContinuousLoadAmmoScope.cs' '!ReferenceEquals(bodyWorld, currentWorld) || ' '' 'different physical world is not captured'
Check 'local-identity' 'ContinuousLoadAmmoScope.cs' '|| !exactRaid || !local' '|| !exactRaid' 'bot scope is not captured'
Check 'main-identity' 'ContinuousLoadAmmoScope.cs' '|| !ReferenceEquals(body, mainPlayer)' '' 'non-main body is not captured'
Check 'inventory-owner' 'ContinuousLoadAmmoScope.cs' '&& ReferenceEquals(lease.Inventory, inventory)' '' 'inventory identity check'
Check 'controller-owner' 'ContinuousLoadAmmoScope.cs' '&& ReferenceEquals(lease.Controller, controller)' '' 'controller identity check'
Check 'world-owner' 'ContinuousLoadAmmoScope.cs' '&& ReferenceEquals(lease.World, world)' '' 'world identity check'
Check 'body-owner' 'ContinuousLoadAmmoScope.cs' '&& ReferenceEquals(lease.Body, mainPlayer)' '' 'body identity check'
Check 'dispose-once' 'ContinuousLoadAmmoScope.cs' 'if (lease == null || lease.Disposed) return false;' 'if (lease == null) return false;' 'retire exactly once'
Check 'static-successor' 'ContinuousLoadAmmoScope.cs' '=> !(disposingOwned || currentOwned) || disposing != null && ReferenceEquals(disposing, current);' '=> true;' 'old owned dispose cannot clear ordinary successor'
Check 'delayed-epoch' 'ContinuousLoadAmmoScope.cs' '&& lease.Revision == ticket' '' 'new same-body load cancels delayed unlock'
Check 'active-load' 'ContinuousLoadAmmoScope.cs' '&& current && !active && !multiSelect' '&& current && !multiSelect' 'active load prevents delayed restore'
Check 'multiselect-load' 'ContinuousLoadAmmoScope.cs' '&& current && !active && !multiSelect' '&& current && !active' 'MultiSelect pending load preserves limit'
Check 'global-unsubscribe' 'ContinuousLoadAmmoCompat.cs' '_contract.EndEvent.RemoveEventHandler(null, Delegate.CreateDelegate(typeof(Action), controller, _contract.LoadingEnd));' '' 'pre-adoption task cannot broadcast onto successor'
Check 'missing-rebind-dispose' 'ContinuousLoadAmmoCompat.cs' 'if (lease != null && !lease.Disposed) _contract.Dispose.Invoke(old, null);' '' 'standalone live entry controller adopted and retired'
Check 'global-broadcast' 'ContinuousLoadAmmoCompat.cs' '=> _contract == null || Scope.ForTask(__0) == null;' '=> true;' 'owned task does not broadcast to ordinary subscribers'
Check 'completion-owner' 'ContinuousLoadAmmoCompat.cs' '!ContinuousLoadAmmoScope.IsLatest(completion) || !Current(lease)' '!ContinuousLoadAmmoScope.IsLatest(completion)' 'old task cannot notify following world'
Check 'task-epoch' 'ContinuousLoadAmmoScope.cs' '&& completion.Operation == completion.Owner.Operation' '' 'old same-body task epoch rejected'
Check 'binding-revocation' 'ContinuousLoadAmmoCompat.cs' ' || !ReferenceEquals(Scope.Find(lease.Controller), lease)' '' 'revoked binding cannot resume after reinitialization'
Check 'foreign-il-hook' 'ContinuousLoadAmmoContract.cs' '.Concat(info.ILManipulators)' '' 'unknown foreign IL hook rejects replacement'
Check 'capture-order' 'ContinuousLoadAmmoCompat.cs' 'if (captureIndex < 0 || broadcastIndex < 0 || captureIndex >= broadcastIndex)' 'if (false)' 'foreign broadcast before capture rejects replacement'
Check 'scope-cleanup' 'ContinuousLoadAmmoScope.cs' '{ _controllers = new(); _inventories = new(); _tasks = new(); _limitControllers = new(); _limitMovements = new(); }' '{ }' 'scope cleanup releases leases'
Check 'entry-preflight-identity' 'ContinuousLoadAmmoCompat.cs' 'if (controller != null && (!ReferenceEquals(_contract.Body.GetValue(controller), oldBody)' 'if (false && controller != null && (!ReferenceEquals(_contract.Body.GetValue(controller), oldBody)' 'unrelated entry controller rejected without teardown'
Check 'rollback-tick' 'ContinuousLoadAmmoCompat.cs' 'if (_contract == null || BodyReplacementContext.IsCreating) return;' 'if (true) return;' 'failed creation cleans candidate and restores old controller'
Check 'rollback-movement' 'ContinuousLoadAmmoCompat.cs' 'if (lease?.HasLimit == true && lease.Body is Player oldBody && oldBody != null' 'if (false && lease?.HasLimit == true && lease.Body is Player oldBody && oldBody != null' 'initial unowned live load rollback releases only observed native limit'
Check 'stale-task-fault' 'ContinuousLoadAmmoCompat.cs' 'if (_contract != null && ContinuousLoadAmmoScope.IsLatest(completion) && Current(completion.Owner)) Fault(ex);' 'Fault(ex);' 'retired task fault cannot poison successor admission'
Check 'registration-finalizer' 'ContinuousLoadAmmoCompat.cs' '_contract.Dispose.Invoke(controller, null);' '' 'partial registration failure disposes exact candidate controller'
Check 'initial-limit-adoption' 'ContinuousLoadAmmoScope.cs' 'lease.HasLimit = observed.Applied && !observed.Mixed && !observed.Unknown;' 'lease.HasLimit = false;' 'exact successful upstream limit is transferred'
Check 'actual-native-removal' 'ContinuousLoadAmmoCompat.cs' 'if (__0 == Player.ESpeedLimit.BarbedWire) Scope.ObserveLimitRemoved(__instance);' '' 'completed initial load does not claim later unrelated restriction'
Check 'shared-limit-authority' 'ContinuousLoadAmmoScope.cs' 'Mixed = present && (!observation.Applied || observation.Mixed);' 'Mixed = false;' 'pre-existing shared restriction rejects safely without removal'
Check 'initial-observation' 'ContinuousLoadAmmoCompat.cs' 'if (_contract != null && __0 && _contract.Body.GetValue(__instance) is LocalPlayer observedBody' 'if (false && _contract != null && __0 && _contract.Body.GetValue(__instance) is LocalPlayer observedBody' 'initial unowned live load rollback releases only observed native limit'
Check 'observation-movement-owner' 'ContinuousLoadAmmoScope.cs' '&& ReferenceEquals(observed.Movement, movement))' ')' 'observed limit requires exact movement'
Check 'after'
foreach($name in $files){if([IO.File]::ReadAllText((Join-Path $source $name)) -ne $original[$name]){throw "Production source changed during checks: $name"}}
Write-Output "CONTINUOUS_LOAD_AMMO_COMPAT_CHECKS_OK; $faults compiled negative controls; evidence: $evidence"

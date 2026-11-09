# Redeploy — working notes for agents

Redeploy lets a PMC respawn after death in a local SPT 4.1 raid, loot the own corpse and
continue, or continue as the own Scav; `Redeploy.Core` is the shared respawn engine that
ArenaMode and Killcam consume. Player-facing documentation is [README.md](README.md); the
documentation index is [docs/README.md](docs/README.md); the dated working state is
[docs/REPOSITORY-STATUS.md](docs/REPOSITORY-STATUS.md). The design, the frozen API contract
and the dated implementation history (German) are in [docs/RESPAWN-API.md](docs/RESPAWN-API.md).
The earlier CorpseRun handoff with the Stufe-0/1 engine notes is archived verbatim in
[docs/history/CORPSERUN-HANDOFF.md](docs/history/CORPSERUN-HANDOFF.md). Its paths, names,
test counts and open items are historical; its "Wichtige Entscheidungen", the Codex-review
verdicts of 2026-07-30 and the PlayerLives update analysis of the same day still stand except
where this file or the source supersedes them (the cursor row: now `CursorRequestNode.Want`,
no per-frame forcing; the raid-end blocking row: see the keep-alive set under
non-negotiable 1; toasts are used today).

## Layout

| Path | What |
|---|---|
| `Redeploy.Client/` | BepInEx plugin `maschine-Redeploy` (assembly `maschine-Redeploy.Client.dll`, netstandard2.1, GUID `com.maschine.Redeploy`, shared with the server mod): the respawn prompt, cooldown, spawn protection, respawn item, Continue as Scav. A *driver* of Core; all client patches derive from SPT's `ModulePatch`. |
| `Redeploy.Core/` | The shared engine `maschine-Redeploy.Core.dll`, deployed flat into `BepInEx/plugins` and shared with ArenaMode: death interception, `RespawnEngine.RunRespawn`, body/camera rewiring, compatibility helpers, the public `Redeploy.Core.Api.RespawnApi`. Own version line in `CoreVersion.props`. |
| `Redeploy.Server/` | SPT server mod `maschine-Redeploy.Server` (net10.0; patches derive from `AbstractPatch`): `/redeploy/...` routes, inventory root repair, Scav continuation and recovery. Deploys to `SPT_Runtime/user/mods/Redeploy/`. |
| `Redeploy.Tests/`, `Redeploy.Server.Tests/` | xunit (net10.0). Besides unit tests of the linked Core/Client sources: `ApiContractTests` (frozen v1 surface against the built Core and the installed consumers), `CoreVersionTests` (version line and driver stamps), `SourceGuardTests` (golden statement order of `RunRespawn`, event-raise positive list, Freecam patches inside `try`), `CoreBindingTests` (the interop file bound to the real Core). |
| `drivers/CoreVersionGate.cs` | Linked into every driver (Redeploy.Client, ArenaMode.Client): refuses an older or unknown Core before any Core type is touched. Must not use Core types. |
| `interop/RedeployRespawnInterop.cs` | Master copy of the `LocalBodyTracker` that consumers copy verbatim (only the namespace changes); binds by reflection, falls back to polling `GameWorld.MainPlayer`. Revisioned: every change bumps `LocalBodyTracker.Revision` and the header, stays compatible with API v1 and gets a line under "Public API and interop" in `docs/CHANGELOG.md`. `interop/examples/RespawnConsumerExample.cs` and every snippet in `docs/RECIPES.md` and `docs/SOFT-DEPENDENCY.md` that declares a whole type are compiled with it against the game by `CoreBindingTests`. |
| `docs/API.md`, `docs/SOFT-DEPENDENCY.md`, `docs/RULES.md`, `docs/RECIPES.md` | The English consumer contract for other mods. Update them with every consumer-visible change; `docs/RESPAWN-API.md` is the German design record behind them. |
| `tools/` | Offline checks (`RespawnInteropChecks`, `BodyReplacementChecks` and the other `*Checks` projects, each with its own README or Verify.ps1), in-game probes that are never part of a release (`RespawnApiProbe`, `*NativeProbe`), `Assert-CoreStartup.ps1`, `pipeline-timing/`. |

External: `../_decompiled/` (verify every claim about game internals there; the 4.1 server
sources are in `../_reference/server-csharp-main/Libraries/SPTarkov.Server.Core/`),
`../ArenaMode/` (the main Core consumer; its `ArenaMode.Client` builds Core through a project
reference), `../Killcam/` (consumer; its `tools/HandoffChecks` reads this Core),
`../UiBridge/` (GPU test bridge), `D:\SPT41\` (game) and `D:\SPT41\SPT_Runtime\` (server,
profiles, `user/mods/Redeploy`; `Shared.props.user` overrides both paths). Syncthing mirrors
`D:\SPT41` to the GPU hosts (`C:\SPT41`) and MASCHINE11, so a deploying build reaches them
too; native tests pause that Syncthing folder on the host and restore it afterwards.

## Build, deploy, test

**Normal builds deploy into the installation (deliberate dev workflow).** The Client build
also builds and deploys Core; the Server build writes `SPT_Runtime/user/mods/Redeploy`
whenever that `user/mods` folder exists. Close game and server first.

```powershell
dotnet build Redeploy.Client/Redeploy.Client.csproj -c Release
dotnet build Redeploy.Server/Redeploy.Server.csproj -c Release
```

Compile checks and test runs that must not touch the installation:

```powershell
dotnet test Redeploy.Tests/Redeploy.Tests.csproj -c Release -p:SPTModType=Library "-p:SPTUserMods=<a path that does not exist>"
dotnet test Redeploy.Server.Tests/Redeploy.Server.Tests.csproj -c Release -p:SPTModType=Library "-p:SPTUserMods=<a path that does not exist>"
dotnet run --project tools/RespawnInteropChecks -c Release      # success token RESPAWN_INTEROP_CHECKS_PASSED
```

- Every project with a `ProjectReference` on Core deploys Core when built without
  `-p:SPTModType=Library` (Core pins `SPTModType=Client`). That includes ArenaMode's client:
  every ArenaMode build ships the current Core source state. A tool that must never redeploy
  Core puts `AdditionalProperties="SPTModType=Library"` on its Core reference, as
  `tools/RespawnApiProbe` does. To deploy only Redeploy.Client, build it with the switch and
  copy the DLL by hand; otherwise Core is redeployed with a new `ProductVersion`/hash, and
  the GPU tests are bound to installed hashes.
- The server's `DeployToSpt` target is controlled by `SPTUserMods` (a non-existent path
  disables it), not by `SPTModType`.
- `ApiContractTests` and `CoreBindingTests` read the built Core in `Redeploy.Core/bin`
  (Release before Debug) and the installed consumers under `BepInEx/plugins` (root
  overridable with `REDEPLOY_TEST_SPT_ROOT`); `CoreVersionTests` read the newest of
  Release/Debug of Core and of the built drivers (`Redeploy.Client/bin`,
  `../ArenaMode/ArenaMode.Client/bin`). Missing files skip, stale builds fail as version
  skew (`BuiltDriversAreStampedWithTheCoreVersion`, `BuiltCoreCarriesItsOwnVersionLine`):
  rebuild Core and the drivers in Release before judging a red `CoreVersionTests`.
- Golden order: `Redeploy.Tests/Golden/RunRespawn.order.txt` freezes the statement order of
  `RunRespawn`. A deliberate change: run once with `REDEPLOY_UPDATE_GOLDEN=1` (fails on
  purpose), review the diff, then run without. Every statement on the local `timing` inside
  `RunRespawn` stays inert (literals and plain parameter/local names only); the counter
  reflection sits in `RespawnPipelineTiming.Fields` so a missing field never reaches the
  raid-boundary reset.
- In-game regression list, run after any change to the pipeline: death → prompt (cursor
  visible, buttons clickable); respawn → naked, corpse with full body and loot; loot the
  corpse → gear back; extract → no `KeyNotFoundException`, profile intact; death → give up →
  normal raid end; insured gear on the corpse → insurer message after the raid; kills before
  the respawn → on the end screen; Continue as Scav → Scav alive, loot transfer, cooldown.
  Test the PerfectCulling path once without Killcam installed: Killcam's copy of the sampler
  fix masks a regression in Core's own `PerfectCullingFix`. Open engine questions that only
  a raid answers: `docs/RESPAWN-API.md`, section 10.
- Logs: client `D:\SPT41\BepInEx\LogOutput-<HOST>-<start>.log` (local time; `BepInEx\logs\current-<HOST>.txt`
  names the current file; client lines log under `maschine-Redeploy`, Core lines under
  `Redeploy.Core`), server `D:\SPT41\SPT_Runtime\user\logs\spt\spt-<HOST>-YYYYMMDD.log`
  (UTC; messages start with `Redeploy:` or `Redeploy Scav …`). Routine respawn and Scav
  lines are Debug; Info is for player-relevant events only. BepInEx logs rotate per host
  (LogTools keeps the last ten archived sessions and at most 200 MB per host; GPU runs use
  up that host's slots): record playtest evidence in a doc or commit immediately.

## Core versioning and the API contract

- `Redeploy.Core/CoreVersion.props` is the one place Core's version lives. Bump it whenever
  Core changes in a way a driver may rely on; never ship a Core older than the newest one
  published. Drivers stamp it as `RedeployCoreBuiltAgainst` and refuse an older Core at
  startup (`drivers/CoreVersionGate.cs`, Error plus `Chainloader.DependencyErrors`, before
  Core init; keep `InitializeRuntime` NoInlining).
- `RespawnApi.ApiVersion` is 1 and the surface in `Redeploy.Core/Api/api-v1.baseline.txt` is
  frozen. A new public member means `ApiVersion` 2 and a new baseline. The `RespawnApi`
  string constants (`ReasonDeath/ReasonSwap`, mirrored as literals in the interop file;
  `StagePrepare/StageCreate/StageRaidEnding/StageRewire`, delivered through
  `ReplacementFailed`) and the event signatures are part of the contract. Consumers resolve
  the facade by the reflection name `Redeploy.Core.Api.RespawnApi, maschine-Redeploy.Core`
  (hard-coded in every interop copy, Killcam's too): renaming namespace, type or assembly
  fails only at runtime elsewhere.
- Other mods observe only (owner decision 2026-11-09): the supported surface is `RespawnApi`
  through the interop file. Driver registration, `RunRespawn`, `RespawnRequest` and the other
  engine types are for Redeploy's own drivers; third-party drivers are not supported. Core gets
  no adapter for a mod that can integrate itself through the interop file; an adapter is only for
  a mod whose per-body state is unreachable from outside. Additions only as `ApiVersion` 2, never
  a veto or stall. The first external consumer is expected soon; do not name it in public docs
  before it ships.
- Public Core events are raised only through `CoreSignals.<Signal>.Raise(...)` (per
  subscriber isolated, invocation list snapshotted, destroyed Unity targets skipped); the
  legacy events `OnDeathIntercepted`/`OnPlayerRespawned` stay next to `DeathClaimed`/`Replaced`.
- `RunRespawn` refuses before any teardown: `RespawnPreflight` (null request/game/selector,
  `!DeathInterception.IsAvailable`, raid already ending, a call from inside a death or
  pipeline raise, a running pipeline) and then `ProfileReplacementValidation`/`BodyReplacementCompatibility.Prepare`.
  A refusal is a synchronous `onFailure` with no state change and no event; it reaches the
  caller when the coroutine first runs, which Killcam's hold can delay. Callers must tell a
  refusal from a teardown failure.
- Bound by name from other repositories: Killcam resolves
  `Redeploy.Core.Engine.RespawnEngine, maschine-Redeploy.Core`, holds a respawn of its
  replay victim with a Postfix on `RunRespawn`, and reads the `RespawnRequest` fields `Game`
  (`LocalGame`) and `DeadPlayer` (`Player`); its `tools/HandoffChecks` requires exactly one
  public static `RunRespawn(RespawnRequest, Action<Exception>)` returning `IEnumerator`
  (even an overload breaks it) and compares `Redeploy.Core/Compat/StaleSamplerRegistry.cs`
  with Killcam's copy. The installed drivers bind the constructors of
  `NearPositionSpawnSelector`/`InfilSpawnSelector`.
- Releases: the Client/Server version lives in `Shared.props`,
  `Redeploy.Server/Redeploy.Server.csproj` (`<Version>`), `Redeploy.Server/ModMetadata.cs`
  and `Redeploy.Client/Plugin.cs` (`PluginVersion`); bump all four. Core is in
  `CoreVersion.props`. The tag sits on the build commit; ArenaMode bundles the
  byte-identical Core and its packaging requires this checkout at the Core's build commit.
  Docs commits come after the tag.

## Non-negotiable

1. **Fail open to vanilla.** `DeathInterception.EnsurePatched` is fail-closed for Core
   itself (a missing keep-alive patch makes `IsAvailable` false and Core inert: no claim, no
   guard block, `RunRespawn` refused); every driver claims only deaths it can serve, and any
   remaining gap throws so Core falls back to the vanilla death instead of a soft lock. The
   keep-alive set is the six patches `EnsurePatched` installs (`CG_Spawn` and
   `InitiateGameStopping` on `BaseLocalGame<EftGamePlayerOwner>`, `Stop` on the declared
   override `LocalGame.Stop`, `PreloaderUI.StartBlackScreenShow`,
   `SessionResultExitStatus.Show`, `GamePlayerOwner.CleanupOnDestroy`) plus the paired
   `RaidLifecycle` boundaries; a let-through stop calls `IDeathInterceptionDriver.OnExternalStop`
   so the raid still saves. Never leave a raid in a state only Redeploy can end.
2. **Both halves or neither.** Profile persistence after a body swap needs the client
   sanitising references and the server repairing `Inventory.Equipment`; a client without
   the server mod must not offer a respawn (the `/redeploy/ping` check, cached per session).
3. **Fika off.** Redeploy and Core do nothing when `com.fika.core` is loaded
   (`FikaCompatCheck` caches only a positive answer; `DeadBodyGuard` acts only in a
   `LocalGame`); the body swap cannot be synchronised.
4. **Licences.** PlayerLives and RevivalMod inspired the spawn protection and respawn cost;
   no code was taken. Deliberately not adopted from them (user decisions, 2026-07-30): a
   downed/critical state, the `IsAlive=false` bot-ignore hack, renderer blinking, hardcore
   options (headshot/chance), a stamina refill during protection, an "Any" respawn item,
   forced inventory close at death. DoorDash, DynamicMaps, Freecam and Killcam are
   compatibility targets, never bundled.
5. **Adversarial review after every implementation**, verified against `../_decompiled/`;
   it has found the one real defect in nearly every change here.

## Traps

Each cost a session. Verified against the source on 2026-10-30; the archived handoff holds
the long versions.

- The old equipment stays on the corpse and the profile gets a fresh empty `InventoryEquipment`;
  never delete it. Special slots and `RetainedSlots` (SecuredContainer, Scabbard, ArmBand) are
  always transferred, with a rollback onto the corpse when the add fails: the corpse tree is
  never serialized into the post-raid profile, so anything left there is gone. Quest items
  (`Inventory.QuestRaidItems`) survive without a transfer (verified on the 4.0 decompile;
  re-check under 4.1 before relying on it).
- Never touch `Profile.InsuredItems`: stale entries are harmless and the server overwrites
  the client list; filtering once stripped insurance for dropped items and older corpses.
- After the swap `ProfileInventorySanitizer` rebuilds `FastAccess` and drops stale ids from
  `FavoriteItemsStorage`, `DiscardLimits`, `CheckedMagazines`/`CheckedChambers`.
- `KeyNotFoundException` at raid end, or a crash at game start: SPT's `InRaidHelper.SetInventory`
  does not move `Inventory.Equipment` to the new root. Server `InventoryEquipmentFixHelper`
  (fail-closed: only the parentless item with `_tpl == 55d7217a4bdc2d86028b456d`, never an
  arbitrary parentless item) plus client sanitising fix it; a broken profile is rescued by
  pointing `Inventory.equipment` at that item (backup first).
- Kill stats: `RaidVictimsSnapshot` is taken in `RunRespawn` (skipped for the own-Scav
  replacement profile) and restored by `RedeployCoreStatisticsRestorePatch`; a new statistics
  session wipes `Victims` otherwise.
- The respawn item is consumed before the equipment moves to the corpse
  (`RespawnRequest.OnBeforeProfileMutation`; own-Scav uses `ScavRespawnItemCost`).
- `RespawnCooldownSeconds` counts from the death and the pipeline starts at least one frame
  after the click, because the click arrives inside the prompt's OnGUI; there is deliberately
  no countdown notification, because a Killcam hold can outlast it.
- Spawn protection: the game auto-draws the first weapon (the transferred knife) on spawn,
  which would end the protection at once. Stow with `SetEmptyHands` behind the
  `IsChangingWeapon` guard (mid-animation it corrupts the HandsController) and arm the
  early-end check only after the hands were empty once.
- Cursor: request it with `CursorRequestNode.Want(show)`; never set `Cursor.visible`/
  `lockState` per frame (flicker) or use `ToggleShowInGameCursorEvent` (pointer warp).
- The own corpse keeps the first-person render state: each driver calls
  `PlayerBody.UpdatePlayerRenders(ThirdPerson)` once the `Corpse` field is set
  (`FixOwnCorpseRenders` waits up to 10 s); a new driver must too.
- Visual health effects survive a respawn unless `DeathEffectsHelper.RebindEffectsController`
  also calls `EffectsController.OnDisable()` by reflection; `Effects.Clear()` on the profile
  does not reset camera post-processing.
- `PlayerCameraController` sits on the player object: destroy it with `DestroyImmediate`
  (never `Destroy`, whose deferred `OnDestroy` would tear down the NEW controller), recreate
  with `PlayerCameraController.Create(newPlayer)`, then `RepairSharedCameraState` and
  `RebindEffectsController` in the same frame; do not reorder.
- Meds and food hang after a respawn unless `PlayerHealthController.Start()` is called
  (`LocalGame.Spawn()` does it at raid start, the respawn path must do it itself).
- Body-following compat helpers in the rewire block (DoorDash is the worked example:
  `DoorDashCompatHelper` + the pure, tested `DoorDashFollowRule`): never throw, resolve the
  optional mod per call through `Chainloader.PluginInfos`, attach fresh (`DestroyImmediate`
  then `AddComponent`, never GetOrAdd), and run right after `game.LocalPlayer = newPlayer`,
  because from that line ArenaMode recovers a failed swap onto the replacement body. Patches
  that target another plugin are enabled at raid start, not in `Awake` (plugin load order).
  Untested for DoorDash: a Redeploy.Client-driven respawn, a swap failing mid-rewire, the
  official DoorDash download.
- Finalize and other delayed coroutines re-check identity (`game.LocalPlayer`, the owner)
  before touching the camera or `MyPlayer`.
- No new `FindObjectOfType`/`FindObjectsOfType` in paths that run per respawn: their cost
  grows with every corpse (`SceneLookupCache`, `CullingGridPreProcessCache` and
  `OcclusionCullingSwitcherCache` exist for this). A stale `PerfectCullingCrossSceneSampler`
  after a camera recreate throws "Autocull sampler exists" on the next raid; the prefix on
  `Sampler.Start` destroys other instances and is idempotent with Killcam's copy.
- Heap growth without collection in a raid is vanilla: EFT disables the GC for the raid on
  machines with at least 25 GB RAM (SPT setting). Not a Core leak.
- `NearPositionSpawnSelector.Resolve` must never throw: it runs after the old body is torn
  down. New placement decisions go into `NearSpawnPolicy` (pure, tested), world queries into
  `Probe`; the line `Respawn position on <map>: ...` names corpse, target and rejections at
  Debug, and the fallback paths (map spawn point instead of the corpse, spawn point
  unavailable, 2 m above the corpse) at Warning.
- Compile/API: qualify `EFT.InventoryLogic.FastAccess` (HarmonyLib clash); `CompoundItem`
  has no `GetSlot()`.
- A server mod that "does nothing": check the DLL in `user\mods\Redeploy\` (not only in
  `build\`) and its `LastWriteTime` against the build. `SPTPath` is not `SPTUserPath`.

## How to work here

- Commit after every completed change, including tests and documentation; small commits
  whose message explains the reasoning. Do not push without authorization. Verify game
  internals in `../_decompiled/`; when a playtest disagrees with the explanation, the
  explanation is wrong.
- A native GPU cycle with the exact package binaries comes before a release tag.
- New documents go to `docs/` with a row in `docs/README.md`; dated validation records keep
  their date in the name. Update `docs/REPOSITORY-STATUS.md` when handing over.
- User-facing communication is normally German; code, commits and documents are English, except
  the German design record `docs/RESPAWN-API.md` and the probe README.

# Published owned mods and Redeploy body replacement

Assessment date: 2026-10-24. This is a static source/DLL audit, not native gameplay acceptance. No runtime code, builds, installations or profiles were changed.

## Result

**No new mandatory Redeploy.Core adapter was established among the inspected owned mods.** Two small consumer-side improvements are justified as hardening: CombatLog should not finalize a raid whose attempted Stop was vetoed; DualSideDoorBreach can explicitly retire its pending key-consumption context when the local body changes. Neither establishes a failure on every normal Redeploy respawn.

The next release should ship the migrated ArenaMode and Core 1.0.9 together. The current public ArenaMode 0.1.3 still contains its own Recoil Rework Legacy, Continuous Load Ammo and Continuous Healing adapters. Core already detects this mixed-version conflict when the corresponding optional mod is loaded. This is a release-coordination requirement, not another missing adapter.

## Scope and evidence

The public GitHub inventory contains **27 SPT mod repositories**, plus `spt-beta-hub`, which is a catalogue rather than another mod. It agrees with all 22 previously published mods in the local September inventory, plus ArenaMapPort, ArenaMode, CombatLog, Killcam and Redeploy. The four publicly downloadable beta packages are accounted for separately below. Private-only projects, developer probes and retired/absorbed predecessors are not additional published mods in this assessment. SPT 4.0 backports are not substituted for the current SPT 4.1 releases.

For the 24 delegated gameplay, HUD/recording and menu mods, the actual release ZIPs were downloaded and their managed DLLs statically decompiled. Several public tags contain older source than their attached DLLs; a local fix or a tag version constant was therefore not accepted as proof of released behavior. Redeploy and ArenaMode ZIPs were also inspected for DLL identity and their shared Core. ArenaMapPort was reviewed at its published 0.2.3 source tag; its externally hosted map package was not downloaded or binary-certified in this audit. The beta DLLs were decompiled as well; BangAndClear's SPT 4.0 target excludes it from SPT 4.1 compatibility acceptance.

The accompanying [identity manifest](audits/OWNED-MODS-20261024.json) records release tags, tag commits where available, inspected DLL versions and SHA-256 hashes. A tag commit or embedded build stamp is not necessarily a clean build-source commit in the curated repositories. Detailed local snapshots, decompilations and the three independent reviews are retained under the ignored `artifacts/owned-mod-audit-20261024/` directory. Source paths in the tables identify the relevant classes/methods in the inspected release, not an assumption that today's development checkout is the release.

Core baseline: published Redeploy 1.0.0 (`d1d7240`) and ArenaMode 0.1.3 (`c5cee42`) both ship the **same Core 1.0.8 DLL**, SHA-256 `ECC86F4CEAF82661F1CB2E832149369D4C03D462704A9D935ED8DD907DAEB251`. The development comparison uses Core 1.0.9 (`cac56a8`) and Arena's migration (`2a5c239`). The earlier bounded GPU tests for that migration remain documented in [shared body compatibility](BODY-REPLACEMENT-COMPAT-20261023.md); they are not acceptance tests for all combinations listed here.

## Findings to act on

### CombatLog: finalize only an admitted raid end

Published 1.0.0's `Analytics/CombatLogShotPatches.cs`, `CombatLogRaidEndPatch.Prefix`, calls `RaidMeta.EndRaid` and saves before `LocalGame.Stop` runs. The first call sets `RaidMeta.Finished` permanently; later shot/kill capture checks that flag.

Core's `DeathInterception.RedeployCoreStopPatch` can veto `Stop(Killed)` while a continuation is pending. CombatLog's void prefix has no ref arguments, so Harmony can still execute it even when another prefix skips the original ([Harmony prefix contract](https://harmony.pardeike.net/v2/articles/patching-prefix.html)). If another component attempts this Stop during a pending respawn, CombatLog can close a raid that actually continues.

**Important counterexample:** Core claims the normal death earlier, in `CG_Spawn`, and blocks the ordinary death-screen path. A successful ordinary Redeploy respawn therefore does not itself call this Stop. Killcam delaying a genuinely final Stop also does not, by itself, prove loss of later gameplay. The problematic combination was not reproduced in-game here.

Recommended owner: **CombatLog**, using an admitted-end observation such as a postfix gated by `__runOriginal`, retaining its duplicate-save guard and checking actual game state. Do not work around this by suppressing unrelated patches globally in Core.

### DualSideDoorBreach: clear a pending old-body key action

Published 2.0.2's `PendingKeyConsume.Set/TryTake` retains a Player until a matching door callback consumes the context. It checks a three-second lifetime, the exact door, and either its interaction context or a one-frame limit, but does not explicitly require the living, current physical MainPlayer.

Normal death releases the door and stops the relevant animation, so these existing guards may already prevent a stale action in practice. **A wrong key consumption is not established.** Nevertheless, clearing the context on local death/body replacement/raid end, and rejecting a retired physical owner at consumption, would close the ambiguity.

Recommended owner: **DualSideDoorBreach**. Core's existing API/current-body polling is sufficient; no new Core-specific adapter is justified.

### Release coordination: ArenaMode and Core

The actual ArenaMode 0.1.3 release DLL contains `ArenaRecoilLegacyCompat`, `ArenaContinuousLoadAmmoCompat` and `ArenaContinuousHealingCompat`. Development Core 1.0.9's `Compat/BodyReplacementCompatibility.cs`, `LegacyArenaConflict`, refuses that older Arena binary when a corresponding optional mod is loaded, rather than allowing two adapters to own the same components. Without those optional mods this check is inert.

Publish the migrated Arena build alongside Core 1.0.9 and include the same Core DLL in both consumer packages. Do not describe the current public Core 1.0.8 as already providing the new standalone integrations. This requirement is already implemented and documented; this audit adds no runtime change.

## Complete published-mod matrix

“No new Core fix found” means no concrete missing body-replacement adaptation was established by the inspected paths. It is not a blanket certification of every mod combination.

| Mod | Inspected release | Relevant behavior / disposition |
| --- | --- | --- |
| Airburst | [1.1.2](https://github.com/maschine34675/Airburst/releases/tag/v1.1.2) | `AirburstRangeLock` polls current MainPlayer/hands/camera. In-flight projectile state intentionally survives its shooter's death; it stores shot/profile attribution rather than an active Player. No new Core fix found. Fika multiplayer behavior is outside this audit. |
| ArenaMapPort | [0.2.3](https://github.com/maschine34675/ArenaMapPort/releases/tag/v0.2.3) | `CameraRepairHooks.AfterSetCamera` reapplies camera repairs; `NativeMultiFlare.UpdateProtection` unsubscribes/rebinds the physical player; `DonorCameraSubscriptions` handles camera changes. `Player.Init` environment repairs run for replacements too. No new Core fix found from source; map-package binary and native visual acceptance remain unverified here. |
| ArenaMode | [0.1.3](https://github.com/maschine34675/ArenaMode/releases/tag/v0.1.3) | Direct Core driver (`Respawn/ArenaRespawnDriver`, registered in Plugin), already owns live/dead replacements. Coordinate its migrated release with Core 1.0.9 as above. |
| AutoCorpseSearch | [2.2.0](https://github.com/maschine34675/AutoCorpseSearch/releases/tag/v2.2.0) | `Plugin.JobValid` requires the job's SearchController to equal current MainPlayer.SearchController before waiting/searching. Old-body jobs are rejected. Already protected in the shipped DLL. |
| AutoIFF | [2.1.1](https://github.com/maschine34675/AutoIFF/releases/tag/v2.1.1) | `LocalPlayerWatcher.Update` compares physical MainPlayer references and recreates the identifier manager. Manager also refuses a dead/non-current owner. Already protected in the shipped DLL. |
| CombatLog | [1.0.0](https://github.com/maschine34675/CombatLog/releases/tag/v1.0.0) | Current-hands shot ownership; conditional early Stop finalization described above. Consumer-side hardening recommended, no new Core adapter. |
| CraftQueue | [2.4.2](https://github.com/maschine34675/CraftQueue/releases/tag/v2.4.2) | Client `process/tryGetExecutorHideout/isSameExecutor` rejects raid work and rechecks executor identity after awaits. Server `LocalRaidStartPatch/LocalRaidEndPatch` tracks actual raid endpoints, not bodies. No new Core fix found; queue across respawn/menu return deserves a native check. |
| DefaultAutoFireMode | [2.0.0](https://github.com/maschine34675/DefaultAutoFireMode/releases/tag/v2.0.0) | Uses the firearm of the actual draw/controller invocation, without a first-player cache. No new Core fix found. |
| DualSideDoorBreach | [2.0.2](https://github.com/maschine34675/DualSideDoorBreach/releases/tag/v2.0.2) | Movement patches use current call arguments. Brief `PendingKeyConsume` context is the optional consumer hardening described above. DoorDash's separate body attachment already has a Core helper. |
| EasyMounting | [2.0.1](https://github.com/maschine34675/EasyMounting/releases/tag/v2.0.1) | `LocalPlayerCheck` reads the supplied MovementContext; constructor/instance patches affect each new mounting component. Server changes global settings. No new Core fix found. |
| Killcam | [1.0.1](https://github.com/maschine34675/Killcam/releases/tag/v1.0.1) | Ships API-v1 `LocalBodyTracker`, physical-current-body guards and replay/respawn handoff. Historical replay records are intentional. No new Core fix found. |
| LinkedSearchInStash | [2.0.1](https://github.com/maschine34675/LinkedSearchInStash/releases/tag/v.2.0.1) | `AddStashSearchButtonPatch` excludes non-hideout worlds, captures a template ID and resolves current menu context. No raid-body cache or new Core fix found. |
| LowAmmoCue | [1.0.0](https://github.com/maschine34675/LowAmmoCue/releases/tag/v1.0.0) | Plugin/ShotObserver verify physical current MainPlayer and HandsController; body changes dispose prior audio output. Already protected in the shipped DLL; public tag alone lacks implementation source. |
| ModProfiler | [1.4.0](https://github.com/maschine34675/ModProfiler/releases/tag/v1.4.0) | Method instrumentation and world-level sampling; retaining a raid measurement across bodies is intentional. No new Core fix found. |
| MoreQuickSlots | [2.0.0](https://github.com/maschine34675/MoreQuickSlots/releases/tag/v2.0.0) | Hotkey handler reads current MainPlayer/FastAccess; native `QuickAccessPanel.Show` rebinds added slots when Core starts the new owner. No new Core fix found; test a held key/open selector during death. |
| NotificationFilter | [2.0.0](https://github.com/maschine34675/NotificationFilter/releases/tag/v2.0.0) | Reads current game/UI state and supplied notification/quest arguments; no retained raid owner. No new Core fix found. |
| QuestMarkers | [1.4.0](https://github.com/maschine34675/QuestMarkers/releases/tag/v1.4.0) | `QuestTargetScanner` reacquires current player, quest controller and camera. Death hides markers; polling restores them. No new Core fix found. |
| RaidMenuCleanupFix | [2.0.0](https://github.com/maschine34675/RaidMenuCleanupFix/releases/tag/v2.0.0) | `SkipRaidMenuCleanupPatch` checks the current game's InRaid flag per call. A respawn stays within that raid. No new Core fix found. |
| RaidReviewOverlay | [1.0.2](https://github.com/maschine34675/RaidReviewOverlay/releases/tag/v1.0.2) | Browser/menu bridge without recording or body ownership. No new Core fix found; this does not certify the separate Raid Review recorder. |
| ReceiveAllChats | [2.0.1](https://github.com/maschine34675/ReceiveAllChats/releases/tag/v2.0.1) | Menu session/chat attachment handling; re-fetches chat UI after async flush. Server reads session dialogues. No new Core fix found. |
| Redeploy | [1.0.0](https://github.com/maschine34675/Redeploy/releases/tag/v1.0.0) | Client/server 1.0.0 with Core 1.0.8. Core recreates owner, camera, effects and UI, then publishes API-v1 replacement. Development Core 1.0.9 has the existing general third-party migrations; no further owned-mod adapter established. |
| ScopeRangefinder | [3.5.0](https://github.com/maschine34675/ScopeRangefinder/releases/tag/v3.5.0) | Current player/hands/optic/camera each update; death/non-ADS resets/hides state. No new Core fix found; native scoped-state continuity should be checked. |
| StashFilter | [1.0.0](https://github.com/maschine34675/StashFilter/releases/tag/v1.0.0) | Window state resets on Show; template caches are not player caches; armor attribute closures belong to each newly constructed item. No new Core fix found. Separate unpublished patch-ID hardening noted below. |
| TraderSearch | [2.1.0](https://github.com/maschine34675/TraderSearch/releases/tag/v2.1.0) | `AddOfferSearchController` replaces menu inventory context on Show and unsubscribes the previous controller before binding another. No new Core fix found. |
| UnloadAllMagazines | [2.2.1](https://github.com/maschine34675/UnloadAllMagazines/releases/tag/v2.2.1) | Async batch captures the menu controller; native sorting UI does not expose this path in ordinary raid inventory. No normal Redeploy path established. A third mod enabling raid sorting would need separate cancellation/owner validation. |
| WeaponBuilderSearch | [2.0.0](https://github.com/maschine34675/WeaponBuilderSearch/releases/tag/v2.0.0) | Rebuilds dropdown ItemViews on Show, clears on Close; no active raid-body ownership. No new Core fix found. |
| WebOverlay | [1.11.0](https://github.com/maschine34675/WebOverlay/releases/tag/v1.11.0) | Browser/window and global cursor bridge; resolves current UI controller rather than retaining a PlayerOwner. No new Core fix found; overlay focus during respawn remains a native UI check. |

## Public beta downloads

These are published through the [beta hub](https://github.com/maschine34675/spt-beta-hub), not additional Forge release repositories. ZIP names/build labels refer to the downloaded snapshot.

| Mod | Downloaded build | Result |
| --- | --- | --- |
| BangAndClear | 0.9.2+9882bbc | Explicitly an **SPT 4.0** package. Accounted for, but excluded from compatibility acceptance with this SPT 4.1 Redeploy. A 4.1 port would need its own scripted throw/door/cancellation review. |
| BtrTurretControl | 1.0.0+37e08ef | `BtrTurretRuntime` reads current MainPlayer; controller exits turret mode when that player is outside the BTR. Camera exit removes its pre-cull callback; audio exit resolves the current camera. No lasting old-body attachment established. The old body's death before replacement may leave control active until BtrState changes: test death while operating, respawn prompt input, then camera/audio/control after replacement. No Core adapter justified yet. |
| ClusterGrenade | 2.6.0+c0f3830 | Explosion patches derive subgrenades from the exploding projectile, carrying its profile attribution through GrenadeFactory. No active Player/Owner cache found in this path. Delayed explosives surviving death are expected, not proof of stale-body control. No new Core fix found; attribution after respawn remains a native test. |
| SurroundAudio | 1.0.0+f693bc7 | Global speaker setup and hooks on source creation/spatialization, including each Player's new gear/step/speech sources. Core already rebinds the audio listener/current protagonist. No new Core fix found; physical surround-channel output cannot be certified statically. |

## Separate release-maintenance observations

- **StashFilter:** local `35cf488` renamed Harmony patch classes to avoid owner-ID collisions, including the shared `StashSearchFilterPatch` name with LinkedSearchInStash. The public 1.0.0 DLL still uses the generic name. The existing local fix should be included in a future StashFilter release. Neither mod currently calling Disable was established as a fault here; this is independent defensive maintenance, not a Redeploy defect.
- **Source provenance:** examples of release-tag/source drift include AutoCorpseSearch, AutoIFF, Airburst, UnloadAllMagazines, CraftQueue, RaidMenuCleanupFix and TraderSearch. Several other DLLs embed build SHAs distinct from their curated public tag. Future releases should identify the matching source snapshot; do not silently rewrite old public tags as part of this audit.
- The old September note that AutoIFF's respawn handling was unreleased is superseded by the actual public 2.1.1 DLL. It must not be used to justify another adapter today.

## Focused validation after any implementation

1. CombatLog + Killcam + standalone Redeploy: damage/kill before and after two respawns, then a real exit; expect one complete report. Separately issue a Stop that Core actually vetoes while a respawn choice is pending, to exercise the identified edge.
2. DualSideDoorBreach: interrupt a keyed-door kick at death/replacement and retry that same door; inspect actual key uses. MoreQuickSlots: die with an extra-slot selector/key held, then bind/use an item on the successor.
3. Combined smoke pass: active AutoCorpseSearch queue at death, AutoIFF classification, LowAmmoCue, QuestMarkers, scoped rangefinder and default fire mode after revival. Opening/closing WebOverlay must restore normal cursor control.
4. CraftQueue: leave queued work running through two respawns, exit normally, check stash/materials and resumed queue; reopen menu searches and chats. A body replacement must not be treated as a new server raid.
5. Beta-only follow-ups, if those packages remain supported: BTR death/control release, surround output, and delayed ClusterGrenade attribution.

These are outstanding validation recommendations, not tests performed by this audit. The documentation work was checked for inventory coverage and diff cleanliness; no runtime unit suite or GPU run was required or claimed. Three independent eight-mod reviews plus parent review covered the 27 repositories and four beta entries. All nine audit review gates were satisfied; none were abandoned.

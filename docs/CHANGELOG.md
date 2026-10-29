# Changelog

Notable changes since **v1.0.0**, reconstructed from Git history and published
release notes. Redeploy Client/Server and Redeploy.Core have separate versions:
1.1.1 bundles Core 1.0.18; 1.1.0 bundled Core 1.0.17, 1.0.2 bundled Core 1.0.13,
1.0.1 bundled Core 1.0.10, and 1.0.0 bundled Core 1.0.8.

## [1.1.1] - 2026-10-29

Client/Server **1.1.1**, bundled and minimum Core **1.0.18**. Hotfix for
[v1.1.0](https://github.com/maschine34675/Redeploy/releases/tag/v1.1.0). The
public `RespawnApi` contract remains version 1.

### Fixed

- **Recoil Rework Legacy and Fontaine FOV Fix with Continue as Scav.** Core
  handed these two mods over to the replacement body only when its profile was
  already the game's current profile. The own Scav is built with its own profile,
  so its body was skipped. By the decompiled mod, Recoil Rework would then have
  destroyed the new player object, ending the raid after the respawn item was
  spent; FOV Fix would have applied its callback to the old body. Core now uses
  the profile the running replacement expects, so the Scav body is handed over
  like an ordinary respawn. A source check fails the build's tests if an adapter
  reintroduces the old comparison.
- **Raids reached by map transit.** The Scav button was enabled although the
  server never registers such a raid for a continuation, and choosing it ended
  the raid. The button is now disabled with the reason "Scav continuation is
  unavailable in a raid you reached by map transit." The PMC choices work as usual.
- **A refused Scav preparation no longer ends the raid.** Before anything changes,
  the client now asks the server whether a continuation is possible (including
  whether your Scav shares items with the PMC). If the server refuses, does not
  answer or times out, or the respawn-cost item is gone, the death prompt returns
  with the PMC choices and the Scav choice disabled with a reason. Once the
  continuation is reserved, failures stay fail-closed as before. A server restart
  in the few frames between this check and the preparation still ends the raid.
  If the raid is already ending when the check fails, Redeploy leaves its cleanup
  to the raid end instead of releasing the respawn guard early.
- Server refusals are shown as readable text; the raw code stays in the log. An
  unexpected error while the check starts shows a fixed sentence and logs the
  exception.

### Documentation

- The Support section explains that the `Respawn position` details are written
  at Debug and how to enable Debug logging in `BepInEx.cfg`.
- Historical own-Scav review and recovery reports are marked as such, and a
  link into the ArenaMode repository now points to its published v0.4.0 file.
- The own-Scav guide states which parts of the 1.1.1 refusal path are verified
  offline only, and that the client's transit check is a best-effort early
  disable behind the server's authoritative refusal.

## [1.1.0] - 2026-10-29

Client/Server **1.1.0**, bundled and minimum Core **1.0.17**. Changes since
[v1.0.2](https://github.com/maschine34675/Redeploy/releases/tag/v1.0.2).
The public `RespawnApi` contract remains version 1.

### Continue as your own Scav

- After PMC death, choose the usual naked PMC respawn or one life as your
  regular Player-Scav, with its current equipment and normal cooldown.
  `RespawnAsScav` is enabled by default; saved settings remain unchanged.
- The PMC corpse stays lootable and the PMC outcome remains a death. Scav
  extraction opens the native Scav-to-stash transfer screen; Scav progression,
  regeneration and cooldown follow the native outcome. Recovered insured items
  are removed from the original claim. A second death ends the raid.
- Recover an interrupted continuation from the main menu: abandon an unfinished
  Scav life, complete a provable recorded save or resume the remaining loot
  transfer. Items already moved to the stash remain there. Recovery requires a
  game restart before another raid; ambiguous partial saves remain blocked.
- Missing Scav exits, cooldown and prior BTR/transit item deliveries disable
  only the Scav choice. Both respawn choices respect the configured limit and
  item cost. Fresh PMC raids with map-transition options can reserve the Scav;
  arriving from another map remains unsupported.
- BTR item delivery is unavailable after switching, and Sell All is unavailable
  in this continuation's loot transfer. Move wanted items to the stash and
  finish the transfer. Ordinary PMC/Scav delivery and ordinary Scav Sell All
  keep their native behavior.
- Preserve the extraction countdown across body replacement. Replacement death
  callbacks now follow native ordering, including when bot rebinding fails.
- Core accepts a separate replacement profile for the Scav flow; existing
  same-profile consumers retain their behavior.

### Optional integrations in shared Core

All integrations below work without ArenaMode and preserve the other mod's
settings. Each mod is installed separately.

- **ImpactCue 2.0.3+** and **Enemy Markers 1.4.0+**: retain combat history and bot
  markers while following the replacement body and camera.
- **Manimal's Quick Cancel 2.0.0+**: retire medical work belonging to the old body
  and keep late callbacks from affecting its successor.
- **Picture in Picture Disabler 2.1.0+**: retire the old body's optic state and
  generated mesh caches while preserving unrelated optics and settings.
- **Amand's Graphics 1.8.0+**: follow the replacement camera and retain ON/OFF and
  scene settings. Its initial Labyrinth scene lookup remains unsupported.
- **AI Limit 1.9.2+**: measure bot distance from the replacement player.
- **Healing Autocancel, Forge 5.0.1 / plugin 1.1.0+**: move healing cancellation
  to the new health controller and isolate late events from the old body.
- **Quest Tracker 1.7.0+** and **Audio Accessibility Indicators 2.1.0+**: retain
  the HUD and tracking preferences while refreshing body and compass references,
  including recovery from a failed living-body replacement.

### Settings, logging and verification

- Readable F12 labels and units, ordered settings and advanced diagnostics.
  Routine respawn details now use Debug; safety decisions and real failures
  remain visible. Detailed pipeline timing remains disabled by default.
- Expanded automated lifecycle, recovery, inventory and integration checks.
  Dated native checks cover ordinary respawn, corpse-weapon recovery and firing,
  give-up, own-Scav extraction, partial transfer and interrupted-flow recovery.
- Those native checks used the recorded pre-1.1.0 binaries. They do not certify
  fresh release hashes, every map/optic, optional-mod combination or timing race.
  Fika remains unsupported; future optional-mod versions need matching APIs.

See the [release notes](RELEASE-1.1.0.md), [native release check](RELEASE-CHECK-20261029.md),
[own-Scav recovery](PMC-SCAV-RECOVERY-20261028.md) and
[standalone integration checks](STANDALONE-COMPAT-20261029.md) for exact scope.

## [1.0.2]

Client/Server version **1.0.2**, bundled and minimum Core version **1.0.13**.
The `RespawnApi` version 1 contract remains unchanged. Update all three DLLs
together; a newer compatible Core already supplied by ArenaMode can be retained.

### Radar and crosshair — Core 1.0.13

- Accurate Circular Radar 1.3.4+: preserve its HUD and tracker subscriptions
  through body/camera replacement, update player/camera references and protect
  the owned world from its missing-HUD destruction fallback.
- Simple Crosshair 1.4.0+: preserve the current overlay, detach exact old-body
  callbacks and prevent a retired body's unregister from destroying the new HUD.
- Shared optional integrations for standalone Redeploy and ArenaMode; foreign
  settings and ordinary unowned raids remain unchanged. Radar colors retain
  their faction/role meaning rather than indicating team hostility.

See [implementation and verification](HUD-COMPAT-20261025.md).

### Magazine panel — Core 1.0.12

- Magazine Check Interrupt 1.1.1+: retire the old body's floating magazine panel
  before the replacement uses its new camera. Fence delayed display callbacks
  by physical owner and display generation; complete owned callbacks on the
  game thread. No changes to the mod's settings or ordinary raid bodies.
- Expected pooled-panel teardown diagnostics now use Debug; ambiguous live
  ownership still produces a warning.

See [implementation and verification](MAGCHECK-COMPAT-20261025.md).

### FOV and loading animations — Core 1.0.11

- Fontaine FOV Fix 4.1.0+: refresh camera/weapon ownership after replacement and
  fence callbacks from the retired body.
- Manimal Ammo Loading Animations 2.0.0+: retire per-body loading sessions and
  prevent delayed animation work from returning to an old character.
- Install Manimal's custom animation iterator hooks before their callers so
  the replacement body's own loading animation is retained.

See [implementation and verification](FOV-MANIMAL-COMPAT-20261025.md).

### Verification boundaries

- Added offline lifecycle/ownership tests for all five optional integrations,
  plus coordinator composition and mutation checks.
- The final compatibility candidates have bounded native coverage through
  ArenaMode's shared Core: FOV during ADS, detached-magazine animation across
  deaths, magazine checks on successive bodies, radar/crosshair across living
  kit replacement and death respawns, and cleanup when leaving a raid.
- Those final candidates were not separately rerun in standalone Redeploy.
  All optics, PitFireTeam, delayed-callback races, nonpersistent practice mode
  and long-session behavior were not established by the recorded native runs.
  Build identities and exact scenarios are in the three dated reports above.
- Newer optional-mod versions are admitted only with the expected structure and
  produce an unverified-version warning. These minimum versions are not a
  promise that every later release is compatible. Fika remains unsupported.

## [1.0.1] - 2026-10-24

### Shared body-replacement compatibility — Core 1.0.10

- Moved general body-replacement fixes for **Recoil Rework Legacy 2.0.0+,
  Continuous Load Ammo 1.2.0+ and Continuous Healing 1.6.3+** from ArenaMode
  into Redeploy.Core. Standalone Redeploy now uses the same integrations without
  requiring ArenaMode. All three mods remain optional.
- **Recoil Rework Legacy:** transfer recoil/camera component ownership to the
  replacement character, refresh its equipped-weapon cache and prevent retired
  components from interfering with the new body.
- **Continuous Load Ammo:** rebind the loading controller after replacement and
  isolate old disposal, movement restrictions and delayed completion from the
  new character's inventory and loading operation.
- **Continuous Healing:** prevent callbacks from the retired body from starting
  or cancelling healing on its successor, while retaining the mod's behavior
  on the current living player. Unrelated ordinary bots remain unchanged.
- Validate optional-mod compatibility before tearing down the old player.
  Standalone Redeploy shows a raid-start notice if respawning is unavailable;
  failed admission leaves normal death handling in place. Newer mod versions
  still need the expected APIs and produce an unverified-version warning.
- Reject conflicting older ArenaMode adapters before installing duplicate hooks.
  Update ArenaMode and its matching Core together when using these integrations.
- Raised this release's minimum Core requirement to **1.0.10**. The existing
  `RespawnApi` version 1 observer contract and event sequence remain unchanged.

### Verification

- Added shared lifecycle and optional-mod regression checks, including failed
  preparation/recovery and delayed callbacks from retired bodies.
- Recorded bounded native tests for standalone respawns, corpse-weapon recovery,
  shooting, continuous healing and magazine unloading, plus Arena Deathmatch
  replacements and solo Zombie wave refills. These do not establish every
  asynchronous race, PitFireTeam behavior, or future mod-version compatibility.

See [shared compatibility and verification boundaries](BODY-REPLACEMENT-COMPAT-20261023.md)
for the tested builds, scenarios and remaining limits.

## [1.0.0] - 2026-10-19

Baseline for this changelog: the `v1.0.0` tag at
[`d1d7240`](https://github.com/maschine34675/Redeploy/commit/d1d7240),
with **Client/Server 1.0.0 and Core 1.0.8**. Its archive was refreshed on
2026-10-19 without changing the Client/Server version; earlier same-version
archives are not reconstructed here.

The respawn choices, corpse-gear recovery, retained equipment, protection/cost
settings and existing Killcam, DoorDash, DynamicMaps and Freecam integrations
already belong to this baseline. See its
[README](https://github.com/maschine34675/Redeploy/blob/v1.0.0/README.md)
for the features and limitations at release.

[1.1.0]: https://github.com/maschine34675/Redeploy/releases/tag/v1.1.0
[1.0.2]: https://github.com/maschine34675/Redeploy/releases/tag/v1.0.2
[1.0.1]: https://github.com/maschine34675/Redeploy/releases/tag/v1.0.1
[1.0.0]: https://github.com/maschine34675/Redeploy/releases/tag/v1.0.0

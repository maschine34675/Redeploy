# Changelog

Notable changes since **v1.0.0**, reconstructed from Git history and published
release notes. Release dates are GitHub publication dates.

Redeploy Client/Server and Redeploy.Core have **separate version numbers**.
Release 1.0.2 contains Client/Server 1.0.2 and Core 1.0.13. Release 1.0.1
contains Core 1.0.10; release 1.0.0 contains Core 1.0.8.

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

[1.0.2]: https://github.com/maschine34675/Redeploy/releases/tag/v1.0.2
[1.0.1]: https://github.com/maschine34675/Redeploy/releases/tag/v1.0.1
[1.0.0]: https://github.com/maschine34675/Redeploy/releases/tag/v1.0.0

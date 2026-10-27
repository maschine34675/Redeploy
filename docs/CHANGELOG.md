# Changelog

Notable changes since **v1.0.0**, reconstructed from Git history and published
release notes. Release dates are GitHub publication dates.

Redeploy Client/Server and Redeploy.Core have **separate version numbers**.
Release 1.0.2 contains Client/Server 1.0.2 and Core 1.0.13. Release 1.0.1
contains Core 1.0.10; release 1.0.0 contains Core 1.0.8.

## Experimental branch — PMC to Scav, Core 1.0.16

- Optional `RespawnAsScav`: after a PMC death, continue once in the same raid as a
  fresh Scav. The ordinary Player-Scav and its cooldown are not consumed.
- The PMC corpse stays lootable. The PMC outcome remains a death; successful
  Scav extraction sends carried loot through a system message and Receive items.
- Recovered insured items are excluded from the original insurance claim. A
  second death ends the raid; it does not offer another respawn.
- Shared Core accepts an explicitly separate replacement profile. Existing
  same-profile consumers retain their behavior and RespawnApi version 1.
- The extraction timer now preserves remaining time across ordinary respawns
  as well as the Scav switch.
- Replacement death callbacks follow native ordering, allowing PitFireTeam to
  handle the death before raid teardown disposes its registry. The death handler
  is also retained when bot rebind fails.
- This branch is not a release. See [behavior, evidence and limits](PMC-TO-SCAV.md).

## Unreleased — Core 1.0.14–1.0.15

- ImpactCue 2.0.3+: bind telemetry and weapon/health caches to the confirmed
  replacement body without clearing the raid's combat history. Late callbacks
  from a retired body cannot take the HUD back.
- Enemy Markers 1.4.0+: preserve existing bot markers, bind their billboards to
  the replacement camera, and exclude the local replacement from new markers.
- These Core 1.0.14 integrations passed three native respawns and raid exit per
  driver, including the Enemy Markers destroyed-body cleanup correction. See
  [HUD compatibility evidence](ITEMS-HUD-COMPAT-20261025.md).
- Quick Cancel 2.0.0+: Core 1.0.15 retires only the old body's pending medical
  controllers and rejects delayed callbacks for that body. Actual cancellation
  and subsequent firearm use passed in Arena; the rare pending-drop/death race
  is covered by offline production-boundary checks, not the native timing test.
- Picture in Picture Disabler 2.1.0+: Core 1.0.15 limits optic callbacks and
  generated mesh-cache retirement to proven physical owners. Preserve the exact
  held optic's provenance when native death detaches the weapon before preparation.
  Two actual scoped Arena deaths and new-scope rendering passed with FOV Fix 4.1.0.
- Amand's Graphics 1.8.0+: Core 1.0.15 binds the replacement camera while
  retaining the ON/OFF preference and original scene/light defaults. Factory
  ON/OFF respawns passed. Labyrinth still fails in the mod's initial scene lookup
  before Core replacement; an incomplete baseline is refused rather than reset.
  This is a bounded camera repair, not a fix for that independent map blocker.
- These integrations belong to Core and apply to standalone Redeploy and Arena.
  Ordinary raids without a Core replacement retain the foreign mods' behavior.
  The public RespawnApi version remains 1.
- The same final Core 1.0.15 passed two Factory respawns with Arena absent:
  scoped M700 death and old PiP-cache retirement, current Amand/HUD bindings,
  plus native medical cancellation and an RGD throw after respawn. This does not
  establish every optic, HUD feature, map or the rare pending-drop/death race. See
  [remaining body compatibility](REMAINING-BODY-COMPAT-20261026.md) for native
  scenarios, offline checks and limits. Client/Server stay at 1.0.2; the published
  release still contains Core 1.0.13 and is not changed by this development work.

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

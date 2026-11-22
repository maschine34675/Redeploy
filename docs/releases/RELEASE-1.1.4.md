# Redeploy 1.1.4

For **SPT 4.1.x**. Includes Client/Server **1.1.4** and shared **Core 1.0.28**;
no additional mod is required. The Client and Server code is unchanged since
1.1.1: this release carries the new Core and the drivers' Core 1.0.28 stamp.
Core 1.0.25, 1.0.26 and 1.0.27 were never released on their own; this is the
first release that carries their changes.

## Changed

- **Fontaine FOV Fix 5.0.1 and Realism CommonLib 0.1.0 (Core 1.0.28).** The fresh
  body keeps its nine native-created CommonLib controllers; the outgoing body's
  controllers are disabled and detached, and late reload, firing and gear
  callbacks are fenced. A failed creation restores the outgoing globals and their
  current enabled state, and partial foreign initialization is cleaned up. FOV's
  minimum stays 4.1.0 while its reviewed baseline rises to 5.0.1.
- **Tarkova-Cola 4.0.0 (Core 1.0.28).** At a confirmed physical replacement the
  outgoing body's downed, hands, reload and transient perk state are retired.
  Active perks, raid progress, once-per-raid allowances and the physical
  inventory are kept, and late callbacks of the retired body cannot drive the
  successor.
- **WTT Armory 2.0.5 (Core 1.0.27)** follows the replacement player and resets the
  outgoing AN-94 Hyperburst state; retained old-body callbacks cannot overwrite the
  current player's burst state.
- **DynamicMaps 1.3.0 and Advanced Movement 1.0.1 (Core 1.0.26).** DynamicMaps
  refreshes its active quest and key subscriptions after a replacement, including
  a continuously visible minimap, and keeps hidden providers and the legacy arrow
  behavior. Advanced Movement's outgoing Slide/Dive, gesture and audio state is
  retired without clearing the successor's state.
- **VisualAssist 1.2.0 (Core 1.0.25)** keeps its grenade preview after a confirmed
  replacement. Core disposes the retired preview's own line, sphere and materials
  and prevents late old-body callbacks from clearing the successor; grenade
  physics are unchanged.
- These repairs live in shared Core and serve standalone Redeploy as well as
  ArenaMode. There is no API-v1 or engine-order change, no new frame polling,
  scene scan or absent-plugin Harmony sweep. `RespawnApi` is unchanged (version 1)
  and the interop file stays at revision 1.
- Drivers built from this source (Redeploy.Client 1.1.4, ArenaMode 0.7.0) stamp
  and require Core 1.0.28 and refuse an older Core at startup.

## Installation and updating

Finish any raid and pending Scav recovery or transfer. Close the game and server,
back up your profile, and extract `Redeploy-1.1.4.zip` into the SPT installation.
Update all three files, then restart both:

- `BepInEx/plugins/maschine-Redeploy.Client.dll`
- `BepInEx/plugins/maschine-Redeploy.Core.dll`
- `SPT_Runtime/user/mods/Redeploy/maschine-Redeploy.Server.dll`

Keep exactly one Core DLL across the plugins folder and its subfolders. Redeploy
1.1.4 requires **Core 1.0.28 or newer** with a compatible API. ArenaMode 0.7.0
ships the identical Core 1.0.28; with ArenaMode 0.6.0 or older, keep the Core
from this package. The `RespawnApi` contract remains version 1. Existing
settings are kept.

## Limits and validation

The integrations are optional and detected at runtime. The compatibility records
state what was run in the game and what only offline.

Core 1.0.28 (FOV Fix 5.0.1, Realism CommonLib 0.1.0, Tarkova-Cola 4.0.0) was run on
the GPU test host SPT-GPU-2 on 22 November 2026 in standalone Redeploy with
ArenaMode's client absent and in an Arena Deathmatch: a native death with the
PMC-near-corpse respawn, an actual QuickRevive downed state followed by a Core
replacement that retained the living outgoing body, and in Arena an automatic
respawn and a retained-living-body replacement, with ADS, shooting and walking
afterwards. All nine old CommonLib controllers were disabled and the nine current
ones enabled, and QuickRevive's spent revive stayed spent. Not shown in the game:
every optic, every other Cola perk combination, a long raid, own-Scav behavior
with these versions and forced creation or foreign initializer failures; those are
covered by the offline harnesses only. See the
[FOV/CommonLib](https://github.com/maschine34675/Redeploy/blob/v1.1.4/docs/compatibility/FOV-COMMONLIB-20261122.md)
and
[Tarkova-Cola](https://github.com/maschine34675/Redeploy/blob/v1.1.4/docs/compatibility/TARKOVA-COLA-BODY-20261122.md)
records.

WTT Armory 2.0.5 (Core 1.0.27) was run on SPT-GPU-2 on 21 November 2026 in
standalone Redeploy and in Arena, with real deaths and live replacements; only the
AN-94 was fired in the game, and own-Scav, a forced construction failure and
delayed callback faults are covered offline only. DynamicMaps 1.3.0 and Advanced
Movement 1.0.1 (Core 1.0.26) were run on SPT-GPU-2 on 20 November 2026, standalone
and in Arena, with Slide and Dive replacements, a death into the PMC and the
own-Scav choices, active and hidden minimaps and real inventory events; no actual
quest completion was performed, so quest event delivery is covered offline only.
VisualAssist 1.2.0 (Core 1.0.25) was run on SPT-GPU-2 on 20 November 2026 with two
standalone PMC replacements and an Arena replacement, visible previews, OFF/ON,
normal throws and resource cleanup; own-Scav and forced creation failures were not
exercised in the game.

Fika remains unsupported. All other limits of
[1.1.3](https://github.com/maschine34675/Redeploy/blob/v1.1.4/docs/releases/RELEASE-1.1.3.md#limits-and-validation)
still apply. The
[changelog](https://github.com/maschine34675/Redeploy/blob/v1.1.4/docs/CHANGELOG.md#114---2026-11-22)
records the complete changes.

## Native release check

Pending: filled in after packaging and the native run.

## Package checks

Pending: filled in after packaging and the native run.

# Redeploy 1.0.2

For SPT 4.1.x. Contains **Redeploy Client/Server 1.0.2** and the shared
**Redeploy.Core 1.0.13** library. No additional mod is required.

## Changes

Optional compatibility for body replacement, shared with ArenaMode:

- **Fontaine FOV Fix 4.1.0+:** refresh camera/weapon ownership after respawn and
  prevent retired-body callbacks from changing the current camera.
- **Manimal Ammo Loading Animations 2.0.0+:** retire old loading sessions while
  retaining the replacement body's own loading animation.
- **Magazine Check Interrupt 1.1.1+:** retire the old floating magazine panel
  and keep late hide callbacks from affecting the new display.
- **Accurate Circular Radar 1.3.4+:** retain its HUD through camera replacement
  and update player, camera and tracker references. Radar colors keep their
  original faction/role meaning.
- **Simple Crosshair 1.4.0+:** retain the overlay, transfer its player/camera
  callbacks and prevent retired-body cleanup from removing the current HUD.

The mods above remain optional. Newer versions must still have the expected
APIs and log an unverified-version warning. The shared `RespawnApi` contract
remains version 1. Fika is still unsupported.

## Updating

Close the game and server, extract the ZIP into the SPT installation, and
restart both. Update all three files:

- `BepInEx/plugins/maschine-Redeploy.Client.dll`
- `BepInEx/plugins/maschine-Redeploy.Core.dll`
- `SPT_Runtime/user/mods/Redeploy/maschine-Redeploy.Server.dll`

Keep only one Core DLL across the plugins folder and its subfolders. This
release requires Core **1.0.13 or newer**; retain a newer compatible Core if
ArenaMode already supplies one. Existing Redeploy settings are preserved.

## Verification scope

The compatibility changes have offline lifecycle and coordinator checks.
Their final development candidates were tested through ArenaMode's shared Core
with actual death respawns, FOV/ADS, magazine loading/checking, living kit
replacement, HUD reuse and raid exit. Those candidates were not separately
rerun in standalone Redeploy. The tests do not establish every optic, delayed
callback race, PitFireTeam combination, practice-mode setting or long session.

Exact builds and scenarios:
[FOV/Manimal](FOV-MANIMAL-COMPAT-20261025.md),
[Magazine Check Interrupt](MAGCHECK-COMPAT-20261025.md),
[Radar/Crosshair](HUD-COMPAT-20261025.md).

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

## Final package verification

Published on 2026-10-25 as
[Redeploy 1.0.2](https://github.com/maschine34675/Redeploy/releases/tag/v1.0.2),
from commit `1c78fb3694912106bb9e43b02020c3f0e37cdaf3`.
ZIP SHA-256: `2d06882d950a4cc575ca9411062c9f06eebb7c1d876e9ae5e6dcac1421a7198e`.

The isolated committed-source build contains exactly three DLLs. Assembly,
Windows file/product, plugin/server and minimum-Core versions were independently
verified. Core SHA-256 is
`b6d226bfe61c7ff4333f7c2c90c35680cc1e5d413a962a104b22dd8ee4f6d9c5`,
identical to ArenaMode 0.3.0. Old local Client/Server files were still 1.0.0;
the earlier GitHub 1.0.1 ZIP was correct. The final deployment replaces all three.

All 274 unit tests passed without skips, plus respawn interop, shared coordinator
and compatibility mutation checks. GPU2 loaded these exact bytes for a genuine
ordinary PMC death, native Redeploy dialog, fresh-body respawn at 440 HP and
Gate 3 extraction. Arena was installed but inactive; Redeploy was the Core caller.
This basic final smoke test does not expand the optional-mod matrix described
above. Original test settings were restored and game/server processes stopped.

Gitleaks found no credentials in the full history, frozen source or final payload.
All three DLL hashes have zero malicious/suspicious VirusTotal detections, with
individual report links in the release. The uploaded ZIP was downloaded again
and matched both the local verified package and GitHub's SHA-256 asset digest.

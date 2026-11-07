# Redeploy 1.1.2

For **SPT 4.1.x**. Includes Client/Server **1.1.2** and shared **Core 1.0.20**;
no additional mod is required. The Client and Server code is unchanged since
1.1.1: this release carries the new Core and the drivers' Core 1.0.20 stamp.

## Changed

- **Three new optional integrations in shared Core 1.0.20.** Raid Review 1.10.0
  records each physical life separately (PMC respawns and the own-Scav
  continuation) while the recording owner, session and profile identities stay
  the real ones; earlier deaths and old-body combat events keep their original
  lives, and its existing replay shows the per-life statistics. HollywoodFX 3.0.0
  refreshes its impact and optic player cache after a confirmed replacement
  without reinitializing the world or changing its settings. Hands Are Not Busy
  1.7.1 can no longer reset the hands of a prepared, uncommitted or retired body;
  the replacement body keeps its normal component. All three are detected at
  runtime and need no settings.
- **Absent optional mods no longer cost frames (Core 1.0.19).** Five adapters
  (Quick Cancel, PiP Disabler, Quest Tracker, Audio Accessibility Indicators,
  Healing Autocancel) rescanned every patched method of the game whenever their
  mod was absent and something asked whether it was compatible. ArenaMode's map
  screen asked five times a second, which cost it one long frame every 0.2 s;
  the adapters now unpatch only while their Harmony instance may hold patches.
  ArenaMode measured the screen at 48 to about 60 FPS on its GPU test host.
- Drivers built from this source (Redeploy.Client 1.1.2, ArenaMode 0.5.0) stamp
  and require Core 1.0.20 and refuse an older Core at startup.

## Installation and updating

Finish any raid and pending Scav recovery or transfer. Close the game and server,
back up your profile, and extract `Redeploy-1.1.2.zip` into the SPT installation.
Update all three files, then restart both:

- `BepInEx/plugins/maschine-Redeploy.Client.dll`
- `BepInEx/plugins/maschine-Redeploy.Core.dll`
- `SPT_Runtime/user/mods/Redeploy/maschine-Redeploy.Server.dll`

Keep exactly one Core DLL across the plugins folder and its subfolders. Redeploy
1.1.2 requires **Core 1.0.20 or newer** with a compatible API. ArenaMode 0.5.0
ships the identical Core 1.0.20; with ArenaMode 0.4.1 or older, keep the Core
from this package (it is newer, and Arena accepts a newer Core). The
`RespawnApi` contract remains version 1. Existing settings are kept.

## Limits and validation

The three new adapters are verified against the published builds of Raid Review
1.10.0, HollywoodFX 3.0.0 and Hands Are Not Busy 1.7.1 with offline harnesses
(real Harmony targets, replay and storage checks, mutation controls) and unit
tests; none of the three mods has run together with Core in the game, because
the test hosts do not have them installed. Treat those combinations as untested
gameplay until a native run is recorded. The absent-adapter fix was measured
natively through ArenaMode's map screen on 2026-10-31 (Core `1.0.19+51ac95c`).

Fika remains unsupported. All other limits of
[1.1.1](https://github.com/maschine34675/Redeploy/blob/v1.1.2/docs/RELEASE-1.1.1.md#limits-and-validation)
still apply. The
[changelog](https://github.com/maschine34675/Redeploy/blob/v1.1.2/docs/CHANGELOG.md#112---2026-11-07)
records the complete changes.

## Native release check

Pending: recorded after the GPU cycle with the exact package binaries.

## Package checks

Pending: recorded after packaging (build commit, ZIP SHA-256 and size, fresh
test counts, VirusTotal results of the three DLLs).

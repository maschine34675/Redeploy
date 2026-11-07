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

On the GPU test host SPT-GPU-2 (SPT 4.1.6) with these exact binaries loaded (Client 1.1.2,
Core 1.0.20, Server 1.1.2, built from `3b68193`), a Factory PMC raid with few bots: a death
followed by "PMC near corpse" produced the respawn 14.5 s after the death ("Respawn complete."),
a new body at full health without equipment, the Killcam recorder re-initialised, and no
warning or error from any mod; the raid was left through the result screens. The same Core
carried ArenaMode 0.5.0's own respawns on Iceberg and Bowl and a PitFireTeam Tactical match
over three rounds in the same session. Continue as Scav, transit arrivals, and Raid Review,
HollywoodFX and Hands Are Not Busy (none installed on the host) were not run. Record:
ArenaMode's `docs/reports/2026-11/GPU-RELEASE-050-20261107.md`.

## Package checks

Built from `3b68193d07aa1a6d0d40b9a37843123681124b3a`. ZIP SHA-256:
`e3a4a5111d36b58aec61ded39311ce0d5255ab38b8ef8afb885c0789d6a447d4` (234,823 bytes). The ZIP
holds exactly Client/Server 1.1.2 and Core 1.0.20 (`1.0.20+3b68193`); its Core is
byte-identical to the Core in ArenaMode 0.5.0.

Fresh checks on the release commit without deploying: 601 client/Core and 162 server tests
passed with none skipped, plus the respawn interoperability checks. The package holds only
the three compiled DLLs; no credential scan was run for this release (the source change since
1.1.1 is the Core work of the 2026-11-02 report and version strings).

VirusTotal results below apply to these exact binaries (7 November 2026). None of the three
DLLs had a malicious or suspicious label (71, 71 and 70 engines without detection).

| File | SHA-256 / scan |
|---|---|
| `maschine-Redeploy.Client.dll` | [103e3d73d5f5…](https://www.virustotal.com/gui/file/103e3d73d5f549a2578d69432c9ebb528151cc571dc0f9a87d46089b6bd26b03) |
| `maschine-Redeploy.Core.dll` | [150851f557dc…](https://www.virustotal.com/gui/file/150851f557dc14356de53f75023128b591fb607d2cac99388e91c54148e8c87b) |
| `maschine-Redeploy.Server.dll` | [b90d0176a349…](https://www.virustotal.com/gui/file/b90d0176a349b02fc27624f5b3ddc4cf90e29c5a79e7a403d3b5cefa31eb47f7) |

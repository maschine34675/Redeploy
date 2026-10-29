# Redeploy 1.1.1

For **SPT 4.1.x**. Hotfix for 1.1.0. Includes Client/Server **1.1.1** and shared
**Core 1.0.18**; no additional mod is required.

## Fixed

- **Recoil Rework Legacy and Fontaine FOV Fix with Continue as Scav.** In 1.1.0
  Core did not hand Recoil Rework over to your own Scav, because the Scav body
  carries a different profile than the PMC; this could end the raid right after
  the switch, with the respawn item already spent. For FOV Fix, its early scale
  callback could run against the old body while the Scav body was being built.
  Core 1.0.18 hands both mods over to the Scav body like an ordinary respawn.
- **Raids reached by map transit.** Continue as Scav was offered in the second and
  later raid of a transit chain, and choosing it ended the raid. The button is now
  disabled there with a reason; the PMC choices work as usual.
- **A refused Scav preparation no longer ends the raid.** The client now asks the
  server first whether the continuation is possible. If the server refuses, does
  not answer or times out, the death prompt returns with the PMC choices instead
  of ending the raid. If your respawn item vanished from the corpse meanwhile,
  the prompt returns without respawn choices and you can end the raid. Once the
  dead PMC's session has been ended, failures end the raid as before. Refusals
  are shown as readable text; an unexpected error while the check starts shows a
  fixed sentence, and the details go to the log.
- The Support section of the README explains how to enable the Debug log lines
  that describe respawn positions.

## Installation and updating

Finish any raid and pending Scav recovery or transfer. Close the game and server,
back up your profile, and extract `Redeploy-1.1.1.zip` into the SPT installation.
Update all three files, then restart both:

- `BepInEx/plugins/maschine-Redeploy.Client.dll`
- `BepInEx/plugins/maschine-Redeploy.Core.dll`
- `SPT_Runtime/user/mods/Redeploy/maschine-Redeploy.Server.dll`

Keep exactly one Core DLL across the plugins folder and its subfolders. Redeploy
1.1.1 requires **Core 1.0.18 or newer** with a compatible API. ArenaMode 0.4.1
ships the identical Core 1.0.18; with an older ArenaMode, keep the Core from this
package. The `RespawnApi` contract remains version 1. Existing settings are kept.

## Limits and validation

The Recoil Rework and FOV Fix handover for the Scav switch is verified with the
offline harnesses and tests. The server check, the returned PMC choices after a
refusal and the client's transit detection are verified by unit tests and harness
stubs; the client's transit check is a best-effort early disable, and the server
check is the authoritative refusal. The native release check of the published
binaries is recorded on the
[GitHub release page](https://github.com/maschine34675/Redeploy/releases/tag/v1.1.1).

A server restart in the few frames between the new server check and the Scav
preparation still ends the raid. Fika remains unsupported. All other limits of
[1.1.0](https://github.com/maschine34675/Redeploy/blob/v1.1.1/docs/RELEASE-1.1.0.md#limits-and-validation)
still apply. The
[changelog](https://github.com/maschine34675/Redeploy/blob/v1.1.1/docs/CHANGELOG.md#111---2026-10-29)
records the complete changes.

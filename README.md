# Redeploy

> Formerly **CorpseRun** — renamed in 0.9.x. Remove the old CorpseRun client and server when updating; do not run both names together. See [Updating](#updating).

Dying no longer ends your raid: respawn without gear, fight your way back to your own corpse, and take your equipment back.

## Features

- **Choose what happens when you die.** A prompt offers a respawn near your corpse, a respawn at a regular player spawn, or ending the raid as usual.
- **Your gear stays on your corpse.** The replacement character has no armour, firearms or carried loot, except the retained items below. Recover the rest from your body — if you make it back.
- **What you would normally keep, you keep.** Secure container, melee weapon, armband and pocket special slots move to the replacement character, because corpses never hand those back.
- **Spawn protection.** A configurable invulnerability window starts after respawn. Drawing a firearm, melee weapon or grenade ends it early; the final seconds are announced on screen.
- **Optional respawn cost.** Require a medical item in your carried inventory; one whole matching item is removed when you respawn, not just one charge.
- **Insurance is preserved.** The mod keeps insurance records for gear left on corpses; returns still depend on SPT's normal insurance rules and your server settings.
- **Kills are carried over.** Kills you scored before respawning still appear on the end-of-raid screen.
- **Fails safe.** When the server component is missing, the mod stays inactive and death behaves exactly like vanilla.

## Requirements and compatibility

- **SPT:** 4.1.x. The server component declares `~4.1.0`; the client is built against game build `0.16.9.40743`.
- **Components:** Client, shared Core library, and server. All three are required.
- **Dependencies:** No separately installed mods; the release must include Redeploy.Core.
- **Version lines:** Redeploy Client/Server are currently `1.0.0`; the included Core is `1.0.8`. Core has its own version and is a library, not a separate BepInEx plugin.
- **Optional integrations**, each installed separately and none of them required:
  - **DoorDash** (tarkin) — sprint-ramming doors keeps working with the replacement character.
  - **DynamicMaps** — your position marker follows the replacement character.
  - **Freecam** (Terkoiz) — the free camera keeps working after a respawn.
  - **Killcam** (formerly KillcamReplay) — the death replay stays visible behind the respawn prompt. A chosen respawn waits for the replay to release the camera; Space skips the replay.
- **Fika is deliberately unsupported.** Redeploy disables respawning when `com.fika.core` is loaded and warns at raid start. Its replacement-character flow has no co-op synchronisation.

**With ArenaMode:** Arena uses the same Core but supplies its own respawn rules and server-side handling. Arena alone does not need Redeploy.Client or Redeploy.Server. Install those two components only if you also want Redeploy's death prompt in ordinary raids. The options below configure Redeploy, not Arena's respawns.

## Installation

1. Close the game and SPT server, and back up your profile under `SPT_Runtime/user/profiles/`.
2. Extract the release ZIP into your SPT installation directory.
3. Verify that all three files exist:
   - `BepInEx/plugins/maschine-Redeploy.Client.dll`
   - `BepInEx/plugins/maschine-Redeploy.Core.dll`
   - `SPT_Runtime/user/mods/Redeploy/maschine-Redeploy.Server.dll`
4. Start the SPT server and confirm that `Redeploy` appears among the loaded mods, then start the game. Redeploy's `loaded (Core ...)` startup message in the client log lists both versions.

The client, Core library and server belong to the same installation. The client requires Core to load. Without the server component, Redeploy refuses to respawn and shows a warning at raid start to protect the profile.

## Updating

Close the game and server before replacing files, then restart both. Replace the Client and Server together; keep exactly one copy of `maschine-Redeploy.Core.dll` across `BepInEx/plugins` and its subfolders. If ArenaMode also supplies Core, keep the newer compatible Core rather than overwriting it with an older one. Both clients require Core **1.0.8 or newer** for this build. A missing, unreadable or older Core disables the affected client before settings, hooks or components are initialized, with an error in the client log and SPT dependency list. Install the supplied Core, keep only one copy, and restart the game. Newer Core versions are allowed; this is a minimum-version guard, not certification of future API changes.

Your `BepInEx/config/com.maschine.Redeploy.cfg` is kept. Older CorpseRun settings are not imported automatically; transfer any values you want to keep manually.

If you ran an early internal build, delete these leftovers first — otherwise an obsolete copy of the mod loads alongside the current one:

- `BepInEx/plugins/maschine-Redeploy.dll` (the old name, without `.Client`)
- `SPT/user/mods/Redeploy/` (the SPT 4.0 server path; SPT 4.1 uses `SPT_Runtime/`)
- `maschine-CorpseRun*.dll` anywhere under `BepInEx/plugins/`
- `SPT_Runtime/user/mods/CorpseRun/` and, in an old installation, `SPT/user/mods/CorpseRun/`

The old `BepInEx/config/com.maschine.CorpseRun.cfg` can be archived after copying your settings. Do not install an archived CorpseRun build into SPT 4.1.

## Uninstalling

Finish the raid and close the game and server. Remove `maschine-Redeploy.Client.dll` and `SPT_Runtime/user/mods/Redeploy/`. Remove `maschine-Redeploy.Core.dll` only when no other installed mod, such as ArenaMode, needs it. The Redeploy configuration can be kept for a later reinstall or removed. Uninstalling does not restore equipment or statistics from earlier deaths.

## Usage

There are no hotkeys. When you die, the raid keeps running and a window opens with the mouse cursor released:

- **Respawn near corpse** — searches around your body for a reachable position, checking walls, fences, sniper/mine boundaries and standing room. It tries closer positions, your body's own spot and then a regular player spawn if needed. If every validated option fails, the final fallback is 2 m above the body and is logged as a warning; safe placement cannot be guaranteed on every map.
- **Respawn at spawn point** — uses one of the map's regular player spawns, not necessarily your original spawn. Falls back to the near-corpse search if no player spawn is found.
- **End raid** — the raid ends as killed, exactly as it would without the mod.

After respawning, run back to your body and loot your equipment from it. Redeploy leaves the corpse in the world, but does not protect its loot or prevent another mod from cleaning it up. The raid and its timer keep running while the prompt or replay is open; respawning does not restart the raid.

Spawn protection first puts away the weapon automatically drawn by the game (usually your retained knife). After your hands have cleared, drawing any weapon, including that knife or a grenade, ends protection. Equipping protective clothing alone does not end it.

By default you get one respawn per raid. Once none are left, dying ends the raid normally.

## Configuration

Configure the mod in-game with the F12 BepInEx configuration manager, or close the game and edit `BepInEx/config/com.maschine.Redeploy.cfg`. The file is created on the first successful plugin load. Values below are defaults, not forced replacements for an existing configuration.

| Option | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | Master switch. When off, death behaves like vanilla. |
| `MaxRespawnsPerRaid` | `1` | Respawns allowed per raid; `0` or less means unlimited. |
| `RespawnHealthPercent` | `100` | Starting health for each body part, clamped to 1–100%; old body-part effects are cleared. |
| `RespawnCooldownSeconds` | `5` | Minimum seconds since death; time spent watching a replay counts. `0` or less removes the extra wait, but an active Killcam can still hold the respawn. |
| `CorpseSpawnDistanceMeters` | `20` | Preferred search distance, at least 5 m; half-distance and 3 m rings are also tried. This is not a guaranteed distance. |
| `SpawnProtectionSeconds` | `10` | Invulnerability after respawning; `0` disables it. |
| `RequiredRespawnItem` | `None` | Item that must be carried and is consumed to respawn. |
| `Diagnostics/EnablePipelineTiming` | `false` | Opt-in technical respawn measurements. Reads extra state counters and measures the following frames; leave off during normal play. |

`RequiredRespawnItem` accepts `None`, `Defibrillator`, `Grizzly`, `Salewa`, `IFAK`, `AFAK`, `CMS`, `Surv12`, `CarFirstAidKit`, `Adrenaline` or `Propital`. The check covers carried equipment and its contents, including your secure container, but not the stash. Remaining medical charges are not checked. If the item is absent when you die, only ending the raid remains. If it disappears before consumption or removal fails, the current implementation logs a warning and still respawns; this is a convenience setting, not a strict economy safeguard.

Only the item selection is a fixed dropdown; the other numeric settings do not expose bounded sliders. Use finite, non-negative values unless the table explicitly describes otherwise.

The timing switch applies to Redeploy's own respawns. Other Core consumers choose
independently for each request. Successful camera/marker/freecam setup is logged at
Debug; actual failures and unexpected repairs retain their warnings. A global
BepInEx `LogLevels=All` setting still writes Debug messages.

## Known limitations

- **Fika co-op is not supported.** The mod disables itself when Fika is installed.
- **Progress from earlier lives is only partly carried over.** Kills are restored to the end-of-raid screen, but the game starts a fresh statistics session for the replacement character, so session XP and other counters from before the last respawn are lost. This is intentional and keeps repeated respawns from paying off.
- **Items carried through a normal respawn lose their found-in-raid status** through the game's player-creation path. Do not rely on respawning to preserve FiR loot; transit-raid edge cases have not been validated.
- **Scav raids** work the same way, but have seen less testing than PMC raids.
- The mod changes how your profile is written at the end of a raid. Keep a profile backup while trying it out.
- Integration code and offline checks are not a guarantee for every version of another mod. Dated test notes describe the exact scenarios tested; Fika, forced pipeline failures and some pooled-body edge cases do not have full native coverage.

## For mod authors

Mods that keep a reference to the local player can follow body replacements through
`Redeploy.Core.Api.RespawnApi` (events `DeathClaimed`, `Replaced`, `ReplacementFailed`,
`RaidEnded`; queries `IsAvailable`, `IsReplacing`, `IsRespawnPending`). You do not need to
reference the DLL: copy [interop/RedeployRespawnInterop.cs](interop/RedeployRespawnInterop.cs) into your project, change its
namespace, and add the `LocalBodyTracker` component to your plugin. It binds to Core by
reflection when it is installed and falls back to polling `GameWorld.MainPlayer` otherwise,
so the same code runs with or without Redeploy. The contract is frozen at `ApiVersion` 1;
the reasoning and the guarantees are in [docs/RESPAWN-API.md](docs/RESPAWN-API.md). Core's own version
(`maschine-Redeploy.Core.dll`, 1.0.1 and up) is independent of Redeploy's and only tells fix
levels apart; check `ApiVersion`, not the file version, before using the API.

## Building and checking the source

Use Windows with the .NET 10 SDK and a matching SPT 4.1 installation. Client/Core target `netstandard2.1`; the server and offline checks target `net10.0`. The default checkout layout is `<SPT>/Development/Redeploy`. For another layout, create an untracked `Shared.props.user` in the repository root:

```xml
<Project>
  <PropertyGroup>
    <SPTPath>C:\SPT41</SPTPath>
    <SPTUserPath>C:\SPT41\SPT_Runtime</SPTUserPath>
  </PropertyGroup>
</Project>
```

**Normal builds deploy into that installation.** Close the game and server first. The Client build also builds and deploys Core; the Server build writes to `SPTUserPath/user/mods/Redeploy` when the mods directory exists:

```powershell
dotnet build Redeploy.Client/Redeploy.Client.csproj -c Release
dotnet build Redeploy.Server/Redeploy.Server.csproj -c Release
```

Build outputs are in `Redeploy.Client/bin/Release/`, `Redeploy.Core/bin/Release/` and `build/server/Release/maschine-Redeploy.Server/`. For a Client/Core compile check without deployment, add `-p:SPTModType=Library`; this does **not** disable the separate Server deployment target.

Offline checks do not deploy plugins:

```powershell
dotnet test Redeploy.Tests/Redeploy.Tests.csproj -c Release
dotnet run --project tools/RespawnInteropChecks -c Release
```

Some contract checks inspect previously built DLLs and installed consumers; missing files can produce skipped checks, and stale builds can cause version mismatches. Passing these checks does not exercise Unity or prove a raid works. The [documentation index](docs/README.md) links the API contract, native probe and dated test results. The development probe is not a release component.

## Support

Report problems at <https://github.com/maschine34675/Redeploy-dev/issues> if you have access to the current private development repository; otherwise use the channel where the build was provided.

Please include the exact Redeploy and SPT versions, what you expected and what happened instead, short reproduction steps, and for the affected raid both `BepInEx/LogOutput.log` and the server console output. For a respawn in an odd place, the lines starting with `Respawn position` in `LogOutput.log` name the map, the corpse and the chosen position, and why nearby spots were rejected. Also list the other mods you have installed, especially any that affect the camera, the player character or the end-of-raid screen.

For a missing-server warning, verify the `SPT_Runtime/user/mods/Redeploy/` path above and restart both server and game; the availability result is cached for the game session. Older log text may still mention `SPT/user/mods`. Check logs for credentials, private addresses and other personal data before sharing; do not upload an entire profile unless it is specifically needed and sanitised.

## License and credits

Released under the MIT License; see [LICENSE](LICENSE).

- The spawn protection and the optional respawn cost were inspired by **PlayerLives** and **RevivalMod** (KaiKiNoodles, gitTerebi), which approach the same problem with a downed state instead of a respawn. No code was taken from either mod.
- Redeploy contains compatibility code for **DoorDash** (tarkin), **DynamicMaps** (mpstark), **Freecam** (Terkoiz) and **Killcam**. None of these are bundled; they remain the property of their authors.

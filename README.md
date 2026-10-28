# Redeploy

> Formerly **CorpseRun** — renamed in 0.9.x. Remove the old CorpseRun client and server when updating; do not run both names together. See [Updating](#updating).

Dying no longer ends your raid: respawn without gear, fight your way back to your own corpse, and take your equipment back.

See the [changelog](docs/CHANGELOG.md) for changes since 1.0.0 and the separate
Core version line.

## Features

- **Choose what happens when you die.** Respawn without gear near your corpse or at a regular player spawn, continue as your own Scav when available, or end the raid as usual.
- **Your gear stays on your corpse.** A normal respawn has no armour, firearms or carried loot, except the retained items below. Recover the rest from your body — if you make it back. Choosing your own Scav instead uses that Scav's existing equipment.
- **What you would normally keep, you keep.** During a normal respawn, the secure container, melee weapon, armband and pocket special slots move to the replacement character, because corpses never hand those back. They are not transferred to your own Scav.
- **Spawn protection.** A configurable invulnerability window starts after respawn. Drawing a firearm, melee weapon or grenade ends it early; the final seconds are announced on screen.
- **Optional respawn cost.** Require a medical item in your carried inventory; one whole matching item is removed when you respawn, not just one charge.
- **Insurance is preserved.** The mod keeps insurance records for gear left on corpses; returns still depend on SPT's normal insurance rules and your server settings.
- **Kills are carried over.** Kills you scored before respawning still appear on the end-of-raid screen.
- **Fails safe.** When the server component is missing, the mod stays inactive and death behaves exactly like vanilla.
- **Own-Scav continuation (unreleased):** the optional
  `RespawnAsScav` setting is enabled by default and adds a choice of one life as your regular Player-Scav in the same
  raid after PMC death, provided the normal Scav cooldown has expired. Successful
  extraction opens the usual Scav-to-stash transfer screen. A Scav cooldown only
  disables this choice; the normal PMC respawn remains available. See
  [behavior and test scope](docs/PMC-TO-SCAV.md) and
  [recovery validation](docs/PMC-SCAV-RECOVERY-20261028.md). The own-Scav sections
  below describe current development code; the published 1.0.2 package does not
  contain this feature.

## Requirements and compatibility

- **SPT:** 4.1.x. The server component declares `~4.1.0`; the client is built against game build `0.16.9.40743`.
- **Components:** Client, shared Core library, and server. All three are required.
- **Dependencies:** No separately installed mods; the release must include Redeploy.Core.
- **Version lines:** The published 1.0.2 release contains Client/Server `1.0.2` and Core `1.0.13`. Core has its own version and is a library, not a separate BepInEx plugin.
  Current development uses Core `1.0.16`; its client requires that minimum. The published 1.0.2 package is unchanged.
- **Optional integrations**, each installed separately and none of them required:
  - **Recoil Rework Legacy 2.0.0+** — Core transfers recoil/camera ownership and firearm state to the replacement body.
  - **Continuous Load Ammo 1.2.0+** — Core rebinds continuous loading and fences late disposal/completion from retired bodies.
  - **Continuous Healing 1.6.3+** — old player callbacks cannot continue or cancel the replacement body's healing; current local healing keeps the mod's behavior.
  - **Fontaine FOV Fix 4.1.0+** — refreshes camera and weapon ownership after respawn and fences callbacks from the retired body.
  - **Manimal Ammo Loading Animations 2.0.0+** — retires old loading sessions so their delayed animations cannot continue on a retired character.
  - **Magazine Check Interrupt 1.1.1+** — retires the old body's floating magazine panel; delayed hide callbacks cannot hide its successor's display.
  - **Accurate Circular Radar 1.3.4+** — preserves the radar through camera replacement and rebinds it to the current player. Radar colors retain the mod's faction/role meaning, not team hostility.
  - **Simple Crosshair 1.4.0+** — preserves the overlay and binds its callbacks to the replacement body and camera.
  - **ImpactCue 2.0.3+** (development Core 1.0.14) — rebinds health/weapon telemetry while retaining the raid's combat history.
  - **Enemy Markers 1.4.0+** (development Core 1.0.14) — preserves bot markers, updates their camera and excludes the local replacement body.
  - **Manimal's Quick Cancel 2.0.0+** (development Core 1.0.15) — retires pending medical controllers belonging to the old physical body and fences late callbacks without clearing unrelated controllers.
  - **Picture in Picture Disabler 2.1.0+** (development Core 1.0.15) — retires owned optic state and generated mesh caches across replacement while retaining foreign settings and unrelated optics. Native Arena tests with Fontaine FOV Fix 4.1.0 passed two actual scoped deaths and replacement-scope rendering; standalone testing also passed a scoped death and old-cache retirement.
  - **Amand's Graphics 1.8.0+** (development Core 1.0.15, limited) — binds graphics components to the replacement camera, preserving the ON/OFF choice and original scene/light defaults. Factory ON/OFF respawns passed, but the mod's initial Labyrinth scene activation fails before Core replacement. See the limitation below.
  - **DoorDash** (tarkin) — sprint-ramming doors keeps working with the replacement character.
  - **DynamicMaps** — your position marker follows the replacement character.
  - **Freecam** (Terkoiz) — the free camera keeps working after a respawn.
  - **Killcam** (formerly KillcamReplay) — the death replay stays visible behind the respawn prompt. A chosen respawn waits for the replay to release the camera; Space skips the replay.
- **Fika is deliberately unsupported.** Redeploy disables respawning when `com.fika.core` is loaded and warns at raid start. Its replacement-character flow has no co-op synchronisation.

These body-replacement integrations live in shared Core and do not require
ArenaMode. Newer optional-mod versions require matching APIs and log an
unverified-version warning. An invalid contract refuses respawn before teardown,
with a notice at raid start; ordinary spawning and mod settings remain unchanged.

The Recoil Rework, Continuous Load Ammo and Continuous Healing migration has
bounded native coverage with both standalone Redeploy and ArenaMode. The final
FOV/Manimal, magazine-panel and radar/crosshair fixes were tested natively through
Arena's shared Core path, including real deaths, living kit replacement and raid
exit; their final candidates were not separately rerun in standalone Redeploy.
Offline checks cover their common lifecycle and ownership rules. These tests do
not establish every optic, delayed callback race, practice-mode setting,
PitFireTeam combination or long-session behavior. See the dated reports for
[Recoil/CLA/Healing](docs/BODY-REPLACEMENT-COMPAT-20261023.md),
[FOV/Manimal](docs/FOV-MANIMAL-COMPAT-20261025.md),
[Magazine Check Interrupt](docs/MAGCHECK-COMPAT-20261025.md) and
[Radar/Crosshair](docs/HUD-COMPAT-20261025.md).

ImpactCue and Enemy Markers subsequently passed three respawns and raid exit per
driver with their final Core 1.0.14 correction; see
[HUD body/camera verification](docs/ITEMS-HUD-COMPAT-20261025.md).
The final Core 1.0.15 candidate additionally passed two Factory respawns with Arena
absent. A scoped M700 death retired PiP's old cache; Amand's graphics and HUD
Breakout followed both replacement bodies. Actual medical cancellation and a
Fast Grenade Throw RGD throw worked after respawn. These are bounded tests, not
validation of every HUD feature, optic or map. The rare Quick Cancel interrupted
drop/death race has offline coverage only; a completed native cancel does not
prove that timing window. See [remaining body integrations](docs/REMAINING-BODY-COMPAT-20261026.md)
for exact scenarios, the Amand blocker and outstanding limits. Fast Grenade Throw's
grenade-priority/input correction is Arena-specific and is not added to Core.

**With ArenaMode:** Arena uses the same Core but supplies its own respawn rules and server-side handling. Arena alone does not need Redeploy.Client or Redeploy.Server. Install those two components only if you also want Redeploy's death prompt in ordinary raids. The options below configure Redeploy, not Arena's respawns.

**Older ArenaMode builds:** ArenaMode 0.1.3 still contains the older Arena-owned Recoil Rework, Continuous Load Ammo and Continuous Healing adapters. If it is loaded together with any of those three mods, Core refuses replacement to prevent duplicate hooks. Use ArenaMode 0.2.0 or newer, which delegates these adapters to Core. Standalone Redeploy does not require ArenaMode.

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

Close the game and server before replacing files, then restart both. Update all three files from the release together; keep exactly one copy of `maschine-Redeploy.Core.dll` across `BepInEx/plugins` and its subfolders. If ArenaMode also supplies Core, keep the newer compatible Core rather than overwriting it with an older one. The published Redeploy 1.0.2 release requires Core **1.0.13 or newer**; this experimental `feature/pmc-to-scav` client still reports 1.0.2 but requires Core **1.0.16 or newer**. A missing, unreadable or older Core disables the client before settings, hooks or components are initialized, with an error in the client log and SPT dependency list. Install the supplied Core, keep only one copy, and restart the game. Newer Core versions are allowed; this is a minimum-version guard, not certification of future API changes.

Your `BepInEx/config/com.maschine.Redeploy.cfg` is kept. Older CorpseRun settings are not imported automatically; transfer any values you want to keep manually.

If you ran an early internal build, delete these leftovers first — otherwise an obsolete copy of the mod loads alongside the current one:

- `BepInEx/plugins/maschine-Redeploy.dll` (the old name, without `.Client`)
- `SPT/user/mods/Redeploy/` (the SPT 4.0 server path; SPT 4.1 uses `SPT_Runtime/`)
- `maschine-CorpseRun*.dll` anywhere under `BepInEx/plugins/`
- `SPT_Runtime/user/mods/CorpseRun/` and, in an old installation, `SPT/user/mods/CorpseRun/`

The old `BepInEx/config/com.maschine.CorpseRun.cfg` can be archived after copying your settings. Do not install an archived CorpseRun build into SPT 4.1.

## Uninstalling

Finish the raid and resolve any interrupted Scav continuation or pending loot transfer before closing the game and server. Keep unresolved profile/journal files for diagnosis; deleting a journal is not a recovery procedure. Remove `maschine-Redeploy.Client.dll` and `SPT_Runtime/user/mods/Redeploy/`. Remove `maschine-Redeploy.Core.dll` only when no other installed mod, such as ArenaMode, needs it. The Redeploy configuration can be kept for a later reinstall or removed. Uninstalling does not restore equipment or statistics from earlier deaths.

## Usage

There are no hotkeys. When you die, the raid keeps running and a window opens with the mouse cursor released:

- **PMC near corpse** — respawns without gear, searching around your body for a reachable position and checking walls, fences, sniper/mine boundaries and standing room. It tries closer positions, your body's own spot and then a regular player spawn if needed. If every validated option fails, the final fallback is 2 m above the body and is logged as a warning; safe placement cannot be guaranteed on every map.
- **PMC at spawn point** — respawns without gear at one of the map's regular player spawns, not necessarily your original spawn. Falls back to the near-corpse search if no player spawn is found.
- **Continue as Scav** — uses your own regular Scav and their equipment at a Scav spawn. This additional choice is enabled by default; its button is disabled while your Scav is unavailable or on cooldown, if Scav exits cannot be verified, or after BTR/transit item delivery in the PMC life. The normal PMC choices remain available. After switching, another death ends the raid; successful extraction opens the normal loot transfer screen.
- **End raid** — the raid ends as killed, exactly as it would without the mod.

In a raid started as a Scav, the normal respawn buttons read **Scav near corpse**
and **Scav at spawn point**. There is no separate own-Scav continuation choice.

If an own-Scav continuation was interrupted, the main menu offers recovery.
**Abandon interrupted raid** keeps the PMC death, forfeits the unfinished Scav
loot and applies the normal Scav death/cooldown; it requires confirmation.
After extraction, **Resume loot transfer** opens the remaining Scav inventory,
preserving items already moved to the stash. A fully recorded result may instead
offer **Complete interrupted save**. Follow the recovery screen's restart prompt
before starting another raid. Unproven partial results remain blocked for diagnosis.

During an own-Scav continuation, BTR item delivery is unavailable. In its loot
transfer, **Sell All** is also unavailable: move wanted items into the stash and
finish the transfer. Ordinary PMC/Scav BTR use and ordinary Scav Sell All keep
their native behavior. See the [dated native checks](docs/PMC-SCAV-RECOVERY-20261028.md)
for the tested scenarios and remaining limits.

After respawning, run back to your body and loot your equipment from it. Redeploy leaves the corpse in the world, but does not protect its loot or prevent another mod from cleaning it up. The raid and its timer keep running while the prompt or replay is open; respawning does not restart the raid.

Spawn protection first puts away the weapon automatically drawn by the game (usually your retained knife). After your hands have cleared, drawing any weapon, including that knife or a grenade, ends protection. Equipping protective clothing alone does not end it.

By default you get one respawn per raid. Once none are left, dying ends the raid normally.

## Configuration

Configure the mod in-game with the F12 BepInEx configuration manager, or close the game and edit `BepInEx/config/com.maschine.Redeploy.cfg`. The file is created on the first successful plugin load. Values below are defaults, not forced replacements for an existing configuration.

| Option | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | Master switch. When off, death behaves like vanilla. |
| `RespawnAsScav` | `true` | Experimental: offer your regular Scav alongside naked PMC respawn when its cooldown has expired; extraction opens the normal loot transfer. Regular Scav raids keep normal Redeploy behavior. |
| `MaxRespawnsPerRaid` | `1` | Respawns allowed per raid; `0` or less means unlimited. |
| `RespawnHealthPercent` | `100` | Starting health for each body part, clamped to 1–100%; old body-part effects are cleared. |
| `RespawnCooldownSeconds` | `5` | Minimum seconds since death; time spent watching a replay counts. `0` or less removes the extra wait, but an active Killcam can still hold the respawn. |
| `CorpseSpawnDistanceMeters` | `20` | Preferred search distance, at least 5 m; half-distance and 3 m rings are also tried. This is not a guaranteed distance. |
| `SpawnProtectionSeconds` | `10` | Invulnerability after respawning; `0` disables it. |
| `RequiredRespawnItem` | `None` | Item that must be carried and is consumed to respawn. |
| `Diagnostics/EnablePipelineTiming` | `false` | Advanced setting: opt-in technical respawn measurements. Reads extra state counters and measures the following frames; leave off during normal play. |

`RequiredRespawnItem` accepts `None`, `Defibrillator`, `Grizzly`, `Salewa`, `IFAK`, `AFAK`, `CMS`, `Surv12`, `CarFirstAidKit`, `Adrenaline` or `Propital`. The check covers carried equipment and its contents, including your secure container, but not the stash. Remaining medical charges are not checked. The item must be present at death and is checked again when choosing a respawn. If it disappears later during ordinary respawn or removal fails, that path logs a warning and still respawns; this is a convenience setting, not a strict economy safeguard. Scav continuation reserves and consumes its cost before the body switch and rejects a failed cost transaction.

Only the item selection is a fixed dropdown; the other numeric settings do not expose bounded sliders. Use finite, non-negative values unless the table explicitly describes otherwise.

The F12 menu uses readable labels and units, ordered as General, Respawn and
Diagnostics. Diagnostics is marked advanced and hidden in the normal unfiltered view. Existing
configuration keys and saved preferences, including an explicitly disabled Scav
option, are preserved. Both death-menu choices use the configured respawn limit
and required item; after switching to your Scav, another death ends the raid.

The timing switch applies to Redeploy's own respawns. Other Core consumers choose
independently for each request. Successful camera/marker/freecam setup is logged at
Debug; actual failures and unexpected repairs retain their warnings. A global
BepInEx `LogLevels=All` setting still writes Debug messages.

## Known limitations

- **Fika co-op is not supported.** The mod disables itself when Fika is installed.
- **Amand's Graphics 1.8.0 on Labyrinth:** its own initial activation dereferences
  a missing `!settings` scene object before a Core replacement. Core cannot safely
  capture the incomplete graphics baseline and refuses replacement. The Factory
  camera repair does not resolve this map blocker; an ordinary PMC control has
  not been run. See [native evidence](docs/REMAINING-BODY-COMPAT-20261026.md).
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

Report problems at <https://github.com/maschine34675/Redeploy/issues> or through the channel where the build was provided.

Please include the exact Redeploy and SPT versions, what you expected and what happened instead, short reproduction steps, and for the affected raid both `BepInEx/LogOutput.log` and the server console output. For a respawn in an odd place, the lines starting with `Respawn position` in `LogOutput.log` name the map, the corpse and the chosen position, and why nearby spots were rejected. Also list the other mods you have installed, especially any that affect the camera, the player character or the end-of-raid screen.

For a missing-server warning, verify the `SPT_Runtime/user/mods/Redeploy/` path above and restart both server and game; the availability result is cached for the game session. Older log text may still mention `SPT/user/mods`. Check logs for credentials, private addresses and other personal data before sharing; do not upload an entire profile unless it is specifically needed and sanitised.

## License and credits

Released under the MIT License; see [LICENSE](LICENSE).

- The spawn protection and the optional respawn cost were inspired by **PlayerLives** and **RevivalMod** (KaiKiNoodles, gitTerebi), which approach the same problem with a downed state instead of a respawn. No code was taken from either mod.
- Redeploy contains compatibility code for **DoorDash** (tarkin), **DynamicMaps** (mpstark), **Freecam** (Terkoiz) and **Killcam**. None of these are bundled; they remain the property of their authors.

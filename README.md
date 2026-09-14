# Redeploy

> Formerly **CorpseRun** — renamed in 0.9.x. The old GUID `com.maschine.CorpseRun`, the `user/mods/CorpseRun` server folder and `maschine-CorpseRun.*.dll` files are obsolete; remove them when updating. The respawn engine now lives in the shared library `maschine-Redeploy.Core.dll` (deployed next to the client plugin), which other mods (e.g. ArenaMode) build on.

Dying no longer ends your raid: respawn without gear, fight your way back to your own corpse, and take your equipment back.

## Features

- **Choose what happens when you die.** A prompt offers a respawn near your corpse, a respawn at a regular player spawn, or ending the raid as usual.
- **Your gear stays on your corpse.** The replacement character spawns with nothing; everything you carried is lootable from your body — if you make it back.
- **What you would normally keep, you keep.** Secure container, melee weapon, armband and pocket special slots move to the replacement character, because corpses never hand those back.
- **Spawn protection.** A short invulnerability window covers the way back. It ends the moment you draw a weapon, and its last seconds are announced on screen.
- **Optional respawn cost.** Require a medical item to be in your inventory; it is consumed when you respawn.
- **Insurance keeps working.** Gear left behind on a corpse is reported as lost at the end of the raid, so insurance returns it as usual.
- **Kills are carried over.** Kills you scored before respawning still appear on the end-of-raid screen.
- **Fails safe.** When the server component is missing, the mod stays inactive and death behaves exactly like vanilla.

## Requirements and compatibility

- **SPT:** 4.1.x. The server component declares `~4.1.0`; the client is built against game build `0.16.9.40743`.
- **Components:** Client **and** server. Both are required — see the warning under Installation.
- **Dependencies:** None.
- **Optional integrations**, each installed separately and none of them required:
  - **DynamicMaps** — your position marker follows the replacement character.
  - **Freecam** (Terkoiz) — the free camera keeps working after a respawn.
  - **KillcamReplay** — the death replay stays visible behind the respawn prompt.
- **Confirmed incompatibility: Fika.** A replacement character cannot be synchronised to other co-op clients, so Redeploy disables itself when Fika is installed and says so at raid start.

## Installation

1. Extract the release ZIP into your SPT installation directory.
2. Verify that both files exist:
   - `BepInEx/plugins/maschine-Redeploy.Client.dll`
   - `SPT_Runtime/user/mods/Redeploy/maschine-Redeploy.Server.dll`
3. Start the SPT server once and confirm that `Redeploy` appears among the loaded mods.

Both components belong to the same installation. Installing only the client DLL would corrupt your profile at the end of a raid, so in that case Redeploy refuses to do anything and shows a warning when a raid starts.

## Updating

Overwrite both files with the new versions. Your configuration file is kept.

If you ran an early internal build, delete these leftovers first — otherwise an obsolete copy of the mod loads alongside the current one:

- `BepInEx/plugins/maschine-Redeploy.dll` (the old name, without `.Client`)
- `SPT/user/mods/Redeploy/` (the SPT 4.0 server path; SPT 4.1 uses `SPT_Runtime/`)

## Usage

There are no hotkeys. When you die, the raid keeps running and a window opens with the mouse cursor released:

- **Respawn near corpse** — you spawn about a configurable distance from your body, at a spot you could have walked to: reachable without a long detour, not through a wall or fence, outside the map's sniper and mine zones, with room to stand. If nothing around your body qualifies, you spawn on your body's own spot, and if that is unusable too (for example inside a sniper zone), at one of the map's regular player spawns.
- **Respawn at spawn point** — you spawn at one of the map's regular player spawns.
- **End raid** — the raid ends as killed, exactly as it would without the mod.

After respawning, run back to your body and loot your equipment from it. Your corpse stays in the world for the rest of the raid and is not protected in any way.

By default you get one respawn per raid. Once none are left, dying ends the raid normally.

## Configuration

Configure the mod in-game with the F12 BepInEx configuration manager, or edit `BepInEx/config/com.maschine.Redeploy.cfg`.

| Option | Default | Meaning |
| --- | --- | --- |
| `Enabled` | `true` | Master switch. When off, death behaves like vanilla. |
| `MaxRespawnsPerRaid` | `1` | Respawns allowed per raid; `0` means unlimited. |
| `RespawnHealthPercent` | `100` | Health the replacement character starts with (1–100). |
| `RespawnCooldownSeconds` | `5` | Minimum time between death and respawn, counted from the death: choose Respawn later than that and you spawn right away. |
| `CorpseSpawnDistanceMeters` | `20` | Preferred distance from your body for *Respawn near corpse*; closer spots (half of it, 3 m) are tried when it does not work out. |
| `SpawnProtectionSeconds` | `10` | Invulnerability after respawning; `0` disables it. |
| `RequiredRespawnItem` | `None` | Item that must be carried and is consumed to respawn. |

`RequiredRespawnItem` accepts a defibrillator, a medkit (Grizzly, Salewa, IFAK, AFAK, CMS, Surv12, car first aid kit) or a stimulant (adrenaline, Propital). If the chosen item is not in your inventory when you die, the respawn buttons are hidden and only ending the raid remains.

## Known limitations

- **Fika co-op is not supported.** The mod disables itself when Fika is installed.
- **Progress from earlier lives is only partly carried over.** Kills are restored to the end-of-raid screen, but the game starts a fresh statistics session for the replacement character, so session XP and other counters from before the last respawn are lost. This is intentional and keeps repeated respawns from paying off.
- **Items carried through a respawn lose their found-in-raid status**, the same way they do after an ordinary death.
- **Scav raids** work the same way, but have seen less testing than PMC raids.
- The mod changes how your profile is written at the end of a raid. Keep a profile backup while trying it out.

## For mod authors

Mods that keep a reference to the local player can follow body replacements through
`Redeploy.Core.Api.RespawnApi` (events `DeathClaimed`, `Replaced`, `ReplacementFailed`,
`RaidEnded`; queries `IsAvailable`, `IsReplacing`, `IsRespawnPending`). You do not need to
reference the DLL: copy `interop/RedeployRespawnInterop.cs` into your project, change its
namespace, and add the `LocalBodyTracker` component to your plugin. It binds to Core by
reflection when it is installed and falls back to polling `GameWorld.MainPlayer` otherwise,
so the same code runs with or without Redeploy. The contract is frozen at `ApiVersion` 1;
the reasoning and the guarantees are in `docs/RESPAWN-API.md`. Core's own version
(`maschine-Redeploy.Core.dll`, 1.0.1 and up) is independent of Redeploy's and only tells fix
levels apart; check `ApiVersion`, not the file version, before using the API.

## Support

Report problems at <https://github.com/maschine34675/Redeploy/issues>.

Please include the exact Redeploy and SPT versions, what you expected and what happened instead, short reproduction steps, and for the affected raid both `BepInEx/LogOutput.log` and the server console output. For a respawn in an odd place, the lines starting with `Respawn position` in `LogOutput.log` name the map, the corpse and the chosen position, and why nearby spots were rejected. Also list the other mods you have installed, especially any that affect the camera, the player character or the end-of-raid screen.

## License and credits

Released under the MIT License; see `LICENSE`.

- The spawn protection and the optional respawn cost were inspired by **PlayerLives** and **RevivalMod** (KaiKiNoodles, gitTerebi), which approach the same problem with a downed state instead of a respawn. No code was taken from either mod.
- Redeploy contains compatibility code for **DynamicMaps** (mpstark), **Freecam** (Terkoiz) and **KillcamReplay**. None of these are bundled; they remain the property of their authors.

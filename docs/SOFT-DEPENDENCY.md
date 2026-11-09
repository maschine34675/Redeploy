# Following the local body without depending on Redeploy

This file is for the author of a BepInEx mod for SPT 4.1 who wants the mod to keep working when `maschine-Redeploy.Core.dll` (Core, the shared respawn engine behind Redeploy and ArenaMode) replaces the local player's body in a raid. It covers how to integrate without a dependency: setup, what to expect on a machine with or without Core, how to test each case, how players get Core, and how to keep your copy current. The contract itself is in [API.md](API.md), the checklist of what to get right is [RULES.md](RULES.md), and worked cases are in [RECIPES.md](RECIPES.md).

The supported way in is the interop file, [`../interop/RedeployRespawnInterop.cs`](../interop/RedeployRespawnInterop.cs) (class `LocalBodyTracker`, revision 1). It binds to Core by reflection when Core is installed and watches `GameWorld.MainPlayer` when it is not, so your mod works on both kinds of machine. Third parties observe; they do not drive respawns (see [API.md](API.md#what-the-contract-is-and-what-it-is-not)).

## Start here

You have an existing mod and want it to survive a body replacement. Five steps:

1. Copy the interop file into your project ([Setup](#setup)).
2. Subscribe to `BodyChanged` in your plugin's `Awake`.
3. On every `BodyChanged` with `previous != null`, drop everything that belonged to the previous life (timers, latches, god mode, bot-ignore entries, input locks, HUD) and attach per-body components to `current` again.
4. Move any per-raid reset that hangs on a `Stop` prefix, `OnDestroy` or `UnregisterPlayer` to `RaidEnded` plus your own `GameEnd` postfix ([RULES.md](RULES.md#the-rules), rule 3).
5. Test the four availability tiers ([Verify your integration](#verify-your-integration)).

Then work through [Which of these does your mod hold?](RULES.md#which-of-these-does-your-mod-hold). If your mod vetoes `ActiveHealthController.Kill` (downed, critical or revive mods), read [RECIPES.md](RECIPES.md#mods-that-hold-death) as well. The rest of this file is reference.

## Setup

1. Copy [`interop/RedeployRespawnInterop.cs`](../interop/RedeployRespawnInterop.cs) into your project from `main` of https://github.com/maschine34675/Redeploy, or from the tag of the release you target. Copy it verbatim.
2. Change **only** the namespace line (it reads `namespace YourMod.Interop`). Keep the header comment: its revision line tells you and anyone reading your log which copy ran. Any namespace works as long as the `using` in your plugin matches; the snippet below keeps `YourMod.Interop`. Keep the class name `LocalBodyTracker`.
3. In your plugin's `Awake`, add the tracker once, on your plugin's own GameObject, and subscribe:

   ```csharp
   using BepInEx;
   using EFT;
   using YourMod.Interop;

   namespace YourMod
   {
       [BepInPlugin("com.example.yourmod", "YourMod", "1.0.0")]
       public sealed class YourModPlugin : BaseUnityPlugin
       {
           private LocalBodyTracker _bodies;

           private void Awake()
           {
               _bodies = gameObject.AddComponent<LocalBodyTracker>();
               _bodies.Log = message => Logger.LogInfo(message);
               _bodies.BodyChanged += OnBodyChanged;
           }

           private void OnDestroy()
           {
               if (_bodies != null) _bodies.BodyChanged -= OnBodyChanged;
           }

           private void OnBodyChanged(Player previous, Player current, string reason)
           {
               if (previous == null && current != null)
                   Logger.LogInfo("Redeploy interop: IsBound=" + _bodies.IsBound
                       + ", IsRespawnAvailable=" + _bodies.IsRespawnAvailable);

               if (previous != null) EndLifeState(previous);    // timers, latches, god mode, bot-ignore, input locks, HUD
               if (current != null) StartLifeState(current);    // attach per-body components, subscribe on the new body
           }

           private void EndLifeState(Player previous) { }
           private void StartLifeState(Player current) { }
       }
   }
   ```

   Three shapes reach the handler: `previous == null` is a raid start, `current == null` is a raid end, and both non-null is a replacement of any reason. Do not branch on `reason` for teardown; use it for display and logging only, because reasons are an open set. `AddComponent` belongs in the plugin's `Awake`, on the plugin's own GameObject, never in a patch, a static class or a per-raid object, where the tracker would be created again per raid or lost.

   Set `Log` straight after `AddComponent`. The default writes through Unity's `Debug.Log` with the prefix `[RespawnInterop] `; assigning your logger puts the lines into your BepInEx log source instead. The first bind attempt happens inside `AddComponent`, in the tracker's `Awake`, so a line from that first attempt can still use the default.
4. Unsubscribe in your plugin's `OnDestroy`, as above.

Add one tracker per plugin. `LocalBodyTracker.Instance` is the first tracker of your plugin; Harmony patches that need `IsRespawnPending` read it from there. Check it for null, and call `IsRespawnPending` only from a death handler, never from a veto itself (see the note after the tier table).

[`../interop/examples/RespawnConsumerExample.cs`](../interop/examples/RespawnConsumerExample.cs) is a complete plugin that follows the body, resets per-life state, handles an identity change and reads `IsRespawnPending`. Redeploy's test suite compiles it together with the interop file against the real game assemblies and BepInEx, as C# 7.3, on every run, so it stays buildable.

The early bind in `Awake` has been exercised in an offline harness only, not in a native raid. That does not affect you: the tracker retries per `GameWorld` and polls `MainPlayer` meanwhile, so a late bind loses nothing but the earliest events.

### Where your handler runs

With Core, `BodyChanged` is delivered inside the frame that replaces the body, in the middle of Core's pipeline, after the new body is wired and before spawn protection ([API.md](API.md#timeline-of-a-death)). `current` is the new, fully wired local body; `previous` is the corpse (death) or a living retired body (swap). By then the old body is already dead or retired, and your own death-path code (a `Kill` prefix that let the real death through) has finished.

Do light bookkeeping here: drop state, attach components. Do not start a respawn, stop the raid or destroy a body in the handler; defer by one frame ([RULES.md](RULES.md#the-rules), rule 7). A throw is caught and logged, but a half-finished reset of yours is not rolled back, so guard the risky parts yourself. Without Core the same handler runs from the tracker's `Update`, when the poll first sees the change ([API.md](API.md#bodychanged-semantics)).

If you already have a reliable raid-start hook, keep it as the owner of per-raid setup and use the tracker only for changes with `previous != null` and for raid end. If you use the tracker's `"raid-start"`, apply the same filter your mod uses to decide that a raid (not the hideout or a trader scene) is running, and make setup idempotent per `GameWorld`.

## Compile requirements

| Requirement | Detail |
|---|---|
| Language | C# 7.3 or newer, so latest or preview projects are fine. |
| Target | net471 or netstandard2.1. |
| References | `Assembly-CSharp`, `Comfort`, `UnityEngine` (CoreModule). The interop file does not need `BepInEx`; only your own plugin does. |
| Nullable | The file has no annotations and no `#nullable` directive. It has not been tried with a nullable context enabled or with warnings as errors. If your project does either and the file warns, add `#nullable disable` (C# 8 or newer) below the header and say so in a comment, as the [Licence](#licence-and-attribution) section asks for any edit. |
| Redeploy references | None. No reference to `maschine-Redeploy.Core`, `maschine-Redeploy.Client` or ArenaMode. |
| Threading | Unity main thread only. |

The class is `internal sealed`, so it does not leak into your public API.

## Why a copied file, and what not to do

A soft dependency on Mono takes more than a `try`/`catch`. Each of the usual shortcuts fails in a specific way:

- **A compile-time reference to `maschine-Redeploy.Core.dll`.** Any field, base type, interface, generic argument, parameter or return type in your assembly that names a Core type makes that type unloadable on a machine without Core. Your plugin may still start, because nothing constructs the type. The damage lands on other mods: anything that calls `Assembly.GetTypes()` over the loaded assemblies gets a `ReflectionTypeLoadException` from your plugin. `<Private>false</Private>` does not help. The interop file names no Core type at all; it resolves `Redeploy.Core.Api.RespawnApi, maschine-Redeploy.Core` by name and binds its members by reflection.
- **A `BepInDependency` on Redeploy or ArenaMode.** Core is not a BepInEx plugin and has no GUID. The two GUIDs that exist (`com.maschine.Redeploy`, `com.maschine.ArenaMode`) belong to drivers, mods that own respawn policy. Depending on one says "my mod needs a driver", which is false, and a hard dependency refuses to load your mod on every machine without it. Soft dependencies on them express nothing either.
- **Shipping a copy of Core.** The file name and path are shared with Redeploy and ArenaMode, so your copy would overwrite or be overwritten depending on extraction order. A driver built against a newer Core then refuses to start. See [How players get Core](#how-players-get-update-and-remove-core).
- **Probing once and remembering "not installed".** Plugin load order and scene changes mean Core's type may not resolve in your `Awake` and resolve later. The tracker retries whenever a new `GameWorld` appears while it is unbound and never caches a negative probe. Do not put an "is Core installed" check of your own in front of it: `IsBound` is the answer, and it can change when a new raid starts.
- **Calling engine internals** (`RespawnEngine`, `DeathInterception`, `RespawnClaims`, `RaidLifecycle`, `RedeployCore`, `CoreSignals`, the legacy events such as `OnDeathIntercepted` and `OnPlayerRespawned`). They are public for Redeploy's own drivers, not for other mods, and may change in any Core release. Core also ships no adapters for mods that can integrate themselves; you observe through `Redeploy.Core.Api.RespawnApi` by way of the interop file. The reasons are in [API.md](API.md#what-the-contract-is-and-what-it-is-not).
- **Starting a respawn, stopping the raid or destroying a body from a `BodyChanged` handler.** See [RULES.md](RULES.md#the-rules), rule 7.

## Availability tiers

The same build of your mod meets four situations. All four must work.

| Tier | Machine | `IsBound` | `IsRespawnPending` | Events | Raid end |
|---|---|---|---|---|---|
| Core absent | No `maschine-Redeploy.Core.dll`, a Core older than API v1, or one whose `Replaced` event is unusable | false | `null` | `BodyChanged` from the poll only; no `DeathClaimed`, `ReplacementFailed` or `RaidEnded` | Late: `"raid-end"` arrives when the `GameWorld` goes away |
| Core installed but idle | Core present, but Fika is loaded, no driver is installed, or Core's keep-alive patches are incomplete (`IsAvailable` false) | true | `null` | `DeathClaimed`, `Replaced` and `ReplacementFailed` do not fire; the poll still reports body changes made by other means | `RaidEnded` can still arrive if Core's raid-end hook is installed (it does not depend on `IsAvailable`); otherwise late, as in the first tier. Do not rely on it |
| Core active, Redeploy disabled | Core and drivers present, Redeploy's `Enabled` setting off. `IsAvailable` is true, but Redeploy's driver never claims a death | true | A real value. For a death that only Redeploy would have handled it is false, because Redeploy never claims it | No `DeathClaimed` or `Replaced` from Redeploy. ArenaMode raids still respawn through ArenaMode, with full events: treat the row as Core active whenever an ArenaMode raid is running | Exact (`RaidEnded`) |
| Core active | Core, a driver, no Fika | true | A real value | `DeathClaimed`, `Replaced` (as `BodyChanged`), `ReplacementFailed`, `RaidEnded` | Exact: `GameEnd` postfix, after drivers and Core have reset |

Things to take from the table:

- `IsBound` says Core's API was found, not that respawns will happen. `IsRespawnAvailable` (Core's `IsAvailable`) says Core can respawn in this session. Neither says a given death will be respawned; that is what `IsRespawnPending` is for, and it has its own limits, set out in [API.md](API.md#isrespawnpending).
- `IsRespawnPending` returns `null` when unbound, when Core is older than API v1, or when `IsRespawnAvailable` is false. Treat `null` as "unknown", and `null` and `false` alike as "carry on as before". Do not treat `null` as false when you have an explicit choice to make.
- The poll is always running, so a respawn done by a mod that is not Core still produces a `BodyChanged`. A respawn that Core performs is reported once, not twice: the tracker de-duplicates by body reference.
- Without Core, raid end is late. For per-raid resets keep an exact boundary of your own: a postfix on `BaseLocalGame<EftGamePlayerOwner>.GameEnd` filtered to `LocalGame`, and reset on `RaidEnded` as well when you have it. Do not use a `Stop` prefix, `OnDestroy` or `UnregisterPlayer` ([RULES.md](RULES.md#the-rules), rule 3).
- The hideout and the trader narrate scenes also appear as `"raid-start"`. See the end of [Where your handler runs](#where-your-handler-runs) for which hook should own per-raid setup.
- In a normal respawn there is no wait: `BodyChanged` arrives inside the replacing frame. While Core reports a replacement in progress (`IsReplacing`) the poll waits for Core's `Replaced` instead of reporting the intermediate state, but that only matters after a failed replacement, as a last-resort reconciliation with `MainPlayer` after at most 35 seconds. Do not add timeouts of your own.

`IsRespawnPending` answers only for a body that is already dead. Do not call it from a prefix on `ActiveHealthController.Kill`: the body is alive there and the answer is false, so it cannot tell you whether to down the player. A mod that vetoes `Kill` needs no change to its veto; Core stays silent while you hold death and claims only after you let a real death through. Use `IsRespawnPending` in death handlers (after `Kill` went through), for UI or sounds; it is a prediction, not a promise.

## Verify your integration

The tiers can be produced on one machine. For each, check the log lines below and your own handler's trace.

| Tier | How to produce it | What you should see |
|---|---|---|
| Core absent | Remove `maschine-Redeploy.Core.dll` from `BepInEx/plugins` (and with it Redeploy and ArenaMode, which need it) | No `bound` line. `BodyChanged` with `"raid-start"` still fires; your own log reads `IsBound=False` |
| Core idle | Core installed with Fika loaded, or with no driver installed | A `bound` line, `IsBound=True`, `IsRespawnAvailable=False`. A death is not claimed |
| Redeploy disabled | Core with Redeploy installed; turn Redeploy's `Enabled` setting off | `IsRespawnAvailable=True`. No `DeathClaimed` on a death |
| Core active | Die in a raid with Redeploy on | In your log, in order: your mod's death path, `DeathClaimed`, then (after the player accepts the respawn) `BodyChanged(old, new, "death")` |

Run each tier at least through a raid start, a death and a raid end. The log-line section below says what the tracker and Core print.

## Log lines to look for

Interop lines come out through your `Log` delegate, except the first bind attempt, which runs in `Awake` before you can assign `Log` and goes to Unity's log with the prefix `[RespawnInterop] ` (so does every line if you never set one):

- `bound to RespawnApi v1 (interop revision 1)` - Core was found and the tracker subscribed.
- `binding failed, polling only (interop revision 1): <message>` - Core's type resolved but binding threw. The tracker unbinds and keeps polling. Report this one.

Neither line appears when Core is absent: the tracker then polls silently. Absence of a "bound" line is the normal signal for Core not installed; to tell that apart from a tracker that never started, log `IsBound` yourself, as the snippet above does. The tracker retries at the next `GameWorld`, so a bound line can appear later in the log.

Handler failures are logged and isolated:

- `BodyChanged subscriber failed: <exception>`, and likewise `DeathClaimed`, `ReplacementFailed` and `RaidEnded` with `subscriber failed:`, come from the tracker when your handler throws.
- `<EventName> subscriber <Type.Method (assembly)> failed: <exception>` comes from Core's own dispatcher, on the `Redeploy.Core` log source.

Core's own lines, on the `Redeploy.Core` log source:

- `Redeploy.Core <version> initialized` - the Core version line.
- `Death-interception driver registered: <name> (priority <n>)` - a driver registered. The name is `Redeploy` (priority 0) or `ArenaMode` (priority 100); the BepInEx GUIDs are `com.maschine.Redeploy` and `com.maschine.ArenaMode`.
- `Death interception is incomplete — Core stands down: no death is claimed, nothing is blocked, RunRespawn is refused.` - error. Core is idle: no death is claimed and no replacement happens; your tracker is bound.
- `Fika detected — Redeploy is disabled (respawn is incompatible with co-op).` - warning. Core is idle under Fika.

The two lines with a dash use an em dash. If you search a log, search for the text on either side of it.

## How players get, update and remove Core

Core ships inside two release zips: Redeploy's and ArenaMode's. Both contain the same file at the same path, `BepInEx/plugins/maschine-Redeploy.Core.dll`, byte-identical for a given Core version. Redeploy's zip also carries `BepInEx/plugins/maschine-Redeploy.Client.dll` and `SPT_Runtime/user/mods/Redeploy/maschine-Redeploy.Server.dll`.

Because the name and path are the same, the last zip extracted wins. That is harmless for your mod. It matters to the drivers: only the drivers check Core's version, and each refuses a Core older than the one it was built against. The result is an error in the BepInEx log and SPT's red "plugin(s) failed to load" notice for that driver. You never see this, because your tracker does not check Core's file version. It checks `ApiVersion`, and only that it is at least 1.

What this means for you:

- **Getting Core.** A player gets it by installing Redeploy or ArenaMode. Your mod's page should not tell them to install either, and should not say that your mod requires Redeploy: it does not, and players without a respawn mod will not have Core. If you want to mention it, say that your mod also follows the body through Redeploy's and ArenaMode's respawns when one of them is installed.
- **Updating Core.** The player updates through the Redeploy or ArenaMode release. Redeploy's README tells them to keep exactly one copy of the Core file and to keep the newer one when two disagree. The API version has stayed at 1 across every published release since Redeploy 1.0.0 (bundled Core 1.0.8), so nothing you copied needs to change when they update.
- **Removing Core.** Players remove `maschine-Redeploy.Core.dll` only when no installed mod uses it. Your mod does not count as a user in that sense: with Core gone, the tracker polls, as in the first tier of the table above.

The fix level of Core (`RedeployCore.Version`, the number in the log line above) is not the contract and is not in the frozen API surface. Gate on `ApiVersion`, which the interop file already does, never on the file version.

## Fika-only builds

Core stands down when Fika is loaded: `IsAvailable` is false, no death is claimed and nothing is replaced. Under Fika Core never replaces a body, so nothing in your mod needs to change for Fika alone; a plain `GameWorld.MainPlayer` read is enough. If one build serves both, the interop file is harmless there: `IsRespawnPending` returns `null`.

## Keeping your copy current

- The master is `interop/RedeployRespawnInterop.cs` on `main` of https://github.com/maschine34675/Redeploy.
- The header of every copy carries the revision, and the file exposes it as the constant `LocalBodyTracker.Revision`. It changes only when the file changes, never with Core. The bind line in your log names it.
- Revisions are listed in [CHANGELOG.md](CHANGELOG.md). Every interop change bumps `Revision` and stays compatible with Core API v1, so an older copy keeps working against every Core that reports `ApiVersion` 1. Existing members never change meaning when `ApiVersion` 2 arrives; a copy that binds only v1 members keeps seeing them.
- The file was unchanged from Redeploy 1.0.0 until revision 1 (2026-11-09). Revision 1 added the header, the `Revision` constant, the revision in the bind log line and corrected `ProfileId` comments; behaviour did not change. A copy without a header is revision 0 and behaves the same.
- Update policy: take a new revision when its changelog entry matters to you, not on a schedule. Copy the new file, change only the namespace line again, and rebuild. There is nothing to migrate. If you have edited your copy, you are on your own for merges: keep local changes outside the file, in your handlers.
- The API surface is additive only. A new public member will mean `ApiVersion` 2 and a new baseline; existing members never change meaning. See [API.md](API.md#the-contract-level).

## Licence and attribution

The interop file and the example are MIT, copyright 2026 maschine, part of [Redeploy](https://github.com/maschine34675/Redeploy). Only the copied file keeps the MIT notice; the rest of your mod keeps your licence. Keep the header (the SPDX line, the copyright line and the revision line) in your copy. If you edit the file beyond the namespace line, say so in a comment below the header, so a bug report is not read against the master.

## Reporting a problem

Open an issue at https://github.com/maschine34675/Redeploy/issues. Include:

- The Core version line: `Redeploy.Core <version> initialized`, or a statement that Core is not installed.
- The interop revision line: `bound to RespawnApi v1 (interop revision 1)` or the `binding failed, polling only` line. If neither appears, say so.
- The SPT version, and whether Fika is installed.
- Which driver was active: Redeploy, ArenaMode or both, and, for Redeploy, whether its `Enabled` setting was on. The `Death-interception driver registered` lines show which registered.
- The BepInEx log from the session (`LogOutput.log`), from game start to the point of failure. Remove anything personal first.
- What your handler did, in a sentence, and the reason string your handler received.

If you need a member that API v1 does not have, open an issue as well, with the use case. Additions are considered case by case; there will never be a way for a consumer to veto or stall a respawn.

# Recipes

Worked how-tos for a mod that has to keep working when Redeploy.Core replaces the local player's body in the middle of a raid. Each recipe states the problem, shows the code and lists what can go wrong. [API.md](API.md) is the contract, [SOFT-DEPENDENCY.md](SOFT-DEPENDENCY.md) covers copying the interop file and the availability tiers, and [RULES.md](RULES.md) is the short checklist that links back here.

All snippets are C# 7.3 and use only members of [`LocalBodyTracker`](../interop/RedeployRespawnInterop.cs) (revision 1), the public game types and BepInEx/Harmony. Names `YourModPlugin` and `YourBodyBehaviour` come from the example; `_bodies` is the tracker field of that plugin, and `LocalBodyTracker.Instance` is the same tracker for code that has no reference to the plugin.

**Status.** Redeploy's tests compile the complete example in [`../interop/examples/RespawnConsumerExample.cs`](../interop/examples/RespawnConsumerExample.cs) and every snippet in this file and in [SOFT-DEPENDENCY.md](SOFT-DEPENDENCY.md) that is written as a complete file (the plugins, `YourBodyBehaviour`, the `OnDead` and `GameEnd` patches, `DeferredWork`, `CameraFollower`, the sketch in 8.5), each together with the interop file against the game's assemblies, BepInEx and Harmony, as C# 7.3, whenever the game sits next to the repository. Shorter fragments are checked by reading. A name that no snippet defines (`RunStats`, `ResetPerLifeState`, `ReleasePerLifeResources`, `StartRaidLatches`, `SendRequest`, `Store`) stands for your own code. `_deferred` is the `DeferredWork` class in recipe 4. Where something is untested natively, the text says so.

Terms as in the other documents: **Core** is `maschine-Redeploy.Core.dll`, the shared respawn engine; a **driver** is a mod that owns respawn policy and asks Core to replace the body (Redeploy and ArenaMode); a **body** is an `EFT.Player` instance; a replacement ends in reason `"death"` (the old body stays as the corpse) or `"swap"` (the old body is alive and is disposed later by whoever requested the replacement).

## Where to start

| You are writing | Read |
|---|---|
| A mod that vetoes `Kill` (downed, critical or revive mod) | Recipe 8 first, then 4, 6, 5 and the `IsYourPlayer` table in 2 |
| A component or tick on the player | Recipe 2 |
| Camera, HUD or health-event hooks | Recipe 7 |
| A reaction to the player's death only | Recipe 3 |
| Per-raid counters or latches | Recipe 6 |

## Contents

1. [Start from the example](#start-from-the-example)
2. [A per-body component](#per-body-components)
3. [A decision at the moment of death](#reacting-to-a-death)
4. [Reset per-life state, and defer destructive work](#reset-per-life-state)
5. [Identity change on own-Scav continuation](#identity-changes)
6. [Raid-scoped latches](#detecting-the-end-of-a-raid)
7. [Camera and HUD rebind](#cameras-and-hud)
8. [Mods that hold death (downed and revive mods)](#mods-that-hold-death)
9. [Do you need an adapter? (almost never)](#do-you-need-an-adapter)

<a id="start-from-the-example"></a>
## 1. Start from the example

**Problem.** You want the smallest working integration and no dependency on Redeploy.

**Steps.**

1. Copy `interop/RedeployRespawnInterop.cs` into your project, change only the namespace line, keep the header. Details and compile requirements: [SOFT-DEPENDENCY.md](SOFT-DEPENDENCY.md).
2. Add one tracker to your plugin's own GameObject and subscribe to `BodyChanged`:

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
            // previous == null: raid start. current == null: raid end.
            // reason "death" or "swap": a replacement. Treat any other value like "unknown".
        }
    }
}
```

A mod built from static classes subscribes once in the plugin's `Awake` and points the event at static handlers.

3. Read the log. With Core installed and bindable, the bind line is written when the tracker is added, before your `Log` is set, so it appears as a Unity log line starting `[RespawnInterop] `: `bound to RespawnApi v1 (interop revision 1)`. That line reaches `LogOutput.log` only when BepInEx's `[Logging.Disk] WriteUnityLog` is true (default false), so log `LocalBodyTracker.Revision` and `IsBound` yourself right after setting `Log`, as [SOFT-DEPENDENCY.md](SOFT-DEPENDENCY.md#setup) does. A bind retried later, when a new `GameWorld` appears, uses your `Log`. `binding failed, polling only` means an exception during binding; the rest of the line says why. A Core that is older than API v1, or that lacks `Replaced`, is skipped silently, so check `IsBound`. Without Core you see nothing from the tracker and it polls `GameWorld.MainPlayer`; that is the intended fallback, not an error.

**What can go wrong.**

- A second tracker (for example one per feature) binds to Core again and keeps its own chain. Handlers on the two trackers see the same changes, so a feature that listens to both runs twice, and `LocalBodyTracker.Instance` and its queries stay tied to the first. Use one tracker per plugin and pass it around.
- `BodyChanged` can be raised inside Core's replacement (mid-pipeline). Do not do anything in the handler that starts a respawn, stops the raid or destroys a body. Recipe 4 shows the one-frame defer.
- `BodyChanged` also fires with reason `"raid-start"` in the hideout and in trader narrate scenes, because the tracker only sees `GameWorld.MainPlayer` change. See recipe 6 for a filter.

<a id="per-body-components"></a>
## 2. A per-body component

**Problem.** A MonoBehaviour that lives on the player (a breacher, an aim assist, a recorder) must run on the local body only. After a replacement the old body would keep running it, and a corpse keeps every component.

```csharp
private void OnBodyChanged(Player previous, Player current, string reason)
{
    LocalBodyTracker.Detach<YourBodyBehaviour>(previous);          // null-safe
    if (current != null) LocalBodyTracker.AttachFresh<YourBodyBehaviour>(current);
}
```

```csharp
using EFT;
using UnityEngine;
using YourMod.Interop;

public sealed class YourBodyBehaviour : MonoBehaviour
{
    private Player _player;

    private void Awake()
    {
        _player = GetComponent<Player>();
    }

    private void Update()
    {
        if (!LocalBodyTracker.IsCurrentLocalBody(_player)) return;
        // Per-frame work for the living local body.
    }
}
```

Why both halves:

- `Detach` removes the component from the body that stopped being local. For `"death"` that body is the corpse, which would otherwise keep it. For `"swap"` it is a living body that is still in the world for a while.
- `AttachFresh` destroys a stale instance before adding a new one. A corpse can return to the player pool with its components, so a later body may already carry one.
- `IsCurrentLocalBody` is the per-frame guard. It is `ReferenceEquals(GameWorld.MainPlayer, body) && IsAlive`, which is the vanilla test for the current living local body. `IsYourPlayer` cannot tell the old body from the new one (the next section says where it is still fine), and `ProfileId` is shared by the bodies of an ordinary respawn.

No component? The same guard applies to a Harmony postfix that ticks on a `Player` method, and to a static class your plugin's `Update` calls:

```csharp
[HarmonyPostfix]
private static void Postfix(Player __instance)
{
    if (!LocalBodyTracker.IsCurrentLocalBody(__instance)) return;
    Tick(__instance);
}
```

<a id="tracking-the-current-body"></a>
**Identify the current body.** The current living body is `ReferenceEquals(GameWorld.MainPlayer, p) && p.HealthController.IsAlive`, which is what `IsCurrentLocalBody` computes. `LocalBodyTracker.Current` is the last body the tracker reported. Do not cache the local `Player` in a static: after a replacement it points at the corpse. Resolve through `GameWorld.MainPlayer` or `LocalBodyTracker.Current` at the point of use, or store the instance and compare references.

<a id="isyourplayer-triage"></a>
### Triage your `IsYourPlayer` checks

The rule is "`IsYourPlayer` cannot tell old from new", not "delete every use". Most existing checks are "is this the local human, not a bot", and that is a legitimate question. Sort each check:

| The check is in | Use |
|---|---|
| Per-tick, per-frame or UI logic that must stop on corpses and retired bodies | `IsCurrentLocalBody(p)` |
| A `Kill`, `ApplyDamage` or `OnDead` patch that only needs "local human rather than bot" | `IsYourPlayer` is still correct. If the patch acts on state that outlives the body, also require `ReferenceEquals(MainPlayer, p)`: a corpse is `IsYourPlayer` too, and by reading Core nothing clears the flag on a retired swap body either (untested natively) |
| A death handler | `ReferenceEquals(MainPlayer, p)` only; `IsCurrentLocalBody` fails there because the body is dead |
| Code that stores the player | Store the instance and compare references |

Do not replace every `IsYourPlayer` with `IsCurrentLocalBody`: that breaks every death-time handler.

**What can go wrong.**

- Attaching only at raid start: the new body never gets the component after the first respawn.
- `GetOrAdd` instead of `AttachFresh`: a stale component from a pooled body keeps its old state.
- Gating a component's per-frame logic on `IsYourPlayer`: the corpse keeps running it, for example reaching for a weapon or opening doors. A patch on `Player.UpdateTick` is different: the game ticks only registered living players, so it does not tick the corpse, but it does tick the new body, which must not inherit the old life's state.
- Caching the local `Player` in a static.

<a id="reacting-to-a-death"></a>
## 3. A decision at the moment of death

**Problem.** A `Player.OnDead` postfix decides what the death means for your mod (end a run, stop a recording, show a summary). With Core a death may be followed by a replacement, so "the player died" is not always "the raid is over".

```csharp
using Comfort.Common;
using EFT;
using HarmonyLib;
using YourMod.Interop;

namespace YourMod
{
    [HarmonyPatch(typeof(Player), nameof(Player.OnDead))]
    internal static class PlayerOnDeadPatch
    {
        [HarmonyPostfix]
        private static void Postfix(Player __instance)
        {
            // A bot, or a retired swap body that died later: not the local player's death.
            if (!Singleton<GameWorld>.Instantiated
                || !ReferenceEquals(Singleton<GameWorld>.Instance.MainPlayer, __instance)) return;

            LocalBodyTracker tracker = LocalBodyTracker.Instance;
            bool? pending = tracker != null ? tracker.IsRespawnPending(__instance) : null;

            if (pending == true)
            {
                // A driver holds the raid open for this death. A replacement is possible, not promised.
                // Do not treat the raid as over yet; a Killed Stop may still follow, so keep your Stop handling.
            }
            else if (pending == false)
            {
                // Core will not replace this body. A respawn mod outside Core still might: not "final death".
            }
            else
            {
                // null: no Core, Core older than v1, or Core idle (Fika, no driver). Keep your vanilla logic.
            }
        }
    }
}
```

Apply the patch with your usual Harmony setup (BepInEx ships Harmony; `new Harmony("com.example.yourmod").PatchAll()` in `Awake` works). `LocalPlayer` overrides `OnDead` and calls the base method, so the postfix on `Player.OnDead` runs for the local body as well.

Points that matter:

- Use `ReferenceEquals(MainPlayer, body)` and not `IsCurrentLocalBody` here: the body is already dead, and `IsCurrentLocalBody` requires `IsAlive`.
- At this point in the death `IsRespawnPending` is a **prediction**: Core knows a driver will claim, not whether it will replace. The claim itself comes later, in `DeathClaimed`, before the driver acts. After the claim the answer is true until the driver resolves it. One known case where it flips: the match-deciding death in ArenaMode Team DM predicts true and turns false right after the claim.
- `false` means "Core will not replace this body", for example a bot, a body that is alive, a raid that is stopping or an unclaimed death. It does not mean "final death".
- `null` is a normal, expected answer. It is what you get without Core.

If you need the confirmed claim instead of the prediction, subscribe to `_bodies.DeathClaimed` (Core only) and handle it there. A claim is still not a promise: the player may give up, a required item may be missing or the mode may end the match.

**What can go wrong.**

- Treating `pending == true` as "the player will respawn": the player can give up and the raid then ends with a Killed Stop.
- Treating `pending == false` as "dead for good" and finalising irreversibly: an outside respawn mod may still act.
- Skipping the `MainPlayer` check: a retired swap body can die too, and bots die all the time.
- Expecting `DeathClaimed` for every death. A refused replacement raises no public event (see [API.md](API.md#refusals)): you can see `DeathClaimed` followed by neither `Replaced` nor `ReplacementFailed`. The driver then handles it, usually with a Killed Stop, and `RaidEnded` follows at the raid end. Do not assume anything about `IsRespawnPending` after such a refusal.

<a id="reset-per-life-state"></a>
## 4. Reset per-life state, and defer destructive work

**Problem.** Some state describes one life: timers, "already dead" latches, temporary overrides, a damage log. It must end with the old body whatever the reason, and some of the clean-up must not run in the middle of Core's replacement.

Per-life state belongs either to the `Player` instance or to a reset in `BodyChanged`. What runs inside the handler and what waits one frame:

| When | What |
|---|---|
| **Now**, inside the handler | Your own flags, dictionaries and latches; the input-block flag; stopping or invalidating your own coroutines and timers (bump the epoch below); unsubscribing; the cycle counter |
| **Next frame** | Anything that calls into `previous`'s components (`MovementContext`, animator, ragdoll, `HandsController`, effects); destroying bodies, overlays and other objects that you do not own as per-body components (`Detach<T>` and `AttachFresh<T>` are fine now); visual teardown that reads the old body |
| **Never** from a handler | Starting a respawn (`RunRespawn`), stopping the raid (`Stop`), destroying a body |

If a flag change must be seen by the new body at once (an input lock), clear the flag now and defer only the visuals. The reverse hazard: a fully deferred reset leaves the new body live for one frame with the old lock or timers still active. Touching the old body's components inside the handler has not been tested natively; Core has just torn that body down (unregistered, ownerless), so defer it.

```csharp
private void OnBodyChanged(Player previous, Player current, string reason)
{
    if (previous != null)
    {
        _epoch++;                              // now: invalidates async work, see below
        ResetPerLifeState();                   // now: flags and dictionaries only
        _deferred.NextFrame(() => ReleasePerLifeResources(previous));   // next frame: touches the old body
    }
}
```

`_deferred` is a small helper component. It runs queued work from `Update`, so it never runs inside Core's replacement:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

internal sealed class DeferredWork : MonoBehaviour
{
    private readonly Queue<Action> _queue = new Queue<Action>();

    public void NextFrame(Action action)
    {
        _queue.Enqueue(action);
    }

    private void Update()
    {
        int count = _queue.Count;                  // work queued from here on waits for the next frame
        for (int i = 0; i < count; i++)
        {
            Action action = _queue.Dequeue();
            try { action(); }
            catch (Exception ex) { Debug.LogError("[YourMod] deferred work failed: " + ex); }
        }
    }
}
```

<a id="coroutines-and-timers"></a>
A coroutine does the same job and can re-check identity after the wait:

```csharp
private IEnumerator ReleaseNextFrame(Player previous)
{
    yield return null;
    // Identity may have moved on again during the frame: check, never assume.
    if (previous == null) yield break;           // destroyed meanwhile
    // ... destroy overlays, stop recordings, release resources owned by that body ...
}
```

Start it with `StartCoroutine(ReleaseNextFrame(previous))` on your plugin or tracker GameObject. A coroutine started on the old body's own GameObject stops when that object is destroyed; start it on your own. A timer coroutine captures the body instance when it starts and compares after every `yield`.

**Async work that is not a coroutine** (a queued server call, an authorisation that comes back later, a self-revive that asks the server and applies the reply) needs a counter instead. Keep an `int _epoch`, bump it in `BodyChanged` when `previous != null` and at every raid reset, capture it before any wait or request, and drop the result when it changed:

```csharp
int epoch = _epoch;
SendRequest(reply =>
{
    if (epoch != _epoch) return;     // the body or the raid changed while we waited
    Apply(reply);
});
```

<a id="when-a-replacement-fails"></a>
**When a replacement fails.** `ReplacementFailed(oldBody, stage)` means a started replacement ended without `Replaced`. Core does not roll back. Drop bodies you cached and read `GameWorld.MainPlayer` again in the next frame; the tracker's poll reconciles `Current` on its own:

```csharp
_bodies.ReplacementFailed += (oldBody, stage) =>
{
    _deferred.NextFrame(() => RefreshFromMainPlayer());   // never act on oldBody
};
```

<a id="event-handlers-and-failures"></a>
How handlers are called. Through the tracker, each `BodyChanged`, `DeathClaimed`, `ReplacementFailed` and `RaidEnded` handler runs in its own try/catch:

- A throw does not stop the other handlers. The tracker logs it through its `Log` as `BodyChanged subscriber failed: <exception>` (and likewise `DeathClaimed subscriber failed:` and so on).
- The invocation list is snapshotted, so subscribing or unsubscribing inside a handler takes effect from the next event.
- The tracker does not skip handlers on destroyed objects, so unsubscribe in `OnDestroy`.
- Core's own line `<EventName> subscriber <Type.Method (assembly)> failed: <exception>` on the `Redeploy.Core` log source appears only for a direct subscriber to `RespawnApi`. Through the tracker it names `LocalBodyTracker.OnApiReplaced`, and only if the tracker itself throws.
- Order between subscribers is not part of the contract.

**What can go wrong.**

- State keyed by `ProfileId` survives: ordinary respawns and swaps keep the ProfileId, so a "next death is allowed" latch stays set and the next lethal hit kills at once. Key per-life state by the `Player` instance or reset it on every `BodyChanged` that has a `previous`.
- Resetting only on `"death"`: a `"swap"` leaves a living old body behind, and its timers, god-mode flags and locks must end too.
- Coroutines that survive the body: a timer started for the old body fires later against `GameWorld.MainPlayer`, which is now the new body.
- Destroying the old body from the handler: a retired swap body is disposed by its requester, and a destroyed body inside the pipeline breaks the replacement. Leave bodies alone.

<a id="identity-changes"></a>
## 5. Identity change on own-Scav continuation

**Problem.** Redeploy's optional own-Scav continuation (Redeploy 1.1.0 and later) replaces the PMC with the player's Scav profile. The reason is still `"death"` and there is no flag, but the body now belongs to a different profile. State keyed by the old `ProfileId` describes someone else.

```csharp
private readonly Dictionary<string, RunStats> _statsByProfile = new Dictionary<string, RunStats>();

private void OnBodyChanged(Player previous, Player current, string reason)
{
    if (previous != null) ResetPerLifeState();

    bool newIdentity = previous != null && current != null && previous.ProfileId != current.ProfileId;
    if (newIdentity)
    {
        // Initialise as at raid start: the new profile has its own inventory, skills and history.
        _statsByProfile[current.ProfileId] = new RunStats();
        // The old identity ends here. Drop or archive what was keyed by it, as your mod needs.
        _statsByProfile.Remove(previous.ProfileId);
    }
}

private sealed class RunStats { /* ... */ }
```

To detect an identity change compare the `ProfileId` strings, not the `Player` references, which differ in every respawn, and do not use `==` on `Profile` objects.

<a id="registries-keyed-by-profileid"></a>
### Registries keyed by ProfileId

A dictionary of ProfileId to `Player` that your mod fills from the game's person events needs two rules. A replacement adds the new body under the same ProfileId, and the old body's removal can arrive after that add, so:

- add or overwrite on the add event;
- on the remove event (unspawn), remove only if the stored instance is the body that vanished.

```csharp
private readonly Dictionary<string, Player> _byProfile = new Dictionary<string, Player>();

private void OnPersonAdded(Player body)
{
    _byProfile[body.ProfileId] = body;                  // overwrite: the new body replaces the old entry
}

private void OnPersonRemoved(Player body)
{
    Player stored;
    if (_byProfile.TryGetValue(body.ProfileId, out stored) && ReferenceEquals(stored, body))
        _byProfile.Remove(body.ProfileId);              // not if the new body already took the entry
}
```

These handlers run inside the pipeline: they must never throw (recipe 7).

**What can go wrong.**

- Detecting the continuation by `reason`: there is no such reason. Compare `previous.ProfileId` with `current.ProfileId`.
- Keeping the old profile's state alive for the new body: bot-ignore lists, stat counters, inventory snapshots, quest hooks and other things keyed by profile then describe the dead PMC.
- Assuming the profile never changes within a raid: it does when this feature is on, and ordinary respawns (same ProfileId) hide the problem in testing.
- Removing a registry entry unconditionally on unspawn: it can delete the new body's entry.

Server mods have a related note in recipe 8 ([8.11](#server-side-mods)).

<a id="detecting-the-end-of-a-raid"></a>
## 6. Raid-scoped latches

**Problem.** Some state belongs to a raid: "already announced", "first kill", a per-raid life counter. It must be reset exactly once per raid and not at a body change.

**Which raid-end signal.** Make one idempotent `ResetRaid()` and call it from every signal you have:

1. your own raid-start hook, or `BodyChanged` with `previous == null` (filtered to a local raid, below);
2. `BodyChanged` when `current == null`, which works with and without Core;
3. any raid-end hook you already have (a `Stop` or `GameEnd` patch).

Subscribe to `RaidEnded` as well only if you want the exact Core moment. Calling `ResetRaid()` twice must be harmless, so do not choose between the signals: wire the ones you have and keep the reset idempotent. Keep the raid-boundary hooks you already have if they are idempotent; add the tracker, do not remove them.

With Core, `RaidEnded` is the exact signal. It is raised once per `LocalGame` raid from the `GameEnd` postfix, after the drivers and Core have reset, and never for Fika's `CoopGame`, the hideout or trader narrate scenes:

```csharp
_bodies.RaidEnded += ResetRaid;              // Core only; never fires without Core

private void ResetRaid()
{
    _announced = false;
    _livesLeft = StartingLives;
}
```

Without Core you need your own signal: a postfix on `BaseLocalGame<EftGamePlayerOwner>.GameEnd`, filtered to `LocalGame`:

```csharp
using System.Reflection;
using EFT;
using HarmonyLib;

[HarmonyPatch]
internal static class GameEndPatch
{
    private static MethodBase TargetMethod()
    {
        return AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "GameEnd");
    }

    [HarmonyPostfix]
    private static void Postfix(object __instance)
    {
        if (!(__instance is LocalGame)) return;     // not Fika's CoopGame, not the hideout
        // call your idempotent ResetRaid() here
    }
}
```

`GameEnd` is `async Task`: the postfix runs when the method first returns to its caller (at its first `await`), not when the whole task completes. Core's own postfix runs at the same point. With Core the order between your postfix and Core's reset is not something to build on; `RaidEnded` is the one that comes after Core has reset.

Start of a raid: `BodyChanged(null, body, "raid-start")`. The hideout and the trader narrate scenes also produce it. If your latches are raid-only, filter on the game type:

```csharp
private static bool IsLocalRaid()
{
    return Singleton<AbstractGame>.Instantiated && Singleton<AbstractGame>.Instance is LocalGame;
}

private void OnBodyChanged(Player previous, Player current, string reason)
{
    if (previous == null && current != null && IsLocalRaid()) StartRaidLatches();
}
```

`NarrateGame` and `HideoutGame` are not `LocalGame`. Under Fika `CoopGame` is not either, which is correct: Core idles under Fika.

Things that are **not** a raid end on their own, whatever they look like:

| Looks like a raid end | Why it is not |
|---|---|
| a prefix on `LocalGame.Stop` / `BaseLocalGame.Stop` | Fine as an additional idempotent safety reset, not as the only signal. While Core holds a death it blocks the Killed `LocalGame.Stop` override, so a prefix on `BaseLocalGame.Stop` does not run then; a prefix on `LocalGame.Stop` itself depends on Harmony order. Both run later if the player gives up and Core lets the Stop through, and a replacement never runs either. |
| `OnDestroy` of something | Destroyed for many reasons. |
| `GameWorld.UnregisterPlayer` | Vanilla calls it one frame after every death, bots included. |
| unspawn of the player | Happens in every replacement. |
| the local player's death | The raid may continue after it. |

Without Core, `BodyChanged(previous, null, "raid-end")` comes from the tracker as well, but late: when the `GameWorld` goes away. Use the `GameEnd` postfix for exact timing.

**What can go wrong.**

- Resetting a per-raid counter in `BodyChanged` for every change: a respawn then refills it. Reset only when `previous == null` or `current == null`, and decide per raid or per life on purpose (recipe 8).
- Forgetting the `LocalGame` filter: the hideout's game ends too and clears your raid state while no raid took place.
- Waiting for `RaidEnded` without Core: it never comes.
- Subscribing to all three signals with a reset that is not idempotent.

<a id="cameras-and-hud"></a>
## 7. Camera and HUD rebind

**Problem.** After a replacement the camera controller, the owner and the health controller are new objects. Anything that hooked the old ones keeps looking at a corpse or at nothing.

Core rewires the vanilla pieces before it raises `Replaced`: `game.LocalPlayer` and `GameWorld.MainPlayer`, the owner, `PlayerCameraController` (recreated on the new body), the effects controller and `PlayerHealthController.Start`. The battle UI and the view finalise over the next two or three frames. What stays your job is whatever your mod attached to the old pieces.

Camera: resolve at the point of use instead of caching, and re-check cheaply each frame if you must keep something attached to a camera. `PlayerCameraController.Camera` resolves to the game's current camera, not to a camera owned by that controller, so compare it with what you attached to, as the snippet does:

```csharp
using EFT;
using EFT.CameraControl;
using UnityEngine;
using YourMod.Interop;

public sealed class CameraFollower : MonoBehaviour
{
    private Camera _attachedTo;

    private void Update()
    {
        Player body = LocalBodyTracker.Instance != null ? LocalBodyTracker.Instance.Current : null;
        if (!LocalBodyTracker.IsCurrentLocalBody(body)) return;

        PlayerCameraController controller = body.GetComponent<PlayerCameraController>();
        if (controller == null) return;
        Camera camera = controller.Camera;
        if (camera == null || ReferenceEquals(camera, _attachedTo)) return;

        _attachedTo = camera;
        // Re-attach your effect, component or listener to `camera` here.
    }
}
```

A camera you cached before the change is destroyed at the end of the replacing frame. One you read inside your `BodyChanged` handler survives as long as nobody else takes over the `PlayerCameraController` (a freecam or spectator mod can); the per-frame check above covers both cases.

<a id="event-subscriptions"></a>
HUD and events: move subscriptions from the old body's controllers to the new body's in `BodyChanged`:

```csharp
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;

private IHealthController _hooked;

private void OnBodyChanged(Player previous, Player current, string reason)
{
    if (_hooked != null)
    {
        _hooked.HealthChangedEvent -= OnHealthChanged;
        _hooked.DiedEvent -= OnDied;
        _hooked = null;
    }
    if (current != null && current.HealthController != null)
    {
        _hooked = current.HealthController;
        _hooked.HealthChangedEvent += OnHealthChanged;
        _hooked.DiedEvent += OnDied;
    }
    // Rebuild what your HUD shows for the new body here.
}

private void OnHealthChanged(EBodyPart part, float diff, DamageInfo info) { }
private void OnDied(EDamageType type) { }
```

<a id="hooks-that-run-on-creation"></a>
The same applies to everything else that hangs off the old body: `Profile`, `Skills`, `InventoryController`, `Player.Init` hooks (a hook on creation runs again for every replacement, not once per raid) and any enemy or bot-memory list keyed by the old body or its `ProfileId`.

**What can go wrong.**

- Subscribing once at raid start: the old controllers belong to the corpse and nothing ever arrives from the new body.
- Throwing from vanilla events inside the replacement (`OnPersonAdd`, creation of `PlayerCameraController`): such an exception aborts the replacement after the old body is torn down. These handlers must never throw; wrap them in try/catch.
- Patching `Player.Init` or a creation method and assuming it runs once per raid. It runs for every replacement.
- Caching the local `Player`, the camera or the owner in statics.

<a id="mods-that-hold-death"></a>
## 8. Mods that hold death (downed and revive mods)

This is the recipe for mods that keep a body alive instead of letting it die: critical or downed states, bleed-out timers, revive by a team mate or an item. The usual mechanism is a prefix on `ActiveHealthController.Kill` that returns `false`.

What follows is derived from reading Core's code and the common downed-state pattern. It has not been run natively with a downed-state mod and not with Fika (Core idles under Fika). Where a statement rests on that, it says so.

**Do these six things, in this order.**

1. Add the tracker once and subscribe to it from your feature (recipe 1, and `Bind` in [8.5](#8-5)).
2. In `BodyChanged` with `previous != null`, end the downed state of `previous` and drop all per-life state ([8.3](#8-3), [8.4](#8-4)).
3. End the downed visuals and input lock when the death goes through, not only at the body change ([8.2](#8-2), [8.5](#8-5)).
4. Reset raid state from one idempotent `ResetRaid()` and keep the raid-boundary hooks you already have (recipe 6).
5. Triage your `IsYourPlayer` checks so per-tick logic stops on corpses, without breaking death-time handlers ([recipe 2](#isyourplayer-triage)).
6. Decide lives per raid or per life ([8.8](#8-8)).

Keep your own state model, `Kill` prefix and bot-ignore code. The sketch in 8.5 shows only where the calls go.

<a id="8-1"></a>
### 8.1 What Core sees and what it does not

- While your prefix keeps the body alive, **Core sees nothing**. No `DeathClaimed`, no `Replaced`. `IsRespawnPending` is false for the whole downed phase, because it requires a dead body.
- Core acts only after your mod lets a death through: bleed-out, give-up, out of lives, instant-kill rules. From then on the vanilla death runs and Core follows its normal sequence.
- Your downed state is therefore invisible to Core, and Core's replacement is invisible to your state unless you listen to `BodyChanged`.
- A revive keeps the same body. Core sees nothing, there is no `BodyChanged`, and a revive needs no handling for Core.

<a id="8-2"></a>
### 8.2 The sequence

1. A lethal hit reaches `Kill`. Your prefix declines and downs the player.
2. Downed phase. The body is alive. `IsRespawnPending(body)` is false.
3. Your mod decides the player dies (bleed-out timer, give-up, no lives left) and lets `Kill` through.
   3b. At this point your downed visuals and input lock must already be gone. The driver's prompt and cooldown can be on screen for many seconds after the death and need a free cursor and working UI input; a lock or HUD that outlives the death fights them. If the player gives up at the prompt there is no `BodyChanged` for that death at all.
4. Vanilla: `IsAlive` becomes false, the death event fires, `Player.OnDead` runs (your postfixes; there `IsRespawnPending` is the prediction of recipe 3).
5. Core's prefix on `BaseLocalGame<EftGamePlayerOwner>.CG_Spawn` raises `DeathClaimed(body, driverName)` if a driver wants this death.
6. The driver acts (Redeploy shows its respawn prompt; ArenaMode follows its mode rules). The player may give up here: a Killed Stop follows and the raid ends, `RaidEnded` after.
7. On success: `Replaced(old, new, "death")` and with it `BodyChanged(old, new, "death")`.

A second path ends the downed state **without** your mod letting anything through: a living body can be replaced while it is downed. ArenaMode does this for kit swaps and round breaks, and the reason is `"swap"`, with the old body alive. Core does not know it was downed.

<a id="8-3"></a>
### 8.3 What to reset, and when

Split "downed state" into two events:

- **(a) The death goes through**: your allow-death latch is consumed, or an `OnDead` patch fires for the body you held. End every visual and input effect now. Keep only the identity record.
- **(b) `BodyChanged`**: drop the per-life record and the entries keyed by `ProfileId`. This is also your safety net for effects you did not end at (a).

| Event | What to do |
|---|---|
| The death goes through (a) | End the downed visuals, input lock, HUD, god mode and bot-ignore entry of the body you held. |
| `BodyChanged` with `previous != null`, reason `"death"` | Drop the per-life record. End the downed state of the old body if it is still recorded as downed (safety net). |
| `BodyChanged` with `previous != null`, reason `"swap"` | End the downed state on the old body, which is alive: timers, god mode, bot-ignore lists, input locks, HUD, coroutines. Nothing of it may carry onto the new body. |
| `BodyChanged` with `previous != null` and a different `ProfileId` | Also initialise as at raid start (recipe 5). |
| `BodyChanged` with `current == null`, `RaidEnded`, or your own raid-end hook | The raid is over: call your one idempotent `ResetRaid()` (recipe 6). |

"End the downed state" means every effect your mod attached for that downed phase:

- the **timers and coroutines** that will eventually kill or revive the body. A timer that is not stopped fires later against `GameWorld.MainPlayer`, which is the new body: it kills a healthy player;
- **god mode** or invulnerability (see 8.7);
- **bot-ignore entries and bot memory** (8.6);
- **input locks** and anything that disables movement, shooting or the inventory;
- **HUD** elements for the downed state;
- the one-shot **"allow the next death" latch**. Keyed by `ProfileId` it stays set into the next life and the first lethal hit kills without downing.

<a id="8-4"></a>
### 8.4 Minimal change for an existing ProfileId-keyed store

Many mods already hold a static per-raid dictionary keyed by `ProfileId` with dozens of call sites. Rewriting them to instance keys is cleaner but not required. Resetting the `ProfileId` entry on every change with a previous body is enough:

```csharp
tracker.BodyChanged += (previous, current, reason) =>
{
    if (previous != null) Store.EndAndRemove(previous.ProfileId);   // per-life: state, kill override, timers, coroutine handles, cycle ids
    if (previous == null || current == null) Store.ClearRaid();     // per-raid: lives, raid generation
};
```

`EndAndRemove` must stop the coroutines and timers held in the entry before it removes the entry. If per-raid values (lives) sit in the same entry, reset the per-life fields and keep the entry instead of removing it. This handler covers the body change only: end the visuals at the death as in 8.3 (a).

<a id="8-5"></a>
### 8.5 Structure sketch (adapt, do not paste)

Keep your own state model, `Kill` prefix and bot-ignore code; the sketch shows only where the calls go. The mod-specific parts (how bots ignore, how input is locked, what the HUD shows) are left as comments. `AttachFresh`/`Detach` are not needed here because the state lives in one component on the plugin's GameObject.

The plugin owns the tracker and wires the feature, so there is no static lookup and no ordering requirement:

```csharp
// In YourModPlugin.Awake, after the tracker is added:
_bodies = gameObject.AddComponent<LocalBodyTracker>();
_downed = gameObject.AddComponent<DownedFeature>();
_downed.Bind(_bodies);
```

```csharp
using System.Collections;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using UnityEngine;
using YourMod.Interop;

namespace YourMod
{
    internal sealed class DownedState
    {
        public Player Body;               // the instance that is downed: the identity of this state
        public string ProfileId;          // read once; used only for lists keyed by it
        public float BleedOutAt;
        public bool AllowDeath;           // one-shot latch consumed by the Kill prefix
        public bool EffectsEnded;         // visuals and locks are gone; the record waits for BodyChanged
        public float SavedDamageCoeff;
    }

    internal sealed class DownedFeature : MonoBehaviour
    {
        internal static DownedFeature Instance;       // for the Kill patch

        private const int StartingLives = 2;
        private static readonly bool LivesPerRaid = true;   // see 8.8: choose on purpose
        private const float BleedOutSeconds = 60f;

        private LocalBodyTracker _tracker;
        private DownedState _state;                    // at most one: the local body
        private int _livesLeft = StartingLives;
        private readonly Dictionary<string, Player> _botIgnore = new Dictionary<string, Player>();   // ProfileId -> the downed instance

        private void Awake()
        {
            Instance = this;
        }

        internal void Bind(LocalBodyTracker tracker)
        {
            _tracker = tracker;
            tracker.BodyChanged += OnBodyChanged;
            tracker.RaidEnded += ResetRaid;            // Core only; recipe 6 for the other signals
        }

        private void OnDestroy()
        {
            if (_tracker != null)
            {
                _tracker.BodyChanged -= OnBodyChanged;
                _tracker.RaidEnded -= ResetRaid;
            }
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        // ---- the Kill prefix (Harmony) calls this; return false to keep the body alive ----
        internal bool OnKill(ActiveHealthController controller)
        {
            Player main = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
            if (main == null || !ReferenceEquals(main.ActiveHealthController, controller))
                return true;                            // a bot or a retired body: vanilla

            if (_state != null)
            {
                if (_state.EffectsEnded) return true;   // a death was already let through: vanilla
                if (!_state.AllowDeath) return false;   // already downed: further lethal hits change nothing
                _state.AllowDeath = false;
                EndDownedEffects(_state);               // the death goes through now: visuals and locks end now (8.3 a)
                return true;
            }

            if (_livesLeft <= 0) return true;
            _livesLeft--;
            EnterDowned(main);
            return false;
        }

        private void EnterDowned(Player body)
        {
            var state = new DownedState
            {
                Body = body,
                ProfileId = body.ProfileId,
                BleedOutAt = Time.time + BleedOutSeconds,
            };
            // Capture before zeroing; never keep a captured 0 (8.7).
            float coeff = body.ActiveHealthController != null ? body.ActiveHealthController.DamageCoeff : 1f;
            state.SavedDamageCoeff = coeff > 0f ? coeff : 1f;

            _state = state;
            _botIgnore[state.ProfileId] = body;
            // Lock input, show the HUD, apply god mode (8.7), tell bots to look away ...
            StartCoroutine(BleedOut(state));
        }

        private IEnumerator BleedOut(DownedState state)
        {
            while (Time.time < state.BleedOutAt)
            {
                if (!ReferenceEquals(_state, state) || state.EffectsEnded) yield break;   // ended: death, swap or raid end
                yield return null;
            }
            if (!ReferenceEquals(_state, state) || state.EffectsEnded) yield break;
            Die(state);
        }

        /// <summary>Own give-up button and bleed-out both end here.</summary>
        internal void Die(DownedState state)
        {
            // The body this state was created for, never "whatever is the local player now".
            if (state.Body == null || state.Body.HealthController == null || !state.Body.HealthController.IsAlive) return;
            state.AllowDeath = true;
            state.Body.ActiveHealthController.Kill(EDamageType.Undefined);
        }

        private void OnBodyChanged(Player previous, Player current, string reason)
        {
            if (previous != null) EndDownedState();                // "death" and "swap" alike; safety net after 8.3 (a)

            if (previous == null || !LivesPerRaid) _livesLeft = StartingLives;
            bool newIdentity = previous != null && current != null && previous.ProfileId != current.ProfileId;
            if (newIdentity) _livesLeft = StartingLives;           // a different profile: as at raid start

            if (current == null) ResetRaid();                      // works with and without Core
        }

        /// <summary>One idempotent raid reset for every raid-end signal (recipe 6).</summary>
        private void ResetRaid()
        {
            EndDownedState();
            _livesLeft = StartingLives;
            _botIgnore.Clear();
        }

        private void EndDownedState()
        {
            DownedState state = _state;
            if (state == null) return;
            _state = null;                    // every timer compares against this field and stops
            EndDownedEffects(state);
            StartCoroutine(RestoreNextFrame(state));   // touches the old body: never inside the handler (recipe 4)
        }

        /// <summary>Flags and lists only, so it is safe inside a BodyChanged handler.</summary>
        private void EndDownedEffects(DownedState state)
        {
            if (state.EffectsEnded) return;
            state.EffectsEnded = true;
            Player ignored;
            if (_botIgnore.TryGetValue(state.ProfileId, out ignored) && ReferenceEquals(ignored, state.Body))
                _botIgnore.Remove(state.ProfileId);
            // Unlock input, hide the HUD, clear whatever else the downed phase set on this body.
        }

        private IEnumerator RestoreNextFrame(DownedState state)
        {
            yield return null;
            RestoreDamageCoeff(state);        // 8.7: on the old body only, and only if it still lives
        }

        private static void RestoreDamageCoeff(DownedState state)
        {
            Player body = state.Body;
            if (body == null || body.HealthController == null || !body.HealthController.IsAlive) return;
            ActiveHealthController controller = body.ActiveHealthController;
            if (controller != null && state.SavedDamageCoeff > 0f) controller.SetDamageCoeff(state.SavedDamageCoeff);
        }

        /// <summary>For your bot patches: true only for the very instance that is downed, never for a later body.</summary>
        internal bool IsBotIgnored(IPlayer player)
        {
            Player ignored;
            return player != null && _botIgnore.TryGetValue(player.ProfileId, out ignored) && ReferenceEquals(ignored, player);
        }
    }

    [HarmonyPatch(typeof(ActiveHealthController), nameof(ActiveHealthController.Kill))]
    internal static class KillPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ActiveHealthController __instance)
        {
            DownedFeature feature = DownedFeature.Instance;
            return feature == null || feature.OnKill(__instance);
        }
    }
}
```

Notes on the sketch:

- A `Die` call from the HUD (a give-up button) is a normal call from your own code, not from inside a Core raise. That is safe.
- The rule it shows: every visual and input effect ends when the death goes through (the `OnKill` branch that returns true for `AllowDeath`), and `BodyChanged` drops the per-life record as a safety net. If your mod lets deaths through some other way, call the same end routine from a `Player.OnDead` patch on the body you held.
- `IsYourPlayer` is not used anywhere in the sketch. If your own patches use it as a "local human, not a bot" filter, see the triage table in recipe 2.

<a id="8-6"></a>
<a id="bot-related-state"></a>
### 8.6 Bot-ignore and bot memory

A ghost-style implementation does more than hold a `ProfileId` set: it can edit bot group membership, bot enemy memory (the goal or last enemy pointing at the player) and re-sweep every few seconds. Order matters:

1. Call your exit routine on `previous` first: restore the groups, remove the `ProfileId` from the ignore set, cancel the re-sweep for it. Only then do anything for `current`.
2. The `ProfileId` is the same, so an entry keyed by it matches the new body at once. Remove it before the new body is exposed and do not re-add it from a cached `Player`.
3. Bot enemy memory that holds a reference to the old `Player` instance still points at the corpse or the retired body. Refresh or clear it on that old instance.
4. Do not re-add the old instance to bot groups or enemy lists when you end the ghost state after a replacement: clear your flag and your group bookkeeping only. Core has already removed the old body from the bots and registered the new one with them. A lookup of a living player by `ProfileId` now finds the new body, so an exit routine that "restores" by `ProfileId` acts on the wrong body.

What bots do after the replacement is vanilla behaviour and has not been observed natively with every AI mod. Per-bot caches kept by third-party AI mods are untested.

<a id="8-7"></a>
### 8.7 DamageCoeff and spawn protection

If your god mode or damage scaling uses `ActiveHealthController.SetDamageCoeff` (a coefficient of 0 also suppresses the game's stun and pain effects, which a flag in a damage patch does not), keep the original value per downed state, never in a static shared by all bodies, and note how Core's spawn protection (applied by Redeploy after every respawn; ArenaMode uses the same routine) treats the same value on the new body: it captures the body's current coefficient (falling back to 1 if it reads 0 or less), sets it to 0, and writes its captured value back when the protection ends. It starts right after `Replaced`, in the same frame.

So:

- **Do not leave the new body's `DamageCoeff` at 0.** If your downed state ends on the old body, restore the old body's coefficient on the old body, and touch nothing on the new one.
- **Do not capture "the original coefficient" in a deferred frame from the new body.** From the end of the replacing frame until the protection ends it reads 0. Capture before you change it, as `EnterDowned` does in the sketch, with the same 0-means-1 rule; never keep a captured 0.
- **Do not rely on the value of the new body's coefficient during the protection window** and do not count damage that never lands as a lost life.
- Two writers on one field is the hazard. If your mod has zeroed the coefficient of a body when Core's protection starts on it, Core reads 0, falls back to 1 and later writes 1 back, not your value.

<a id="8-8"></a>
### 8.8 Lives: per raid or per life

Both are legitimate. Decide explicitly and write it in your README, because the behaviour with respawns differs:

- **Per raid.** The counter survives respawns. A player with two lives who respawns after the first death has one left. Reset it at raid start and in `ResetRaid()`, never at `BodyChanged` with a previous body. This is the sketch's default (`LivesPerRaid = true`).
- **Per life.** The counter resets on every `BodyChanged` with a previous body: every respawn is a new set of lives.
- In both cases, after an own-Scav continuation (different `ProfileId`) initialise as at raid start (recipe 5).
- Spawn protection by a Core driver means a fresh body usually takes no damage for a while. Do not spend a life on a hit that was scaled to nothing.

<a id="8-9"></a>
### 8.9 Give-up flows

Two give-up decisions exist and they are independent:

1. **Your mod's give-up** (the player presses your "give up" while downed): call `Die` as above, which sets your one-shot latch and calls `Kill`. From here it is an ordinary death: Core may claim it, and Redeploy may then show its own prompt. The player can give up there too.
2. **The driver's give-up**: the player declines the respawn prompt (or the mode ends the match). Core does not replace the body; a Killed Stop follows and the raid ends. There is no `Replaced`; `RaidEnded` arrives at the end. Your downed effects are already gone because they ended at the death (8.3 a).

Consequences for your mod:

- The one-shot latch must be consumed by the `Kill` that it was set for, and cleared on every `BodyChanged` with a previous body and on raid end. A latch left set carries a free kill into the next life.
- Do not treat your own give-up as the end of the raid: a driver may respawn the player after it.
- Do not finalise your per-raid state in your `Kill` path. Use `ResetRaid()` from the signals in recipe 6.
- You cannot veto or stall Core's replacement and nothing in the API will let you: Core will never let a consumer hold or stall a respawn (see [API.md](API.md#what-the-contract-is-and-what-it-is-not)). If you need time for an animation, run it on the new body after `BodyChanged`.

<a id="8-10"></a>
### 8.10 Fika builds

Core idles under Fika: no claim, no events, `RunRespawn` is refused, `IsRespawnAvailable` is false and `IsRespawnPending` returns `null`. The details are in [API.md](API.md#availability-and-fika). A Fika-only build does not need the interop file. A build that serves both can keep the tracker: it degrades to polling, and `BodyChanged` then comes only from the `MainPlayer` poll: `"raid-start"`, any other change of `MainPlayer` (reason inferred from the old body's `IsAlive`) and a late `"raid-end"`. Behaviour of the sketch under Fika is untested.

<a id="8-11"></a>
<a id="server-side-mods"></a>
### 8.11 Server side: synthetic end-of-raid calls

Redeploy's own-Scav continuation ends the PMC raid on the server by calling SPT's match-end logic directly from C#, not through HTTP. Consequences for a server mod:

- A hook on the static routes for `/client/match/local/start` or `/client/match/local/end` sees only the client's real requests, not this call.
- A mod that patches `MatchCallbacks` or `LocationLifecycleService` sees the synthetic call too.

If your server mod keeps per-raid state (a lives counter, a "raid in progress" flag), decide which of the two points you hook and what a synthetic end means for it.

- **Ordinary respawn and swap:** no server call of any kind. A server-side per-raid lives counter keeps counting through respawns, so it stays in step with a client counter that is per raid (8.8).
- **Own-Scav continuation:** the exact sequence of server start and end calls it produces is not part of the contract. Do not build a counter reset on it without testing.

<a id="8-12"></a>
### 8.12 What can go wrong (summary)

- The old downed state keeps running: the timer kills the new body, god mode or an input lock stays on, bots stay blind.
- The downed HUD or input lock outlives the death and fights the driver's prompt.
- A `ProfileId`-keyed latch or ignore list survives into the next life.
- Gating per-tick logic on `IsYourPlayer`: it runs on the corpse. The opposite mistake, replacing every check with `IsCurrentLocalBody`, breaks death handlers.
- Capturing or restoring `DamageCoeff` across Core's spawn protection.
- Expecting Core to know about the downed phase, or to wait for it.
- Starting a respawn, stopping the raid or destroying a body from a `BodyChanged`, `DeathClaimed` or `ReplacementFailed` handler (refused or harmful: recipe 4).

<a id="do-you-need-an-adapter"></a>
## 9. Do you need an adapter? (almost never)

The supported way for another mod to follow the local body is to observe it: `Redeploy.Core.Api.RespawnApi`, consumed through the interop file. Everything else public in Core (`DeathInterception`, `RespawnEngine`, `RaidLifecycle`, the driver interfaces) is public for Redeploy's own drivers and is not for other mods.

Before asking for an adapter, check that you cannot do it from outside:

| Your need | Do this instead |
|---|---|
| A component on the player must follow the local body | Recipe 2. |
| Per-life state must reset | Recipe 4. |
| State keyed by profile must survive or change | Recipe 5. |
| Something hooked to the old camera, HUD or health controller | Recipe 7. |
| The work must happen after the new body is wired | `BodyChanged` runs after Core has wired the body (list in recipe 7). The battle UI and view finalise over the next two or three frames. Defer one frame for your own work, and re-check identity after the wait (recipe 4). |
| You need to veto, delay or reorder a respawn | Not available and not planned. Core never lets a consumer hold or stall a replacement. |

Core does not ship adapters for mods that can integrate themselves, and the cost of the exception is explained in [API.md](API.md#what-the-contract-is-and-what-it-is-not).

If your state really is unreachable from outside (private per-body objects your code cannot find from `BodyChanged`, and the fix must run before the new body goes live), open an issue on the [Redeploy repository](https://github.com/maschine34675/Redeploy/issues) with the use case: what the state is, why `BodyChanged` is too late or too early, and what breaks without it. The API is additive only; see [How to request an addition](API.md#how-to-request-an-addition).

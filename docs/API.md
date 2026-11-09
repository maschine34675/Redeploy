# Redeploy respawn API reference

This file is the contract: every member of `RespawnApi`, every guarantee Core gives to the mods that observe it, the order in which things happen, and what is not promised. It is written for authors of other BepInEx mods for SPT 4.1 whose mod must keep working when the local player's body is replaced mid-raid. How to integrate without a dependency (copying the interop file, compile requirements, availability tiers, keeping the copy current) is in [SOFT-DEPENDENCY.md](SOFT-DEPENDENCY.md). The checklist of what your mod must do is in [RULES.md](RULES.md), and worked examples with code are in [RECIPES.md](RECIPES.md).

Source of truth in the repository: [`Redeploy.Core/Api/RespawnApi.cs`](../Redeploy.Core/Api/RespawnApi.cs) for the surface, [`Redeploy.Core/Api/api-v1.baseline.txt`](../Redeploy.Core/Api/api-v1.baseline.txt) for the frozen member list that a test compares with every build, and [`interop/RedeployRespawnInterop.cs`](../interop/RedeployRespawnInterop.cs) for the consumer side.

## Start here

Most mods use only `LocalBodyTracker`, the class in the interop file. Read, in this order:

1. [The LocalBodyTracker surface](#the-localbodytracker-surface), including [what the API cannot tell you](#what-the-api-cannot-tell-you).
2. [Timeline of a death](#timeline-of-a-death) and [Timeline of the raid end](#timeline-of-the-raid-end).
3. [Dispatch guarantees](#dispatch-guarantees), which say what your handler may do.
4. [Identity](#identity).

If your mod vetoes `ActiveHealthController.Kill`, read [If your mod holds death](#if-your-mod-holds-death) first. Read [Reference: binding by hand](#reference-binding-by-hand) only if you bind to `RespawnApi` yourself or want the exact literals.

## Contents

Use the tracker:

- [What the contract is, and what it is not](#what-the-contract-is-and-what-it-is-not)
- [If your mod holds death](#if-your-mod-holds-death)
- [The LocalBodyTracker surface](#the-localbodytracker-surface)
- [Timelines](#timelines)
- [Dispatch guarantees](#dispatch-guarantees)
- [Identity](#identity)
- [Availability and Fika](#availability-and-fika)

Reference:

- [Binding by hand](#reference-binding-by-hand) and [the surface](#the-surface) (queries, events, reason and stage literals)
- [Versioning](#versioning)

## What the contract is, and what it is not

Some terms first. **Core** is `maschine-Redeploy.Core.dll`, the shared respawn engine; it is a library, not a BepInEx plugin. A **driver** is a mod that owns respawn policy and asks Core to replace the body: Redeploy (GUID `com.maschine.Redeploy`) and ArenaMode (GUID `com.maschine.ArenaMode`). The **requester** is the driver that asked Core for one particular replacement. A **consumer** is any mod that observes body changes (Killcam today; your mod). A **body** is an `EFT.Player` instance. A **replacement** is Core creating a new local body; the old body becomes the corpse (reason `"death"`) or a retired living body (reason `"swap"`).

Core checks every replacement request in a **preflight** before it tears anything down; a request that fails is **refused** (see [A refusal before teardown](#refusals)). To **claim** a death is to take it over so that the raid is not ended; while a death is claimed, Core holds the raid open. A **Killed Stop** is the game's `LocalGame.Stop` with `ExitStatus.Killed`, which ends a raid after an ordinary death.

**Third parties observe.** The supported surface is the static class `Redeploy.Core.Api.RespawnApi`, consumed through the interop file. It lets you ask four questions and listen to four events. It cannot change what Core does.

- There is no way for a consumer to veto, delay or stall a respawn, and there never will be. A handler that throws is logged and ignored; a handler that blocks only blocks the frame it runs in.
- Core ships no adapter for a mod that can integrate itself. Core carries an adapter for another mod only when that mod's per-body state cannot be reached from outside and the fix has to run inside the replacement. That has a cost: if such an adapter's contract with the other mod drifts, Core refuses the replacement before teardown. Everyone else integrates through the interop file.
- The API is additive only. See [Versioning](#versioning).

<a id="what-is-not-contract"></a>
**Not contract.** These types and members are public because Redeploy's own drivers and ArenaMode are compiled against them, and Killcam binds some of them by name. They are not a surface for other mods, they may change in any Core release, and nothing in this document promises them:

- `DeathInterception`, `IDeathInterceptionDriver`, `ISpawnSelector`. The two interfaces are frozen only because the compiled Redeploy and ArenaMode drivers implement them. Third-party drivers are not supported.
- `RespawnEngine`, `RespawnEngine.RunRespawn`, `RespawnRequest`, `SpawnProtectionOptions`, `RespawnEngineState`.
- `RaidLifecycle` and its events `OnRaidStarted` and `OnRaidEnded`.
- The legacy events `DeathInterception.OnDeathIntercepted`, `RespawnEngine.OnPlayerRespawned` and `RespawnEngine.OnSpawnProtectionEnded`.
- `RedeployCore` and everything on it, including `RedeployCore.Version` (see [Versioning](#versioning)).
- Anything internal to the assembly (`CoreSignals` and `RespawnClaims` among it), the spawn selectors, the compatibility helpers and the order of work inside the replacement pipeline beyond what the [timelines](#timelines) state.
- The order in which subscribers of one event are called.
- The values of `driverName` beyond "a non-empty string naming the driver". Today they are `"Redeploy"` and `"ArenaMode"`; do not branch on them for correctness.

If you need something the contract does not offer, see [How to request an addition](#how-to-request-an-addition).

## If your mod holds death

A downed-state or revive mod that vetoes `ActiveHealthController.Kill` (its prefix returns `false`) needs nothing from Core to do that. Core installs no patch on `Kill`, `ApplyDamage` or `Player.OnDead`, so there is no Harmony ordering between your veto and Core, and Core sees nothing while you hold the death. It claims a death only after your mod lets it through, and from then on the ordinary [timeline of a death](#timeline-of-a-death) applies.

Your downed state can also sit on a living body that gets replaced (an ArenaMode kit swap or round break): see [Timeline of a swap](#timeline-of-a-swap). Read, in this order: [Timeline of a death](#timeline-of-a-death), [Timeline of a swap](#timeline-of-a-swap), [Identity](#identity) (`ProfileId` and `IsYourPlayer` on the corpse and the new body) and [Timeline of the raid end](#timeline-of-the-raid-end) (where to reset). Do not base the decision to hold a death on Core: see [what the API cannot tell you](#what-the-api-cannot-tell-you). The checklist is [RULES.md](RULES.md#mods-that-hold-death); the worked code is in [RECIPES.md](RECIPES.md#8-mods-that-hold-death-downed-and-revive-mods).

## The LocalBodyTracker surface

You will normally not call `RespawnApi` yourself. You copy [`interop/RedeployRespawnInterop.cs`](../interop/RedeployRespawnInterop.cs) into your mod (setup is in [SOFT-DEPENDENCY.md](SOFT-DEPENDENCY.md)) and use `LocalBodyTracker`, a `MonoBehaviour` that you add once to your plugin's GameObject. It binds to Core by reflection when Core is installed and, whether or not it is, always watches `GameWorld.MainPlayer` as a fallback. The file is **interop revision 1**.

### Members

| Member | Meaning |
|---|---|
| `event Action<Player, Player, string> BodyChanged` | `(previous, current, reason)`. The one stream most mods need. |
| `event Action<Player, string> DeathClaimed` | Forwarded from Core. Core only. |
| `event Action<Player, string> ReplacementFailed` | Forwarded from Core. Core only. |
| `event Action RaidEnded` | Forwarded from Core. Core only. |
| `bool? IsRespawnPending(Player body)` | Core's query, or `null` when unknown (see the table below). |
| `bool IsRespawnAvailable` | Core's `IsAvailable`; `false` when unbound. |
| `bool IsBound` | The tracker is subscribed to Core's events. |
| `Player Current` | The body the tracker currently follows. |
| `static LocalBodyTracker Instance` | The first tracker of this plugin, for Harmony patches that need the queries. |
| `Action<string> Log` | Where the tracker writes. Default: Unity `Debug.Log` with the prefix `[RespawnInterop] `. Assign your own logger. |
| `const int Revision` | `1`. Changes only when the file changes, never with Core. |
| `const string ReasonRaidStart` `"raid-start"`, `ReasonDeath` `"death"`, `ReasonSwap` `"swap"`, `ReasonRaidEnd` `"raid-end"`, `ReasonUnknown` `"unknown"` | The reason values `BodyChanged` can carry. |
| `static bool IsCurrentLocalBody(Player body)` | `ReferenceEquals(MainPlayer, body) && body.HealthController.IsAlive`; `false` for `null`. For living bodies only; in a death handler use `ReferenceEquals(MainPlayer, body)` because a dead body fails `IsAlive`. Never use `IsYourPlayer` or `ProfileId` for this. |
| `static T AttachFresh<T>(Player body)` | Destroys a stale `T` on the body, then adds a new one. Player GameObjects come out of a shared pool and can carry a stale component. |
| `static void Detach<T>(Player body)` | Removes `T` from a body that stopped being the local body. Safe for `null` and destroyed bodies. A corpse keeps every component unless you remove it. |

### What `IsRespawnPending` returns

The tracker's query returns `bool?`, three states. Do not write `if (!tracker.IsRespawnPending(p))`; compare with `== true`.

| Value | Meaning |
|---|---|
| `true` | A Core driver is holding the raid open for this death (or, from `Player.OnDead` until `DeathClaimed`, would claim it right now). Never a promise. See [`IsRespawnPending`](#isrespawnpending) for the two regimes. |
| `false` | Core is active and will not replace this body. Not "final death": a respawn mod outside Core may still act. |
| `null` | Core cannot say: the tracker is unbound, Core is older than API v1, `IsRespawnAvailable` is `false` (Fika, no driver, patches incomplete), or the call threw. Behave as if Core did not exist and keep your vanilla logic. |

Passing `null` as the body means `game.LocalPlayer`, in the tracker as in the raw API. Pass the body explicitly.

### What the API cannot tell you

- **Whether a death that has not happened yet will be respawned.** `IsRespawnPending` needs a dead body and is `false` for a living one. `IsRespawnAvailable` says only that Core could replace a body. So the decision to hold a death, or to down the player at all, must not depend on Core: hold it as you do today and Core reacts after you let the death through.
- **Whether the driver will in fact replace after a claim.** A claim is not a promise.
- **The driver's prompt, cooldown or number of lives.**
- **`IsReplacing` and `ApiVersion`.** The tracker forwards neither; do not write your own reflection for them. There is no supported use of `IsReplacing` as a gate: it is `true` while `Replaced` handlers run, so a `Kill` prefix gated on it would be wrong, and on the success path it is never visible from another frame.

### `BodyChanged` semantics

- **Chained.** Each `current` becomes the next `previous`. At most one event per real change. `previous == null` always comes with `"raid-start"`; `current == null` always comes with `"raid-end"`.
- **Reasons.** `"death"` and `"swap"` come from Core's `Replaced`, or are inferred by the poll from the old body's `IsAlive`. `"raid-start"` is the first body the tracker sees. `"raid-end"` is the end of the raid. Treat any other reason like `"unknown"`.
- **Late attach.** If the tracker first learns of a body through `Replaced` (it was added after the raid started), it reports `(null, newBody, "raid-start")`, not a death or swap, to keep the chain rule. You never see the body it did not follow.
- **Delivery differs by source.** With Core, the handler runs inside the tracker's own handler of Core's `Replaced` event: mid-pipeline, before spawn protection, and the [dispatch rules](#dispatch-guarantees) for handlers apply. Without Core it runs from the tracker's `Update`. A handler cannot tell which, so apply the strict rules in every case: never start a respawn, stop the raid or destroy a body directly from a handler; defer such work by one frame. Core's dispatcher isolates and logs the tracker's own handler, but not yours: the tracker isolates yours, see [Dispatch guarantees](#dispatch-guarantees).
- **Poll, with Core.** While Core reports `IsReplacing`, the poll holds back for up to 35 seconds so that the exact `Replaced` event reports the change; after that it reports whatever `MainPlayer` is. If a `ReplacementFailed` arrives, the hold is released and the poll reconciles with `MainPlayer` as soon as `IsReplacing` is false.
- **After a failed replacement** the poll may report old body to an unwired orphan body, and orphan back to old if the requester recovers the old one. Both are real changes of `MainPlayer`. If the requester instead rescues the replacement, there is no `Replaced`, and the poll reports the change.
- **`"raid-end"`** is exact only with Core (from `RaidEnded`). Without Core it arrives late, when the `GameWorld` goes away.
- **Hideout and trader narrate scenes** have a `GameWorld` with a `MainPlayer`, so the poll reports `"raid-start"` for them, and `"raid-end"` when their world goes away. Core raises `RaidEnded` for neither. Filter them by the game type: only a `LocalGame` is a raid (hideout and narrate games are not, and under Fika neither is `CoopGame`). The code is in [RECIPES.md, recipe 6](RECIPES.md#6-raid-scoped-latches). The alternative is your own postfix on `GameWorld.OnGameStarted`, which Core uses itself. The hideout never calls it, but trader narrate scenes do, so filter it the same way (Core ignores a `NarrateGameWorld` there). Either way, do not assume that every `"raid-start"` is paired with a `RaidEnded`.

### Binding

`TryBind` runs in `Awake` and again whenever a new `GameWorld` appears while the tracker is unbound; a negative probe is never cached. It resolves the reflection name with `Type.GetType`, requires `ApiVersion >= 1`, and requires the `Replaced` event. `DeathClaimed`, `ReplacementFailed`, `RaidEnded`, `IsReplacing`, `IsAvailable` and `IsRespawnPending` are optional: a member that is missing or whose signature drifted is skipped on its own.

The tracker stays unbound (`IsBound` is `false`) and polls, without logging anything, when Core is absent, when `ApiVersion` is missing or below 1, or when the `Replaced` event is missing or has a different signature. Only an exception logs `binding failed`. Check `IsBound` rather than relying on the log line alone. The exact log lines and the installation states they identify are in [SOFT-DEPENDENCY.md](SOFT-DEPENDENCY.md#log-lines-to-look-for).

Early binding in `Awake` is untested natively; it does not affect you, see [SOFT-DEPENDENCY.md](SOFT-DEPENDENCY.md#setup).

## Timelines

Each timeline lists what happens in order and what is already true at that point. "Frame N" is the frame in which the replacement happens. On today's code everything inside frame N runs synchronously on the main thread; that is observed behaviour, not a promise.

### Timeline of a death

A death handled by Redeploy or ArenaMode.

1. **Vanilla death.** The health controller's `Kill` sets `IsAlive` to `false` and raises its death event. `Player.OnDead` runs, with its postfixes and the static `OnPlayerDeadStatic` handlers.
   - True: the body is dead, still `GameWorld.MainPlayer`, still registered, still has its owner and camera.
   - `IsRespawnPending(body)` is the prediction described under [`IsRespawnPending`](#isrespawnpending), not a fact.
   - Nothing from Core has been raised. If a mod vetoed `Kill` (see [If your mod holds death](#if-your-mod-holds-death)), none of this has happened yet.
2. **`DeathClaimed(body, driverName)`**, from Core's `CG_Spawn` prefix, once per claimed death.
   - True: everything from step 1; `body` is the dead local body (`game.LocalPlayer`, `MainPlayer`); `IsRespawnPending(body)` is `true` while the handlers run; the driver has not acted yet (no respawn prompt, no arena event).
   - Not guaranteed: that the game status is `Started`. ArenaMode can claim during `Stopping`, for example after a Team Deathmatch result.
   - Not a promise: see step 3.
3. **The driver decides**, taking from zero to many seconds: a prompt, a cooldown, a hold, a preload. During this time the raid stays open and `IsRespawnPending(body)` stays `true`, except in the known Team Deathmatch case described under [`IsRespawnPending`](#isrespawnpending). Outcomes: the driver asks Core to replace (step 4); the player gives up, the item is missing or the match ends, so the raid ends normally and a Killed Stop follows; or the request is refused (see [A refusal before teardown](#refusals)).
4. **The replacement runs in frame N.** Teardown first (old owner destroy is queued, old body unregistered, profile prepared), then the new body is created and wired. Game-owned observers fire inside this window, not isolated by Core: `GameWorld.OnPersonAdd` (at that moment `MainPlayer` is already the new body while `game.LocalPlayer` is still the old one), camera-controller creation events. A throw from one of those aborts the replacement after teardown.
5. **`Replaced(old, new, "death")`**, synchronously, after wiring and before spawn protection.
   - **Already true:**
     - `new` is `game.LocalPlayer`, `GameWorld.MainPlayer` and the owner's `MyPlayer`, and is registered under its ProfileId.
     - The new owner exists and has started; the death handler is attached to the new body; the player health controller is started; stale limp and recoil state from a pooled body is reset.
     - Bots no longer track the old body and know the new one.
     - A new `PlayerCameraController` and new cameras exist. The old controller is already destroyed. The old camera GameObject lives until the end of frame N, then dies before rendering. A camera you cached before the change is therefore gone; one you read inside this handler survives as long as nobody takes over the `PlayerCameraController` in the following frames. Re-resolve cameras after every change.
     - Effects, black screen and input are restored.
     - `IsRespawnPending(old)` is `false`.
     - `IsReplacing` is still `true`.
     - The old body is the corpse described under [reason `"death"`](#reasons).
   - **Not yet true:**
     - Spawn protection has not started. With Redeploy as driver it starts right after the handlers in the same frame, and it does this: it reads the new body's damage coefficient, sets it to `0`, and when protection ends restores the value it read (`1` if that was `0` or less), provided the body is still alive. Do not leave the new body's `DamageCoeff` at `0`, and do not rely on its value during protection.
     - The old owner object still exists until the end of frame N.
     - The requester's own continuation has not run; it comes in a later frame.
     - Final view clean-up (camera, battle UI) runs in the following frames.
   - **What your handler may do:** see the table under [Dispatch guarantees](#handlers-and-isolation).
6. **After the handlers**, in the same frame, Core raises its legacy event `RespawnEngine.OnPlayerRespawned` (not contract), starts spawn protection if the driver asked for it, and ends the call. `IsReplacing` becomes `false` at the synchronous tail.

### Timeline of a swap

A swap is the same pipeline with a living old body: an ArenaMode kit swap at raid start or a round break. There is no death, so there is no `DeathClaimed`, and `IsRespawnPending` is `false` throughout.

1. The driver asks Core to replace the body while `game.LocalPlayer` is alive. The reason is fixed at this moment: `IsAlive` is `true`, so `"swap"`.
2. Teardown and creation run exactly as in step 4 of the death timeline.
3. **`Replaced(old, new, "swap")`** with the same guarantees about `new` as for a death.
   - The old body is alive, unregistered, ownerless, without a death handler, and keeps its living health controller, colliders and any bleeding.
   - What happens to it is the requester's decision. ArenaMode disposes it no earlier than the following frame, which gives its components `OnDestroy` in the middle of the raid. If disposal is declined for some reason, the old body keeps living and may die later; then `OnDead` fires for a body that has `IsYourPlayer == true` but is not the current local body, and `IsRespawnPending` is `false`.
4. State that belongs to the old life must end on the old body, even though it was alive.

### A failed replacement

1. The request passed its preflight, so teardown has started and `IsReplacing` is `true`.
2. A stage fails: `"prepare"`, `"create"`, `"raid-ending"` or `"rewire"`.
3. Core waits one frame, then clears `IsReplacing`, marks the old body as resolved and, unless `RaidEnded` was already raised for this game, raises **`ReplacementFailed(old, stage)`**. Core then calls the requester's failure callback immediately, in the same frame. Whatever recovery the requester starts from that callback may therefore run in the same frame as your `ReplacementFailed` handler or later; do not assume the world is still as the failing stage left it after your handler returns.
   - True: `IsReplacing` is `false`; `IsRespawnPending(old)` is `false`.
   - Not true: any rollback. Depending on the stage, `MainPlayer` can be the old body, an unwired body or a partly wired new body.
4. Consumer action: drop cached bodies, and in the next frame read `GameWorld.MainPlayer` again. The tracker does this for you; it may report one or two `BodyChanged` events for the changes it sees. Do not end or start per-life state on `ReplacementFailed` itself: treat the tracker's resulting `BodyChanged` (or your next-frame `MainPlayer` read) as the body change. If `MainPlayer` is the old body again, the tracker reports nothing and your state for that body is still valid; it never saw a replacement.
5. Never raised for the same request after `Replaced`, even if something throws later.

<a id="refusals"></a>
### A refusal before teardown

A request is refused with nothing changed when the preflight finds that it cannot succeed: the request, game or spawn selector is missing; Core is unavailable; the raid is already ending; the call comes from inside a `DeathClaimed`, `Replaced` or `ReplacementFailed` handler; or a pipeline is already running. After the preflight, validation of a replacement profile and per-mod compatibility preparation also run before any teardown and can refuse in the same way.

**A refusal raises no public event.** So a consumer can see `DeathClaimed` followed by neither `Replaced` nor `ReplacementFailed`. The driver then handles the death in its own way, usually by letting the raid end, and `RaidEnded` follows at the end of the raid. Do not read anything into `IsRespawnPending` after a refusal; its value is not part of the contract and unproven.

### Timeline of the raid end

1. The raid stops. A death screen, fades and a `Stop` may come first; none of them is a raid-end signal for you (see below).
2. The game's `GameEnd` completes its synchronous part. `GameEnd` is `async`.
3. Core's `GameEnd` postfix runs. Order: the drivers reset their per-raid state, Core clears its own (so `IsReplacing` is `false`, no claim is held, no last-body reference), Core marks this game as ended, its legacy raid-end event `RaidLifecycle.OnRaidEnded` is raised (not contract), and then **`RaidEnded`** is raised.
   - True: the raid is over for the game's purposes. No pipeline event is raised for this game any more (`ReplacementFailed` is suppressed). Drop body references and touch nothing.
   - If the synchronous part of `GameEnd` threw, the postfix still runs and `RaidEnded` still fires, without the usual guarantee that the clean-up finished. Core logs that case.
4. A request coroutine may outlive its raid. Its `RunRespawn` call is refused by the preflight because the raid is tearing down.
5. A raid whose `GameEnd` never ran is cleaned up quietly at the next raid start. There is no synthetic `RaidEnded` for it.

`RaidEnded` is not raised for the hideout, trader narrate scenes or Fika's `CoopGame`. If you bind late, nothing already raised is replayed.

**What is not a raid-end signal:** a prefix on `LocalGame.Stop` or `BaseLocalGame.Stop`, `OnDestroy`, `UnregisterPlayer` (vanilla calls it one frame after every death), an unspawn, and a local death. Core blocks the Killed `LocalGame.Stop` (the override) while it holds a death. A prefix on `BaseLocalGame<EftGamePlayerOwner>.Stop` therefore does not run then, because the override never reaches the base method; a prefix on `LocalGame.Stop` itself depends on Harmony ordering that Core does not control. Keep such a hook only as an extra, idempotent reset.

A prefix on `GameWorld.Dispose` fires when a world is torn down, for raids, the hideout and narrate scenes alike. Core makes no promise about its order relative to `RaidEnded`. It is a backstop for freeing resources, not an exact raid-end signal and not a per-life boundary.

**Which signal marks which boundary:**

| Boundary | Signal |
|---|---|
| Per-raid start | No Core event. The tracker's `BodyChanged` with `"raid-start"` (filter hideout and narrate worlds, see [`BodyChanged` semantics](#bodychanged-semantics)), or your own postfix on `GameWorld.OnGameStarted` with the same filter (narrate scenes call it too). |
| Per-life boundary | Core's `Replaced`, or the tracker's `BodyChanged` with a non-null `previous` and a non-null `current`. |
| Raid end | Core's `RaidEnded` (exact, `LocalGame` raids only), the tracker's `BodyChanged` with `current == null`, or your own `BaseLocalGame.GameEnd` postfix filtered to `LocalGame`. |

<a id="handlers-and-isolation"></a>
## Dispatch guarantees

These hold for `DeathClaimed`, `Replaced`, `ReplacementFailed` and `RaidEnded` as Core raises them, that is, for the handlers Core calls itself. Handlers you attach to the tracker are one step removed; see "Handlers on the tracker" below.

- **Main thread.** Events are raised on Unity's main thread. Queries belong there too.
- **Isolation.** Each subscriber runs in its own `try`/`catch`. A throwing handler cannot silence the handlers after it, and nothing propagates into the replacement or the damage call stack. The failure is logged as a warning on the `Redeploy.Core` log source:
  ```
  <EventName> subscriber <Namespace.Type.Method (assembly)> failed: <exception>
  ```
  It names the failing handler, so a bug report can be traced to its mod.
- **Handlers on the tracker.** Core's dispatcher isolates and logs the tracker's own handler. The handlers you attach to the tracker's events are isolated by the tracker: each runs in its own `try`/`catch` over a snapshot of the list, and a failure is written through `LocalBodyTracker.Log` as `BodyChanged subscriber failed: <exception>` (likewise `DeathClaimed`, `ReplacementFailed` and `RaidEnded subscriber failed`), not as Core's warning above.
- **Snapshot.** The list of subscribers is copied before the first call. Subscribing or unsubscribing from inside a handler takes effect from the next raise, not the current one.
- **Destroyed targets.** Core skips a handler whose target is a destroyed `UnityEngine.Object`: a handler on a destroyed `MonoBehaviour` is a live C# object, so Core checks Unity's overloaded equality. Static methods and handlers on plain C# objects are never skipped. The tracker does not skip handlers on destroyed `MonoBehaviour`s, so unsubscribe in `OnDestroy`.
- **Order is unspecified.** Subscribers are called in the order they subscribed, which depends on when each mod subscribes and is not under your control. There are no priorities. Never depend on running before or after another mod.
- **Nesting is allowed.** An event raised while a handler runs, for example the death of the new body inside a `Replaced` handler, is dispatched and isolated on its own.
- **Re-entrancy refusal.** A replacement request made from inside a `DeathClaimed`, `Replaced` or `ReplacementFailed` handler is refused before teardown. (A request from a `RaidEnded` handler is refused because the raid is tearing down.) This applies to drivers; as an observer you do not make requests.
- **Defer by one frame.** If you need something from the "defer" row below, start a coroutine, `yield return null`, then re-check that the body you meant is still the one you want (`ReferenceEquals(GameWorld.MainPlayer, body)`) and do it. The same rule applies to every `BodyChanged` handler of the tracker, because a handler cannot tell whether it runs from `Replaced` or from the poll.
- **Observers outside Core are not isolated.** Core isolates only its own events. The game's `GameWorld.OnPersonAdd`, patches on `Player.Init` and the static `PlayerCameraController` events fire in the middle of the replacement, in a half-wired state. A throw there aborts the replacement after teardown (stage `"create"` or `"rewire"`). Handlers on those must never throw, and a prefix that skips one of the game methods the replacement calls for the new body blocks it in the same way.
- **Hooks on creation run again.** Anything you hook on `Player.Init` or player creation runs for every replacement, not only at raid start.

**Inside a `Replaced`, `BodyChanged` or other Core event handler:**

| | What |
|---|---|
| Safe | Read fields and properties of either body. Set your own state. Subscribe or unsubscribe. `Detach<T>` and `AttachFresh<T>` on the two bodies. Read cameras (a camera read here survives, see the death timeline). |
| Defer by one frame | `LocalGame.Stop` and `InitiateGameStopping`. Destroying either body. Killing the new body (spawn protection is not active yet). |
| Never | Starting a respawn request (it is refused). Touching the old owner or the old camera (both are queued for destruction at the end of frame N). |
| Not verified | Calling `HealthController` or `Player` methods on the old body, for example to remove effects on a swapped body that is still alive and bleeding. Defer them when unsure, and check the body is still the one you meant. |

## Identity

Ordinary respawns and swaps keep the ProfileId: old and new body share one profile, so `previous.ProfileId == current.ProfileId`.

Redeploy's optional own-Scav continuation (Redeploy 1.1.0, Core 1.0.17) replaces the dead PMC with the player's Scav profile. The new body has a **different ProfileId**. The reason is still `"death"`; there is no flag. To detect it, compare `previous.ProfileId` with `current.ProfileId`. Treat it as a new identity: initialise state as you would at raid start.

Consequences:

- Never use `ProfileId` to decide whether a body is the current local body. It is shared by the old and new body of a respawn, and it can change in an own-Scav continuation. Use `ReferenceEquals(MainPlayer, body)`, and `IsAlive` for living bodies.
- Never use `IsYourPlayer` for it either. Both the corpse and the new body report `IsYourPlayer == true`; only `MainPlayer` identifies the living one. A scan of players for `IsYourPlayer` can return the corpse if you hold a list of your own: the corpse is unregistered from the world, so a scan over the world's registered players does not see it.
- State that belongs to one life should be keyed by the `Player` instance, or reset on every body change. Player objects come from a shared pool, so do not rely on an instance key alone either: remove the entry on that change. State keyed by ProfileId survives into the next life with the same ProfileId. A set of ProfileIds that bots ignore, a dictionary of per-player state, an "allow the next death" latch: each one applies to the new body the moment it appears, because the id is the same. Clear or re-key it on every `BodyChanged` that has a previous body.
- A registry keyed by ProfileId is overwritten by the new body; remove an entry on unspawn only if the stored instance is the body that went away.

### What the new body is

- A new `Player` with its own health controller, inventory controller, hands controller and movement context. Subscriptions on the old body's objects stay with the old body ([RULES.md](RULES.md#rule-14), rule 14).
- `Profile` and `Skills` are the same objects after an ordinary respawn or swap, and different objects after an own-Scav continuation.
- Equipment and health are the driver's choice, not Core's. Redeploy: no armour, firearms or carried loot; the secure container, melee weapon, armband and pocket special slots move to the new body and everything else stays on the corpse; health is set from the player's `RespawnHealthPercent` setting. Own-Scav continuation: the Scav's own equipment. ArenaMode: the kit of the current mode.
- During spawn protection Core may empty the new body's hands to stow an automatically drawn weapon, when the driver asks for that.

## Availability and Fika

`RespawnApi.IsAvailable` is the conjunction of three conditions, evaluated in this order and stopping at the first that fails:

1. The keep-alive patches are all installed. Core installs six patches that let a raid survive a player's death, on `BaseLocalGame<EftGamePlayerOwner>.CG_Spawn` (the game's death handler) and `InitiateGameStopping`, `LocalGame.Stop`, `PreloaderUI.StartBlackScreenShow`, `SessionResultExitStatus.Show` and `GamePlayerOwner.CleanupOnDestroy`, plus the hook on `BaseLocalGame.GameEnd` that reports the raid end. If any of these seven is missing, Core stands down, because a claimed death could otherwise end half-handled. A missing raid-start hook (a postfix on `GameWorld.OnGameStarted`) is logged but does not affect availability. Core installs further patches for compatibility and statistics; the list is given so that you can see overlaps with your own patches, and is not frozen.
2. At least one driver is registered. A Core with no driver has nothing to serve.
3. Fika is not loaded. The probe checks for the plugin `com.fika.core`.

The order matters: a plain installation never pays for the Fika probe, and a probe that throws cannot flip a Core that has no driver anyway.

**The Fika check latches only a positive answer.** Once Fika has been seen, it stays seen. A negative answer is not final, because the list of loaded plugins fills up while the chainloader runs: "not loaded" asked from an early `Awake` may only mean "not yet", so it is asked again. The consequence for you: a `false` from `IsAvailable` read before the chainloader has finished is not final.

When `IsAvailable` is `false`, Core is idle: it claims no death, blocks nothing, refuses every replacement request, and raises no `DeathClaimed`, `Replaced` or `ReplacementFailed`. `RaidEnded` depends only on the raid-end hook being installed and can still arrive; under Fika it does not, because a Fika raid is not a `LocalGame`. Under Fika the respawn cannot be synchronised with other players, so Redeploy and Core do nothing there. A Fika-only build of your mod gains nothing from the interop file beyond a poll of `MainPlayer`.

`IsAvailable` being `true` says only that Core could replace the body. It does not say that a particular death will be respawned. Use `IsRespawnPending` for that, with its caveats.

**What the queries return**, by installation state. The tracker's `BodyChanged` from the poll works in every state.

| State | `IsBound` | `IsRespawnAvailable` | `IsRespawnPending` |
|---|---|---|---|
| Core absent, older than API v1, or its `Replaced` event unusable | `false` | `false` | `null` |
| Core installed but idle (Fika loaded, no driver, or a patch missing) | `true` | `false` | `null` |
| Core active, Redeploy's `Enabled` setting off | `true` | `true` | `false` for deaths only Redeploy would have handled: it never claims |
| Core active | `true` | `true` | `true` or `false` as described under [`IsRespawnPending`](#isrespawnpending) |

Which events fire in each state, what happens to raid end, and the log lines that tell the states apart are in [SOFT-DEPENDENCY.md](SOFT-DEPENDENCY.md#availability-tiers).

## Reference: binding by hand

You need this only if you bind to `RespawnApi` yourself instead of using the interop file.

| Fact | Value |
|---|---|
| Reflection name | `Redeploy.Core.Api.RespawnApi, maschine-Redeploy.Core` (no version, culture or public key token) |
| Assembly simple name | `maschine-Redeploy.Core` |
| Plugin GUID | none. Core is not a BepInEx plugin, so there is nothing to depend on with `[BepInDependency]` |
| Shape | one `public static class`; only BCL and EFT types in its signatures |
| Threading | Unity main thread only: queries must be called from it, events are raised on it |
| Contract level | `RespawnApi.ApiVersion`, a static `int` property (not a `const`), `1` today |
| Frozen | namespace, type name, assembly simple name, every member name and signature, every string literal below |

The reflection name has no version on purpose: BepInEx resolves assemblies by simple name, so a new Core version never breaks the lookup, while a rename would break every consumer at runtime only. That is why those names are frozen.

`ApiVersion` is a property so that its value is read from the installed Core at run time. A `const` would be baked into your binary at compile time. Check it once; treat `>= 1` as "v1 members exist". The interop file does this for you and also checks each member individually.

## The surface

All members are `public static` on `RespawnApi`. Reflection signatures are exact; the interop file relies on them.

### Queries

| Member | Type | Meaning |
|---|---|---|
| `ApiVersion` | `int` (property) | Contract level of the installed Core. `1`. Fix-level Core releases do not change it. |
| `IsAvailable` | `bool` (property) | Core can replace the local body in this session. When `false`, Core is idle: it claims no death, blocks nothing and refuses every replacement request. It says nothing about whether a given death will be respawned. See [Availability and Fika](#availability-and-fika). |
| `IsReplacing` | `bool` (property) | A replacement pipeline is running. See below. |
| `IsRespawnPending(Player body)` | `bool` | Is a Core driver holding the raid open for this death? `null` means `game.LocalPlayer`. See below. |

#### IsReplacing

`IsReplacing` is `true` from just before teardown (after the preflight, the profile validation and the per-mod preparation have passed, see [refusals](#refusals)) until the synchronous end of that request, and for one more frame when it fails. On today's code a successful replacement completes inside a single call (the new body is created synchronously), so on the success path you cannot observe `IsReplacing == true` from another frame. That is observed behaviour, not part of the contract: the engine still allows creation up to 30 seconds. You see it across frames only after a failure. It is still `true` while `Replaced` handlers run, and already `false` while `ReplacementFailed` handlers run.

#### IsRespawnPending

`IsRespawnPending(body)` answers "will a Core driver try to replace this body?" in two different regimes:

| Moment | Meaning of `true` |
|---|---|
| From `Player.OnDead` (postfixes, `OnPlayerDeadStatic` handlers) until `DeathClaimed` | A prediction, and a heuristic: a registered driver would claim this death right now. It does not know whether that driver will then replace. Known case where it flips: the match-deciding death in ArenaMode Team Deathmatch predicts `true` and turns `false` right after the claim. |
| From `DeathClaimed` until the driver has acted | A driver claimed this death and still holds the raid open. Always `true` while `DeathClaimed` handlers run. |

`true` is never a promise. The player may give up, a required item may be missing, the mode may end the match, and a Killed Stop can still follow. Keep your raid-end handling.

`false` means Core will not replace this body: it is not the current local body, it is alive, it is a bot, nobody claimed it, the claim is released or resolved, the raid is stopping, or Core is unavailable. `false` is not "final death": a respawn mod outside Core may still act. The query never throws; on an internal error it logs a warning and answers `false`.

It is also `false` for a body that was replaced (inside `Replaced` handlers and afterwards) and for a body whose replacement failed (inside `ReplacementFailed` handlers and afterwards).

A mod that holds death sees `false` for the whole held phase, because the query needs a dead body. See [If your mod holds death](#if-your-mod-holds-death).

### Events

All four are `public static event`. Each is raised through the isolating dispatcher described in [Dispatch guarantees](#dispatch-guarantees).

| Event | Delegate | When |
|---|---|---|
| `DeathClaimed` | `Action<Player, string>` `(deadBody, driverName)` | A driver claimed the local player's death. |
| `Replaced` | `Action<Player, Player, string>` `(oldBody, newBody, reason)` | The local body was replaced and is fully wired. |
| `ReplacementFailed` | `Action<Player, string>` `(oldBody, stage)` | A started replacement ended without `Replaced`. |
| `RaidEnded` | `Action` | A `LocalGame` raid ended. |

#### DeathClaimed

`DeathClaimed(deadBody, driverName)` is raised from Core's prefix on `BaseLocalGame<EftGamePlayerOwner>.CG_Spawn`, which is the game's death handler. That is after the vanilla death events and before the driver acts. You are still inside the call stack of the lethal damage. It is raised only for `LocalGame` raids, only while Core is available, and only when a registered driver wants this death. When no driver wants it, nothing is raised and vanilla proceeds. It is a claim, not a promise: it can be followed by `Replaced`, by `ReplacementFailed`, or by neither (see [A refusal before teardown](#refusals)).

#### Replaced

`Replaced(oldBody, newBody, reason)` is raised once per successful replacement, synchronously, in the frame in which the new body is wired and before spawn protection and before the requester's own follow-up work. `newBody` is the new local body. `oldBody` is the body the request replaced; for every current requester that is the previous local body (`game.LocalPlayer`, normally also `MainPlayer`). What exactly is true at that moment is listed in [Timeline of a death](#timeline-of-a-death).

#### ReplacementFailed

`ReplacementFailed(oldBody, stage)` is raised when a replacement that got past its preflight ended without `Replaced`. Core does not roll anything back. The world is as the failing stage left it. Drop every cached body and read `GameWorld.MainPlayer` again in the next frame (see [A failed replacement](#a-failed-replacement)). It is raised at most once per request, never after `Replaced` of the same request, and never after `RaidEnded` of the same game. `oldBody` is `null` only if the request named no body and the game had no local player at entry; no current requester does that.

#### RaidEnded

`RaidEnded()` is raised once per `LocalGame` raid, from the `GameEnd` postfix, after the drivers and Core have reset their per-raid state. Never for Fika's `CoopGame`, the hideout or the trader narrate scenes. It does not depend on `IsAvailable`; see [Availability and Fika](#availability-and-fika).

There is no public raid-start event. The interop tracker reports one itself.

<a id="reasons"></a>
### Reason literals

`Replaced` carries `reason`. It is derived by Core from one thing only: `HealthController.IsAlive` of the old body at the moment the replacement request is accepted. No driver can label it.

| Constant | Value | Old body at entry | What the old body is afterwards |
|---|---|---|---|
| `RespawnApi.ReasonDeath` | `"death"` | dead | Stays in the world as the corpse: same GameObject, every component still attached, `IsYourPlayer` still `true`. Unregistered from the world, no owner, no death handler. It may later return to the shared player pool with its components. |
| `RespawnApi.ReasonSwap` | `"swap"` | alive (ArenaMode kit swap or round break) | A retired living body: unregistered, no owner, no death handler, still alive with its health controller and colliders. Whoever requested the replacement disposes it later. |

The set is open. A later API version may add reasons. Treat any value you do not know as a generic replacement: end the old body's per-life state, attach to the new body. The interop tracker adds `"raid-start"`, `"raid-end"` and `"unknown"` for its own purposes; those never come from Core.

### Stage literals

`ReplacementFailed` carries `stage`, the point at which the replacement ended.

| Constant | Value | What the world looks like |
|---|---|---|
| `RespawnApi.StagePrepare` | `"prepare"` | Failed before a replacement body existed. Teardown has started: the old owner is destroyed, the old body is unregistered, and the profile (item cost, health, equipment) may already be changed. There is no new body. |
| `RespawnApi.StageCreate` | `"create"` | Creating the body faulted or timed out. `GameWorld.MainPlayer` can already point at a registered, unwired body (for example when a subscriber of the game's own `OnPersonAdd` threw). The old body is unregistered and has no owner. |
| `RespawnApi.StageRaidEnding` | `"raid-ending"` | The raid began stopping while the creation was pending. `MainPlayer` can point at an unwired body. The raid teardown owns the session. |
| `RespawnApi.StageRewire` | `"rewire"` | Failed while wiring the created body. `game.LocalPlayer` and `MainPlayer` can already be the partially wired new body. |

Creation currently completes synchronously, so `"create"` by timeout and `"raid-ending"` are not expected on today's code paths. Handle them anyway: they are part of the contract. The set is open here too; treat unknown stages like a failure at an unknown point.

If an exception is thrown after `Replaced` was raised, the requester still learns of it, but Core raises no `ReplacementFailed`: a request produces exactly one public outcome.

## Versioning

### The contract level

- **ApiVersion 1** has existed unchanged in every published release since Redeploy 1.0.0, which bundled Core 1.0.8.
- The **additive policy**: members are never removed, renamed or given a different meaning, and the string literals above never change their values. A new public member means `ApiVersion` 2 and a new baseline file. A changed shape gets a new name and the old member keeps firing.
- Capabilities are detected by the member's existence, not by the version number. A passed version check does not prove that a member exists, so the interop file checks each member and skips a missing or drifted one. Do the same if you bind by hand.
- There is no `Supports(string)` query and no way to ask for a veto, a delay or a priority. See [What the contract is, and what it is not](#what-the-contract-is-and-what-it-is-not).
- `api-v1.baseline.txt` lists the frozen members. A test compares it with the built Core on every run, so an accidental change fails the build.

### Core releases

Core versions only change the fix level; `ApiVersion` stays `1`. How Core reaches the player's machine is in [SOFT-DEPENDENCY.md](SOFT-DEPENDENCY.md#how-players-get-update-and-remove-core).

| Redeploy release | Bundled Core |
|---|---|
| 1.0.0 | 1.0.8 |
| 1.0.1 | 1.0.10 |
| 1.0.2 | 1.0.13 |
| 1.1.0 | 1.0.17 |
| 1.1.1 | 1.0.18 |
| 1.1.2 (current release) | 1.0.20 |

Core 1.0.21 and 1.0.22 exist on the main branch only and are unreleased as of 2026-11-09. They add compatibility adapters for other mods; the API surface is unchanged. See [CHANGELOG.md](CHANGELOG.md).

**`RedeployCore.Version`** (a public static `System.Version` on `Redeploy.Core.RedeployCore`) reports Core's fix level. It is not part of the frozen surface. Do not gate your code on it: gate on `ApiVersion` and on member existence. Only the drivers check Core's version; you do not need to, and you must not ship Core or depend on it (see [SOFT-DEPENDENCY.md](SOFT-DEPENDENCY.md#how-players-get-update-and-remove-core)).

### The interop file

`LocalBodyTracker` has its own revision, the constant `LocalBodyTracker.Revision` and the matching header line. It is **revision 1** (2026-11-09); the file's history and how to keep your copy current are in [SOFT-DEPENDENCY.md](SOFT-DEPENDENCY.md#keeping-your-copy-current).

Every change to the interop file bumps the revision and stays compatible with Core API v1; revisions are listed in [CHANGELOG.md](CHANGELOG.md). The revision is independent of Core: a new Core never forces a new copy, and a new copy never needs a newer Core.

### How to request an addition

Open an issue on the Redeploy repository, https://github.com/maschine34675/Redeploy/issues, and describe the use case: what your mod needs to know or do, at which moment, and what you do today instead. A request for something that would let a consumer veto, delay or stall a respawn will not be accepted. An accepted addition appears as `ApiVersion` 2 with a new baseline, alongside the existing v1 members, which keep working unchanged.

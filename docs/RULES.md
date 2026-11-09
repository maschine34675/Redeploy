# Rules for respawn-safe mods

This is the checklist for a mod that keeps working when Redeploy.Core replaces the local player's body mid-raid. Each rule says what to do, what breaks if you do not, and where the explanation lives. The contract is in [API.md](API.md), worked how-tos are in [RECIPES.md](RECIPES.md), and setup, packaging and tiers are in [SOFT-DEPENDENCY.md](SOFT-DEPENDENCY.md). The interop file is [`../interop/RedeployRespawnInterop.cs`](../interop/RedeployRespawnInterop.cs).

Terms: **Core** is `maschine-Redeploy.Core.dll`, the shared respawn engine. A **driver** is a mod that owns respawn policy and asks Core to replace the body (Redeploy, ArenaMode); the **requester** is the driver behind one particular replacement. A **body** is an `EFT.Player` instance. A **replacement** creates a new local body; the old one becomes the corpse (reason `"death"`) or a retired living body (reason `"swap"`). You **observe**; Core never lets a consumer veto or stall a respawn. Under Fika Core does nothing; see [API.md](API.md#availability-and-fika). Full definitions: [API.md](API.md#what-the-contract-is-and-what-it-is-not).

## Start here

You get body-change events by copying the interop file into your mod and adding its `LocalBodyTracker` to your plugin's GameObject; [SOFT-DEPENDENCY.md](SOFT-DEPENDENCY.md) has the steps. Everything below assumes you subscribe to `LocalBodyTracker.BodyChanged(previous, current, reason)`.

Core never replaces a body when it is absent or idle, but another mod can, and the tracker's poll reports that. Follow these rules unconditionally.

**The minimum every mod does:**

1. Bind the tracker and handle `BodyChanged`.
2. When `previous != null`, end per-life state on `previous` and start it on `current`, for every reason.
3. Reset per-raid state at raid start and at raid end (rule 3).
4. Never act inside the handler: defer one frame (rule 7).

**Where to read first:**

| Your mod | Read first |
|---|---|
| Vetoes `ActiveHealthController.Kill` (downed, critical, revive) | [20 to 23](#mods-that-hold-death), then [1](#rule-1), [22](#rule-22), [18](#rule-18), [3](#rule-3) |
| Anything else that holds per-body or per-raid state | the table at the [end](#which-of-these-does-your-mod-hold), then the rules it names |
| Server-side only | [24](#server-side-mods) |

## The rules

<a id="rule-1"></a>
1. **Identify the current local body with `ReferenceEquals(GameWorld.MainPlayer, p)`, plus `IsAlive` when the body must be living.** Never use `IsYourPlayer`, `ProfileId` or `==`. The corpse keeps `IsYourPlayer == true`, so per-tick logic gated on it keeps running on corpses. Ordinary respawns and swaps keep the `ProfileId`, so it cannot tell old from new. `==` on a destroyed `UnityEngine.Object` lies. `ProfileId` is fine as a lookup key only next to the stored `Player` instance. See [API.md](API.md#identity) and [RECIPES.md](RECIPES.md#tracking-the-current-body).

   | Where you are | Test |
   |---|---|
   | Per-tick logic, input, UI, damage | `LocalBodyTracker.IsCurrentLocalBody(p)` (`MainPlayer` and alive) |
   | Death handlers, `DeathClaimed`, `OnDead` postfixes (the body is already dead) | `ReferenceEquals(GameWorld.MainPlayer, p)` only |

   A downed body that your mod keeps alive with a `Kill` veto is alive, so it passes `IsCurrentLocalBody`. *You see: death logic skipped for the player's own death, because `IsCurrentLocalBody` was false for the dying body.*

<a id="rule-2"></a>
2. **Remove per-body components from the previous body and attach fresh ones to the new body.** Use `AttachFresh<T>(player)`, not `GetOrAdd`, because a pooled player object can carry a stale `T`.
   - Death: the corpse keeps every component and they keep running; it can even return to the player pool carrying them.
   - Swap: the body stays alive and ownerless until its requester disposes it, at a time Core does not define, so its components keep running until then (and `OnDestroy` only fires when it is disposed).

   See [API.md](API.md#reasons) and [RECIPES.md](RECIPES.md#per-body-components). *You see: a revive prompt or HUD element still drawn on the corpse.*

<a id="rule-3"></a>
3. **Reset per-raid state at both raid start and raid end, idempotently.** Raid start is `BodyChanged` with `previous == null` and reason `"raid-start"`; raid end is any of `RaidEnded`, `BodyChanged` with `current == null`, or your own `GameEnd` postfix. Several of them can fire and without Core `RaidEnded` never does, so the reset must be idempotent. Filter the hideout and trader scenes yourself, which also show up as raid start. Never use a per-life reset here, and never use a raid reset on a respawn.

   Do not treat `OnDestroy`, `UnregisterPlayer`, unspawn, a local death or a prefix on `LocalGame.Stop` as the end of the raid. Vanilla calls `UnregisterPlayer` one frame after every death, and Core blocks the Killed `Stop` while it holds a death, so such code either fires mid-raid or never fires. Use the signals above. See [API.md](API.md#raidended) and [RECIPES.md](RECIPES.md#detecting-the-end-of-a-raid). *You see: once-per-raid latches stuck after a respawn, or never cleared after a missed raid end.*

<a id="rule-4"></a>
4. **Re-resolve cameras after every body change.** A camera cached before the change dies at the end of the replacing frame. One cached inside the `Replaced` or `BodyChanged` handler survives only as long as nobody else takes over the `PlayerCameraController`, so read it again later rather than holding it. See [RECIPES.md](RECIPES.md#cameras-and-hud).

<a id="rule-5"></a>
5. **Key `ProfileId` registries safely.** Add or overwrite the entry on `OnPersonAdd`. On unspawn, remove it only if the stored instance is the body that vanished. A same-profile body can already have replaced the entry, so an unconditional remove can delete the live body's entry. See [RECIPES.md](RECIPES.md#registries-keyed-by-profileid).

<a id="rule-6"></a>
6. **In death handlers, check `ReferenceEquals(MainPlayer, body)` first, then ask `LocalBodyTracker.Instance?.IsRespawnPending(body)` instead of guessing.** A retired swap body can die too, and it is not the player's death. Use the tracker's member, not `RespawnApi` directly. The result is `bool?`:
   - `true`: a driver is expected to claim. It is a prediction before the claim, not a promise; a Killed Stop can still follow.
   - `false`: Core will not replace this body now. It is not "final death", because a respawn mod outside Core may act. The value can flip to `false` right after the claim (known case: the match-deciding death in ArenaMode Team DM).
   - `null`: no information (no Core, older Core, Fika, or Core idle). Behave as you would without Core.

   See [API.md](API.md#isrespawnpending) and [RECIPES.md](RECIPES.md#reacting-to-a-death).

<a id="rule-7"></a>
7. **Never start a respawn, stop the raid or destroy a body from a Core or `BodyChanged` handler. Defer it by one frame.** A `RunRespawn` started from inside a `DeathClaimed`, `Replaced` or `ReplacementFailed` handler is refused, and `BodyChanged` is delivered inside `Replaced` when Core is bound. A body destroyed from inside a handler is pulled out from under the code that is still running. This includes removing a corpse, disposing a retired swap body and calling `Kill` yourself. Defer all of these by one frame in every handler, `RaidEnded` included: a coroutine that yields once, then re-checks body identity as in rule 13. A handler cannot tell whether it runs inside Core's `Replaced` or from the tracker's poll, so apply the strict rule in both cases. See [API.md](API.md#handlers-and-isolation).

<a id="rule-8"></a>
8. **Vanilla events inside the pipeline must never throw.** `OnPersonAdd` and the `PlayerCameraController` created event run inside the replacement. An exception there aborts the respawn after teardown has begun, and Core does not roll back. Wrap your handlers in try/catch. Core isolates and logs exceptions from your handlers on Core's own events; it does not do that for your hooks on vanilla events. See [API.md](API.md#timeline-of-a-death).

<a id="rule-9"></a>
9. **Treat reasons as an open set.** `"death"` and `"swap"` exist today; later versions may add more. Do the common path for every reason, and add reason-specific code only for what really differs:

   ```csharp
   EndLifeState(previous);          // every reason, including unknown ones
   StartLifeState(current);
   if (reason == "death") ShowDeathScreen();   // only what truly differs
   // no else branch that assumes "swap" or "death"
   ```

   Never treat an unknown value as an error, and never as `"death"` by default. See [API.md](API.md#reasons).

<a id="rule-10"></a>
10. **Handle `ReplacementFailed` by dropping every cached body and re-reading `GameWorld.MainPlayer` next frame.** Core does not roll back, so the old body, the new one or neither may be the local player. An *orphan* is a new body that Core created but never wired as the local player. The interop poll can report old to orphan and orphan to old; do not assume a chain of changes is monotonic. See [API.md](API.md#replacementfailed) and [RECIPES.md](RECIPES.md#when-a-replacement-fails).

<a id="rule-11"></a>
11. **Do not assume a claim leads to an event.** A refused `RunRespawn` raises no public event, so you can see `DeathClaimed` followed by neither `Replaced` nor `ReplacementFailed`, and the player can also decline the respawn at the driver's prompt. Treat `DeathClaimed` as "a respawn may follow", not as a respawn. Finish your own death handling when the death goes through, without waiting for `Replaced`; let `BodyChanged` mark the start of a new life, and let `RaidEnded` or your own raid-end hook close the raid. Anything you count per respawn (rather than per death) belongs on `BodyChanged`, never on the claim. A claim also arrives for deaths you did not hold, such as an instant kill. See [API.md](API.md#refusals).

<a id="rule-12"></a>
12. **Do not cache the local player in a static.** A static `Player` field points at the corpse (or the retired body) after the first respawn. A static `Profile` stays valid across ordinary respawns and swaps but is replaced by an own-Scav continuation. Read `GameWorld.MainPlayer` when you need it, or hold the tracker's `Current`, which can be stale after `ReplacementFailed`; re-read `MainPlayer` then. See [RECIPES.md](RECIPES.md#tracking-the-current-body).

<a id="rule-13"></a>
13. **Re-check body identity after every `yield` in a coroutine that captured a player.** The body can be replaced between frames; a coroutine that keeps ticking on the old body acts on a corpse. Compare against the current body and stop when it differs. See [RECIPES.md](RECIPES.md#coroutines-and-timers).

<a id="rule-14"></a>
14. **Move subscriptions to the new body.** Events on the old `Player`, its `HealthController` and its `InventoryController` stay with the old body and never fire for the new one. `Profile` and `Skills` are shared by an ordinary respawn or swap (only an own-Scav continuation brings a different `Profile`), so a handler you left on them keeps firing and now acts on the corpse. Unsubscribe from the previous body's objects and subscribe to the current one on every change, and make a handler on shared objects check that its body is still current. See [RECIPES.md](RECIPES.md#event-subscriptions). *You see: a handler firing twice, or acting on the corpse.*

<a id="rule-15"></a>
15. **Expect `Player.Init` and creation hooks to run again for every replacement.** A postfix on `Player.Init` or on body creation fires for each new body, not once per raid. Make per-raid setup idempotent and do per-life setup there deliberately. See [RECIPES.md](RECIPES.md#hooks-that-run-on-creation).

<a id="rule-16"></a>
16. **Do not leave `DamageCoeff` at 0, and do not rely on its value during spawn protection.** Core captures the new body's coefficient, sets it to 0 and writes the captured value back afterwards, so a value you set meanwhile is overwritten, and a 0 left behind is captured and restored as 1. For god mode, use a flag in your own damage patch. See [API.md](API.md#timeline-of-a-death).

<a id="rule-17"></a>
17. **Allow for an identity change on own-Scav continuation.** Redeploy's optional own-Scav continuation (Redeploy 1.1.0 and later) replaces the PMC with the player's Scav profile: a different `ProfileId`, reason still `"death"`, no flag. Compare `previous.ProfileId` with `current.ProfileId` and initialise per-profile state as at raid start. See [API.md](API.md#identity) and [RECIPES.md](RECIPES.md#identity-changes).

<a id="rule-18"></a>
18. **Refresh your own bot-ignore lists, enemy lists and bot memory keyed by the old body or its `ProfileId`.** On `BodyChanged` with a previous body, remove the entries for `previous` and re-add the new body only if it needs one. Store the `Player` instance next to the `ProfileId` and compare it before each lookup. A list keyed by `ProfileId` still matches the new body after a same-profile respawn, so bots stay blind to it; an entry keyed by the `Player` instance still points at the old body. See [RECIPES.md](RECIPES.md#bot-related-state). *You see: bots ignoring the new body for the rest of the raid.*

<a id="rule-19"></a>
19. **Do not block, delay or stall Core's own entry points:** `RespawnApi` events, `RunRespawn`, the claim, the Killed `Stop`. No long synchronous work in a handler: the pipeline runs synchronously and a stalled handler stalls the frame. The API gives consumers no veto and never will. Holding death with a `Kill` prefix is allowed (rule 20), but a hold that never ends means no death is ever claimed, so give every downed phase a bounded end (bleed-out, give-up, out of lives) that lets the real `Kill` through. If you need something added, open a GitHub issue on the Redeploy repository with the use case. See [API.md](API.md#what-is-not-contract).

## Mods that hold death

If your mod vetoes `ActiveHealthController.Kill` (downed, critical or revive mods), these rules apply on top. See [RECIPES.md](RECIPES.md#mods-that-hold-death) for code.

<a id="rule-20"></a>
20. **Expect Core to be silent while you hold death.** A prefix on `Kill` that returns false keeps the body alive, so Core sees nothing: no `DeathClaimed`, and `IsRespawnPending` is false for the whole downed phase because it requires a dead body. Core claims only after you let a death through (bleed-out, give-up, out of lives). The sequence is downed, real death, vanilla death events, `DeathClaimed`, driver prompt, `Replaced`. See [API.md](API.md#timeline-of-a-death).

<a id="rule-21"></a>
21. **The old body's downed state ends on every body change, including `"swap"` and any unknown reason.** ArenaMode replaces a living body for a kit swap or round break, and that is reported as `"swap"` even while your mod has it downed. Stop timers, god mode, bot-ignore entries, input locks and HUD on the old body, and do not carry them onto the new one. See [API.md](API.md#reasons).

<a id="rule-22"></a>
22. **On `BodyChanged(previous, current, reason)` with `previous != null`, run one procedure.** State keyed by `ProfileId` survives into the next life, so the new body would look "already downed" to your `Kill` prefix, or carry an "allow the next death" latch.
    1. On `previous`: end the downed state for every reason (timers, god mode, input lock, HUD, bot-ignore entry, bot groups).
    2. Drop the per-life entry (state, allow-next-death latch, revive progress). If the `ProfileId` changed (own-Scav continuation), initialise `current` as at raid start.
    3. Leave per-raid state untouched.
    4. Do not start a new downed phase on `current` from this handler; defer one frame (rule 7).

    Choose explicitly for each piece of state:

    | Per raid (survives a respawn) | Per life (reset on every change) |
    |---|---|
    | lives used, if you chose per-raid; raid generation | downed state, timers, latches, revive progress, bot-ignore entry |

    *You see: after the first respawn the new body is never downed and dies at once, or bots ignore it for the rest of the raid.* See [RECIPES.md](RECIPES.md#mods-that-hold-death).

<a id="rule-23"></a>
23. **Do not gate per-tick logic on `IsYourPlayer`.** It stays true on the corpse, so downed-state ticks, revive prompts and HUD keep running on a dead body. Use the table in [rule 1](#rule-1).

## Server-side mods

<a id="rule-24"></a>
24. **Do not rely on HTTP seeing every match end.** Own-Scav continuation ends the PMC raid by calling SPT's match-end logic from C#, which static-router hooks on `/client/match/local/start` and `/end` never see (patches on `MatchCallbacks` or `LocationLifecycleService` do). Reset per-raid server state in the layer you hook. See [RECIPES.md](RECIPES.md#server-side-mods).

## Which of these does your mod hold?

| Your mod holds | Rules |
|---|---|
| Per-body components (MonoBehaviours on the player) | [2](#rule-2), [12](#rule-12), [15](#rule-15) |
| Event subscriptions on Player, Profile, Skills, HealthController, InventoryController | [14](#rule-14), [15](#rule-15) |
| ProfileId- or Player-keyed registries | [1](#rule-1), [5](#rule-5), [17](#rule-17) |
| Latches and flags (allow-next-death, once-per-raid, per-life counters) | [3](#rule-3), [17](#rule-17), [22](#rule-22) |
| Lives or attempt counters | [22](#rule-22), [3](#rule-3) |
| God-mode or damage-coefficient changes | [16](#rule-16), [21](#rule-21) |
| Bot-ignore lists, enemy lists, bot memory | [18](#rule-18), [21](#rule-21) |
| Coroutines or timers with a captured player | [12](#rule-12), [13](#rule-13) (add [7](#rule-7) if it stops a raid or destroys bodies) |
| Input locks | [21](#rule-21), [23](#rule-23) |
| Camera or HUD references | [4](#rule-4), [10](#rule-10), [21](#rule-21) |
| Per-tick logic or UI gated on `IsYourPlayer` | [1](#rule-1), [23](#rule-23) |
| A static or cached reference to the local player | [1](#rule-1), [10](#rule-10), [12](#rule-12) |
| Raid-start or raid-end logic | [3](#rule-3), [9](#rule-9) |
| Patches on `Player.Init`, `Kill`, `ApplyDamage`, `Stop` | [15](#rule-15), [19](#rule-19), [20](#rule-20) |
| A veto on `ActiveHealthController.Kill` | [20 to 23](#mods-that-hold-death) |
| Death or respawn reactions (UI, sounds, scoring) | [6](#rule-6), [9](#rule-9), [11](#rule-11) |
| Server-side per-raid state | [24](#rule-24) |

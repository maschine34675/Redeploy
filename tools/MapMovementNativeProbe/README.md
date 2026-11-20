# Map/movement native observation probe

Developer-only; never package this plugin. A normal build copies nothing:

```powershell
dotnet build tools/MapMovementNativeProbe -c Release -p:SPTModType=Library
```

The probe references the installed Core binary without rebuilding or deploying it.
`com.maschine.MapMovementNativeProbe/observe` reads the current physical player,
DynamicMaps provider/controller identities, old/new delegate target counts, and
Advanced Movement state. `remember=true` stores only a comparison reference;
`sample=N` returns one captured transition sample without exceeding UiBridge's
response limit. Samples are bounded to forty entries. A three-second explicit transition watch
reads two cached map fields once per frame and counts hidden/non-raid frames.
It does no scene discovery and is inactive at all other times.

`transition` is a test driver, not a supported third-party Core integration. It
requires a disposable PMC named `MapMoveTest*`, a living local game and an idle
Core pipeline. It uses Advanced Movement's actual `TryStartSlide`/`TryStartDive`
entry points and their native sprint/speed/ground checks. `move=none` skips the
move. `transition=none` only starts the move; `death` uses native health death;
`swap` calls Core's real replacement pipeline at once. The near-position selector
uses Core's usual placement guards. It never invokes compatibility repair methods
or writes foreign controller state. Samples capture state before the move, after
accepted start, before profile mutation and at Core's actual `Replaced` event.

Live-swap bodies are deliberately retained (at most five per raid) to prevent
immediate caller disposal from hiding stale references. The probe releases only
its own retained objects at raid end; native `Player.OnDestroy` performs disposal.
Use healthy, non-bleeding test bodies and end the raid before removing the probe.
Normal native deaths remain under Redeploy.Client/Arena ownership.

Run with a disposable clone of the GPU host test profile and isolated synchronization.
Record exact loaded Core/mod MVIDs, initial and replacement observations, actual
walking/firing, key additions/removals and all log streams. Remove the probe and
temporary mods afterward, restore original configuration bytes and verify every
canonical profile and companion file. No original profile rollback is permitted.

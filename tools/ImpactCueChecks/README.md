# ImpactCue body replacement checks

Run from the Redeploy checkout:

```powershell
dotnet run --project tools/ImpactCueChecks -c Release -- "D:\SPT41\Development\ArenaMode\artifacts\compat-seven-20261025\hud\ImpactCue-UI-MOD-2.0.3\BepInEx\plugins\ImpactCue-UI-MOD\ImpactCue-UI-MOD.dll"
```

The executable links the production adapter, reflection contract, pure scope and
BodyReplacementContext. Its EFT/Unity/Harmony/foreign boundaries are small local
stand-ins; it invokes the production hooks with Harmony prefix/result semantics.
It separately checks every consumed foreign field/method against the shipped
2.0.3 DLL using Cecil, with destructive in-memory negative controls. Nothing is
written to the supplied DLL and nothing is deployed.

The behavioral negative control reproduces the retained-corpse cache. Scenarios
cover absent mod, unchanged ordinary raids, preflight cancellation, exact game
and world ownership, deaths, living swaps, preserved counts and preferences,
old-body BTR/cache/actor callbacks, failed-replacement recovery, pre-activation
Arena swaps, subsequent raids, old-world callbacks, missing hook admission and
unexpected plugin disappearance while owned. A guarded native unregister override
is exercised with an already-unregistered corpse; body authority must still begin
at the exact override entry. The three-level `LocalGameWorld : ClientGameWorld :
GameWorld` fixture dispatches by runtime method handle, while patch registration
rejects inherited `MethodInfo` wrappers. This distinguishes the physical method
body from reflection's `ReflectedType`. Stopping games, wrong owners and destroyed Unity
wrappers also cannot authorize telemetry.
Old-body projectile attribution remains local; it cannot replace the telemetry
cache. These tests do not establish native Harmony/Unity rendering behavior.

Pure scope tests are also in `Redeploy.Tests/ImpactCueTests.cs`:

```powershell
dotnet test Redeploy.Tests -c Release --filter FullyQualifiedName~ImpactCueTests
```

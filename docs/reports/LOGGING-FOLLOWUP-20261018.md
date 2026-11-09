# Respawn diagnostic follow-up — 18 October 2026

Implemented against `bf227f2`; not deployed or tested in EFT by this leaf.

## Behavior

- Core 1.0.8 adds `RespawnRequest.EnablePipelineTiming`, default false. This is
  additive to the engine request, not a change to the frozen `RespawnApi` v1
  observer contract. Existing consumers remain binary compatible. Consumers
  newly assigning the field must ship Core 1.0.8 or later.
- Redeploy's own driver exposes `Diagnostics/EnablePipelineTiming=false` and
  copies the setting into each request. Other drivers opt in independently.
- With collection off, the collector does not construct a state snapshot, read
  reflection/GC counters, measure the victims restore or schedule the extra
  following-frame coroutine. The disabled collector's marker calls are no-ops.
  Victim restoration still runs exactly once regardless of diagnostics.
- Successful expected PerfectCulling sampler replacement is Debug. Successful
  camera eviction, cache reuse, DynamicMaps marker binding and normal Freecam
  setup/mode/rebind details are also Debug. Missing contracts, unexpected
  Freecam script recreation, stranded culling-index repairs and every existing
  failure warning/error retain their original severity and behavior.
- Respawn completion, inventory/protection decisions and safety outcomes remain
  visible. No global logger filtering or configuration changes. `LogLevels=All`
  continues to include Debug; turning timing off removes the measurements
  themselves rather than merely changing their severity.

## Verification

```powershell
dotnet build Redeploy.Client/Redeploy.Client.csproj -c Release -p:CustomAfterMicrosoftCommonTargets=D:/SPT41/Development/ArenaMode-log-followup/tools/AirPitChecks/NoDeploy.targets --nologo -v:quiet
dotnet test Redeploy.Tests/Redeploy.Tests.csproj -c Release --nologo -v:minimal
git diff --check
```

- Client + Core build: zero warnings/errors; the override empties
  `SPTClientDeploy` (the only client/Core deployment target).
- Unit suite: **183 passed, zero failed/skipped**. Existing API-v1 metadata,
  installed-consumer resolution, interface, interop and source-order checks pass.
  No golden order or frozen API baseline was regenerated.
- `PipelineTimingOptInTests` compiles the actual collector, actual
  `RespawnRequest` declaration and stage formatter with instrumented external
  game boundaries. It exercises off, on, on→off, raid reset, failure and
  idempotent completion. It detects two deliberate production-source mutants:
  ignoring opt-in and reading victims before the disabled guard. Each must fail
  with its expected runtime assertion, not a compiler error. A separate source
  check rejects a conditional, missing or duplicated actual victims restore.
- The first suite run was 182/183: an old test looked for Arena only in the
  obsolete flat plugins folder and therefore missed its spawn-selector interface.
  Both metadata scans now also include the current `maschine-ArenaMode` folder.
  The own-client version-stamp check also reads the current worktree's build,
  rather than another checkout named `Redeploy`. Assertions were not weakened.

Four-pass review: (1) request-level API and logging scope implemented;
(2) native ordering and restore independence reviewed;
(3) cost probes, exact failure mutants and actual installed consumer paths
verified; (4) final severity diff, documentation, clean diff and full suite
rechecked. No unrelated plugin, server, native asset diagnostic or logger was
changed. Native acceptance belongs to the parent integration test.

Known measurement limit remains: a timed-out native player-creation task may
finish during a later active pipeline, so restore timing cannot identify the
original task solely from the global statistics callback. This change does not
claim to solve that existing diagnostic attribution limitation.

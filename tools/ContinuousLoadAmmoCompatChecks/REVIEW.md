# CLA migration review - 2026-10-23

Scope: shared Redeploy.Core body replacement. This leaf does not deploy or alter profiles/config.

## Four passes

1. **Authority and ownership.** No Arena symbols remain in production files. Optional plugin binding
   remains reflective; missing mods do not alter admission. Core context proves exact physical raid,
   profile and candidate body. Original registration/loading retains its original actions and Task.
   An inert weak observation records only a successfully completed synchronous initial start branch.
2. **Lifecycle and failure.** Prepare validates only; AbortPreparation drops provisional references.
   Existing standalone controller is adopted at exact candidate registration and disposed once.
   Registration finalizer also retires a partially assigned controller when foreign UI setup throws.
   Disposal finalizer protects successor slots and clears the exact failed retired slot. Tick releases
   retired strong references and rebuilds the controller only for an exact restored live old body.
3. **Async ownership and movement.** Physical controller/inventory/world/body/movement identities
   and per-load/per-task epochs fence delayed state restoration, completion broadcasts, native faults
   and repeated disposal. Native cancellation is harmless. A retired task fault cannot poison a
   successor's admission. Actual native removal revokes observed movement ownership, so a later
   unrelated restriction is never adopted. Pre-existing shared or unverified restrictions reject
   preparation without altering ordinary loading or disposing the initial controller.
4. **Evidence and boundaries.** The executable compiles production adapter/contract/scope plus the
   shared context. Fixture reflection calls route through the production hook dispatcher, including
   nested disposal and native restriction removal. Published CLA 1.2.0 metadata is inspected without
   loading its code. Native Harmony/Unity execution remains a separate GPU acceptance gate.

## R1 review correction

Independent review demonstrated an initial unowned live load whose native restriction survived
failed-candidate recovery. The original harness began that case with an already-owned controller,
so it missed the first-adoption boundary. The correction transfers only a proven initial native
restriction onto the exact physical lease. It does not infer ownership from IsActive or the limit
value, inspect asynchronous MultiSelect flags after the fact, or replace the ordinary native Task.

The new behavioral cases cover initial native loading, rejected preflight, failed-candidate recovery,
completed normal loading followed by an unrelated restriction, a restriction without any CLA load,
pre-existing shared restrictions, actual removal clearing uncertainty, and exact movement identity.
The independent reviewer repeated the original reflection reproducer after the correction:
controller recovered, old SprintDisabled=false, BarbedWire absent, one exact removal.

Final verification passed: 111 behavioral/metadata assertions before and after all 32 compiled
negative controls. Every control failed its intended behavioral assertion; no compile failure was
counted. Verify.ps1 also confirmed unchanged production sources. Evidence:
`artifacts/continuous-load-ammo-checks/92e3de5bdb9944bb83e607774bcaf711/`.
Command: `pwsh -NoProfile -File tools/ContinuousLoadAmmoCompatChecks/Verify.ps1`.
This is offline contract/behavioral evidence only, not native GPU acceptance.

Known inherited limit: an original CLA 800-ms animation continuation begun before first Core
adoption cannot be cancelled retroactively. Its closure still addresses the old physical body;
new controllers are unsubscribed from its identity-free global completion event. This is explicitly
not an assertion that pre-existing native async work can be cancelled by this adapter.

Native BarbedWire AddStateSpeedLimit is a no-op when the cause already exists. Simultaneous
restrictions already share one upstream slot; this adapter neither rewrites that behavior nor claims
to recover multiple owners from that slot. Existing foreign restrictions before the observed CLA
start are handled conservatively by refusing preparation until native removal has cleared them.

## Harness timing follow-up

Independent parent re-verification exposed a fixture timing race: a continuation may be scheduled
between TrySetLastEquippedWeapon and native limit removal. Both completion waits now await the
complete asserted state (weapon equipped and movement limit removed), while preserving the exact
assertions. No production source changed. The targeted baseline passed all 111 assertions and the
active-load mutant failed only its intended active-load assertion. Evidence:
`artifacts/continuous-load-ammo-checks/harness-wait-fix-5e9f188170f848be9794b5f9284ee745/`.
The parent performs the full independent matrix again on this corrected harness.

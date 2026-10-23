# Independent shared body compatibility review — 2026-10-23

Reviewed worktrees: Redeploy baseline `d1d7240`, ArenaMode baseline `c021c0d` under
`D:/SPT41/Development/.worktrees/body-compat-20261023/`. This review changed no
product files, test harnesses, installation files, profiles or configuration.
Only this report is owned by the reviewer. No deployment or native game access.

## Final independent review status

R1 and R2 are closed in the reviewed offline scope. The original R1 reproducer
passes after correction; R2 is covered by the expanded coordinator matrix and a
compiled legacy-refusal-removal negative control. No remaining actionable defect
was established. Full parent reruns, builds, deployment and native acceptance are
separate evidence; this reviewer ran no broad matrix or native game session.
The initial findings below are retained as the review trail and are superseded
by the correction review at the end.

## Findings at initial review

### R1 — First live CLA adoption can strand the recovered old body's movement limit (P2)

Location: `Redeploy.Core/Compat/ContinuousLoadAmmoCompat.cs`, first-controller
adoption at lines 153–165 and disposal cleanup at lines 216–224 (initial reviewed revision).

The first controller is intentionally ordinary until a Core replacement. If that
body starts loading outside inventory, upstream `SetPlayerStateAsync(true)` sets
SprintDisabled and the BarbedWire speed limit, but no Core lease exists and
`HasLimit` is never set. Candidate registration adopts a new lease with
`HasLimit=false`, removes the global completion subscription and disposes the old
controller. If construction then fails before `game.LocalPlayer` switches and the
caller recovers the old live body, Tick recreates its controller, but the previous
movement restrictions remain. The current failure test starts from `Owned()` and
therefore only covers a controller whose start was already owned.

Confirmed with a read-only reflection invocation of the existing
`tools/ContinuousLoadAmmoCompatChecks/bin/Release/net10.0/` executable and fixture:

1. Install the fixture hooks; World; Register the initial ordinary controller.
2. Invoke State(initialController, true); mark its inventory process active.
3. Begin; Candidate; Register the candidate (adopts/disposes initial controller).
4. Restore both native current-body pointers to the old live body; EndAttempt; Tick.

Observed result:

```json
{"OldControllerDisposes":1,"CandidateControllerDisposes":1,"RestoredBodySprintDisabled":true,"RestoredBodySpeedLimited":true,"LimitRemovals":0,"CurrentControllerIsRecreated":true}
```

The upstream source independently establishes the behavior:
`D:/SPT41/Development/ArenaMode/artifacts/forge-top200-20261019/leaf-1/upstream/ozen-m--SPT-ContinuousLoadAmmo/ContinuousLoadAmmo/Controllers/LoadAmmoController.cs`:
`Dispose` at 291 cancels preset/native work and unsubscribes, without directly
clearing movement restrictions; `LoadingOutsideInventory` at 343 enters the start
state; `SetPlayerStateAsync` at 361 sets SprintDisabled, then sets
`SpeedLimit.Value * MovementContext.MaxSpeed` under `ESpeedLimit.BarbedWire`.
The false branch clears sprint and removes the speed limit after 800 ms, but Core
unsubscribes this old controller before native disposal can broadcast completion.

Do not infer restriction ownership from `IsActive` alone: inside-inventory loading
need not have installed the movement limit, and the native BarbedWire slot is shared.
A correction must preserve evidence of this exact controller's upstream start,
or refuse an unprovable live adoption without modifying the old body.
This is distinct from the acknowledged upstream 800-ms closure already started
before initial adoption; that old closure remains an explicit scope boundary.

### R2 — Older Arena adapters can coexist with the new Core adapters (P1)

Location: `Redeploy.Core/Compat/BodyReplacementCompatibility.cs`,
`GetRejectionReason` lines 58–69 (initial reviewed revision).

The coordinator installs/validates all three Core families without rejecting the
loaded older Arena implementation. The existing minimum-version gate admits a
Core newer than the driver's build (`drivers/CoreVersionGate.cs:47`), so installing
standalone Redeploy with Core 1.0.9 beside an older released Arena is a valid loader
combination. Baseline Arena `Plugin.cs:68–70` initializes its own three families.
Deleting those files in the rebuilt Arena only protects the new/new combination.

Both recoil implementations patch the foreign initializer. Both adopt the same
old component pair and clear its static owners before foreign construction. The
second prefix then sees an old lease with empty singleton slots and rejects
(Core `RecoilLegacyCompat.cs:199–200`; baseline
`ArenaRecoilLegacyCompat.cs:143–144`). This occurs after engine teardown. Existing
native hook receipts count hooks from the foreign mod's assembly, and own receipts
only require the current adapter's hooks; neither detects the other compatibility
owner. CLA likewise has two independently owned controller/operation registries.

The relevant legacy adapter families must be detected and refused before any Core
family installs hooks/adopts bodies. When none of the target optional mods is
installed, the ordinary no-mod path should remain inert. Merely updating the new
Arena minimum does not close the old-Arena/new-Core combination.

Both findings were sent to the parent immediately. See the correction review below for current status.

## Four review passes

1. **Authority and scope.** Read all new context/coordinator and three
   Compat/Contract/Scope families. Traced initial exact-body adoption, profile-object
   identity, game/world/main/local identity, weak retired-body leases, same-profile
   unowned bodies, ordinary bots, Arena's explicitly registered medical extension,
   absent optional mods, admission and cancellation before teardown. No additional
   concrete issue identified in these paths.
2. **Native lifecycle and composition.** Verified `LocalPlayer.Create` at native
   `EFT/LocalPlayer.cs:72` passes `async:false`; native `Player.Init` registers at
   `EFT/Player.cs:28272`, and `GameWorld.RegisterPlayer` sets MainPlayer at
   `EFT/GameWorld.cs:2029`, before the foreign recoil initializer. Read the actual
   published recoil decompilation/source and CLA controller source. Traced CLA
   registration before recoil initialization, candidate validation before rewire,
   ReplacementFailed before caller recovery, and Replaced before caller continuation.
   R1 arises at the initial ordinary-to-owned controller boundary.
3. **Cleanup, delayed work and installation skew.** Traced replacement/body/task
   epochs, global completion unsubscription, dead initial CLA controller disposal,
   repeat death/live swaps, new/old recovery, weak recoil tombstones, exact-world
   RetireWorld, world-owned pump OnDestroy, missed-end raid-start fallback, and the
   standalone admission cache. Reviewed baseline/new Arena startup and shutdown,
   no duplicate families in the rebuilt Arena, and shared minimum-version metadata.
   R2 arises only in the supported loader combination with an older Arena binary.
4. **Evidence and claim limits.** Read production-linked fixture programs,
   metadata checks and negative-control scripts for all three families, coordinator
   composition tests under construction, admission checks, and the shared verification
   wrapper. Read completed build logs (Redeploy: 0 warnings/errors; Arena: 4 existing
   CS8632 warnings, 0 errors). Ran only the narrow read-only R1 reproducer above;
   broad test reruns belong to the parent. Fixture behavior is not Mono/Harmony,
   deferred Unity destruction, real animator/UI, or native gameplay acceptance.

## Reviewed sources

- Redeploy: `AGENTS.md` historical-record notice; `.unlazy/body-compat-20261023/PLAN.md`.
- `Redeploy.Core/Compat/BodyReplacementContext.cs`, `BodyReplacementCompatibility.cs`.
- `Redeploy.Core/Compat/RecoilLegacyCompat.cs`, `RecoilLegacyContract.cs`, `RecoilLegacyScope.cs`.
- `Redeploy.Core/Compat/ContinuousLoadAmmoCompat.cs`, `ContinuousLoadAmmoContract.cs`, `ContinuousLoadAmmoScope.cs`.
- `Redeploy.Core/Compat/ContinuousHealingCompat.cs`, `ContinuousHealingContract.cs`, `ContinuousHealingScope.cs`.
- `Redeploy.Core/Engine/RespawnEngine.cs` diff and lifecycle/event surroundings;
  `RaidLifecycle.cs`; `Redeploy.Core/CoreVersion.props`; `drivers/CoreVersionGate.cs`.
- `Redeploy.Client/Services/RedeployDriver.cs`, `Patches/RedeployRaidStartedPatch.cs`;
  README and compatibility documentation.
- Arena: current `AGENTS.md`, `ArenaMode.Client/Compat/ArenaMedicalBodyScope.cs`,
  `ArenaOptionalMods.cs`, `Plugin.cs`, `ArenaMode.Client.csproj`, `LiveKitRespawn.cs`
  recovery path, README, dependency/shared-compat documentation, admission source pins.
- Baseline/primary Arena versions of the three compatibility families, startup,
  and dated Recoil/CLA/CH review documents for reference routing only.
- Local native decompile: `D:/SPT41/Development/_decompiled/EFT/LocalPlayer.cs`,
  `Player.cs`, `GameWorld.cs` at the cited construction/disposal/medical seams.
- Published recoil source and decompile under primary Arena
  `artifacts/compat-first-20261020/recoil/`; CLA upstream controller, registration
  and preset cancellation under the exact path cited in R1.
- `tools/RecoilLegacyCompatChecks`, `tools/ContinuousLoadAmmoCompatChecks`,
  `tools/ContinuousHealingCompatChecks`, `tools/BodyReplacementChecks`,
  `tools/SharedBodyCompatChecks/Verify.ps1`; sibling Arena admission checks.

No other actionable defect was established in the reviewed scope. R1/R2 correction
status is recorded below. This is not a deployment or native-playtest signoff.

## Final correction review

**R2 closed in offline source/composition scope.** `LegacyArenaConflict` now runs before every
family admission call and before context/body/pump creation. It inspects the actual
loaded Arena plugin instance assembly for the matching legacy family type, and
requires that family's optional plugin GUID to be installed before refusing. Old
Arena with none of the target mods remains inert; an unrelated legacy family does
not block a different targeted family. A missing/destroyed Arena instance produces
a retryable refusal; a ready rebuilt instance clears that result on refresh.
The coordinator tests use real `Assembly.GetType` over separate dynamic legacy and
rebuilt plugin assemblies rather than stubbing the conflict check. Final evidence:
`tools/BodyReplacementChecks/artifacts/20261023151837139/` reports 19 cases /
349 assertions and 9 compiled negative controls, including removal of the legacy
refusal. All baseline/mutant builds have zero warnings/errors. Reviewed coordinator
SHA-256: `EAED089DFD7DE776DC0A48EC093E2D437C041631FFCAE1FA2C9BD2BA4779561F`.
No further source defect found in this correction; parent rerun remains separate.

**R1 closed in offline source/original-reproducer scope.** The adapter now weakly
observes the initial ordinary controller's actual successful synchronous
`SetPlayerStateAsync(true)` call without replacing its Task or actions. A preexisting
BarbedWire limit marks the observation ambiguous and causes non-destructive
preparation refusal. Exact controller/body/world/inventory/MovementContext identity
is required to transfer the observation into an owned lease. An observation of
actual `RemoveStateSpeedLimit(BarbedWire)` invalidates the proof, so a later unrelated
restriction is not inherited from an earlier completed load. Preparation cancellation
preserves ordinary loading; disposal cleanup requires the same physical movement
context. Source review found no additional actionable defect in the correction.

Re-ran the original narrow read-only reflection reproducer against the rebuilt
existing Release executable, without modifying any source/harness or writing output:

```json
{"OldControllerDisposes":1,"CandidateControllerDisposes":1,"RestoredBodySprintDisabled":false,"RestoredBodySpeedLimited":false,"LimitRemovals":1,"CurrentControllerIsRecreated":true}
```

Reviewed correction source SHA-256:

| Source | SHA-256 |
|---|---|
| ContinuousLoadAmmoCompat.cs | D3A70827928AB5C3BBF98D670C5C0462D33C8AB1E8345D3CF0E404BB8C53DDAE |
| ContinuousLoadAmmoContract.cs | 550E42EF3F3A9C738D50E8297F942C3E6FBE8B123EE8FAA343F39B6F59DAFD23 |
| ContinuousLoadAmmoScope.cs | 3861DCC95C2E2A400E20F8A3AF626AD9674B458C9A08B6D42EDDB549227B7EB4 |

The expanded CLA baseline reports 111 assertions. Its complete 32-mutation run
and the parent rerun remain separately reported execution evidence; this reviewer
does not claim those runs as personally executed or complete here.

**Native ownership boundary.** EFT uses one shared `ESpeedLimit.BarbedWire` entry.
Native `Player.AddStateSpeedLimit` forwards `MovementContext.AddStateSpeedLimit`,
which does nothing if that cause already exists; CLA calls `ChangeSpeedLimit`, which
min-merges the entry. The correction conservatively rejects a restriction already
present before an observed initial CLA start. It does not create separate ownership
for overlapping native writers in the shared slot; a later no-op wire Add remains
upstream behavior. My initial suggestion that wire Add itself min-merges was
refuted by the exact native forwarding and is not a finding.

**Remaining acceptance limits.** Native Mono/Harmony detours, Unity destruction
scheduling, real CLA UI/animator behavior, standalone death/respawn and failed live
swap recovery, mixed-client loading, and a following ordinary raid still require
new-build gameplay evidence. Older Arena GPU runs are not migration acceptance.
The pre-adoption 800-ms upstream continuation boundary remains as described above.



The new removal observer relies on native `RemoveStateSpeedLimit` semantics.
A foreign patch that suppresses that method without removing the entry is not
certified by these checks. No such interfering patch was established in the reviewed
installation/source scope; this is a boundary, not an open confirmed defect.

## Parent execution receipt

After the independent review, the parent re-ran the complete corrected CLA matrix:
111 assertions before and after all 32 compiled negative controls, exit 0 with
`CONTINUOUS_LOAD_AMMO_COMPAT_CHECKS_OK`. Evidence:
`artifacts/continuous-load-ammo-checks/0d2e247933434fbd85e9002115535970/`.
The correction to two harness waits preserves their assertions and only waits
for both completed side effects; production sources did not change.

Final integration evidence is under
`tools/SharedBodyCompatChecks/artifacts/a94db883708c4aa0a079dfa7aff6fd7e/`:
both clients/Core build, 200 Redeploy tests, Arena 2,289 passed/one existing gated
skip, admission 46 assertions/seven mutants and session 679 assertions. Parent
composition re-verification is in `tools/BodyReplacementChecks/artifacts/20261023152104795/`.
These execution receipts close the previously pending matrix evidence above;
they do not extend the stated native acceptance boundaries.

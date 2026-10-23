# Shared body replacement compatibility

Core 1.0.9 moves the general Recoil Rework Legacy, Continuous Load Ammo and
Continuous Healing adapters out of ArenaMode. Standalone Redeploy uses the same
protection. These are optional integrations, not dependencies. The frozen
`RespawnApi` v1 surface and the death/swap event sequence remain unchanged.

## Ownership and preparation

- Ordinary initial spawning is not a Core replacement. Before teardown, Core
  validates the exact local game, world, profile and physical old player, then
  prepares the existing foreign components/controllers for adoption. A dead old
  player is valid; controller disposal waits until candidate registration.
- All three adapters must finish their non-destructive preparation before the
  old owner's teardown. A rejected preparation forgets only its provisional
  adoption; it does not destroy components, dispose controllers or change settings.
- During construction, only the new local player of that exact operation may
  acquire the replacement lease. Sharing a profile ID is insufficient.
- Core checks the constructed body and hook health before publishing the
  replacement. Failure closes construction authority before the failure event;
  recovery follows the caller's authoritative physical player pointers.
- A world-owned update component handles recovery and later weapon changes.
  Raid ending also performs cleanup directly, so it does not rely on that
  component surviving the world. Weak retired-body/controller ownership protects
  delayed callbacks without retaining those bodies indefinitely.

## Optional integrations

| Mod | General Core behavior |
|---|---|
| Recoil Rework Legacy 2.0.0+ | Transfer singleton recoil/camera components before the replacement's `Awake`; refresh the actual equipped firearm cache; fence retired updates. Failure recovery can restore ownership even to the still-dead original body. Never destroy a player's GameObject. |
| Continuous Load Ammo 1.2.0+ | Adopt the initial loading controller; retire and rebind it on the exact replacement; isolate disposal, movement limits and asynchronous completion by physical controller/inventory/task. |
| Continuous Healing 1.6.3+ | Keep upstream behavior for the current living local player; prevent retired-body start/cancel/effect-removal callbacks from operating on the successor. Unrelated ordinary bots remain upstream. |

Newer versions are admitted only if the reflected APIs and required Harmony hooks
remain valid, with a warning that runtime compatibility is unverified. Missing or
invalid contracts refuse a Core replacement before teardown. Standalone Redeploy
shows the reason at raid start and lets vanilla death handling run when admission
fails. Arena displays the same reason through its existing admission path.

Update ArenaMode and its matching Core together. With a target optional mod loaded,
Core refuses an older Arena binary that still contains the corresponding adapter,
before installing its own hooks. This prevents two independent owners from handing
off the same components/controllers. Without these optional mods the check is inert;
an Arena plugin that is still loading can be checked again later.

Continuous Load Ammo's first ordinary controller remains upstream. Core only
observes its actual completed loading-start call and the actual removal of the
speed limit, without replacing its Task or actions. This allows a failed live
replacement to return to the old body without leaving its old loading restriction
behind. The proof belongs to the exact controller, body, inventory, world and
movement context. A pre-existing shared limit or uncertain start refuses the
replacement before teardown rather than clearing an unrelated restriction.
An upstream delayed animation Task already started before the first adoption
cannot be cancelled retroactively; it still refers to the old physical body.

## Arena-specific behavior

Arena registers `BodyReplacementCompatibility.RegisterHealingScope` to own medical
fallback for bodies in an exactly active Arena world, including Arena bots. That
registration does not extend recoil/ammo ownership. Core alone never infers bot
ownership merely because a normal raid contains a replaced player.

The three foreign-hook implementations and their behavioral harnesses live only
in Redeploy.Core. Arena retains a small medical scope bridge and its own admission
checks. Disposing Arena's registration must not shut down shared Core protection
used by standalone Redeploy. Keep the old weak medical leases for late callbacks.

## Verification

The harnesses under `tools/RecoilLegacyCompatChecks`,
`tools/ContinuousLoadAmmoCompatChecks` and `tools/ContinuousHealingCompatChecks`
link the real adapter sources and use local fixtures for Unity/game/foreign APIs.
They verify native binary contracts from published fixture DLLs as metadata and
exercise compiled negative controls. This is offline evidence, not a substitute
for testing the migrated hooks in Unity.

The final no-deploy integration run on 2026-10-23 passed all **200 Redeploy unit
tests** and **2,289 Arena unit tests**. One Arena case was skipped by its existing
installed-APBS environment gate. Arena admission checks passed 46 assertions and
seven compiled negative controls; session checks passed 679 assertions. The
production-linked coordinator/Arena-scope harness passed 349 assertions across
19 scenarios and rejected nine compiled mutants. Both clients and Core built
successfully; Arena reported only the four existing CS8632 nullable-context
warnings in `ClientSettingsModels.cs`. Installed DLL hashes did not change.

Parent re-verification also passed the production-linked adapter suites: Recoil
137 assertions with nine compiled negative controls, Continuous Healing 286 with
14, and Continuous Load Ammo 111 with 32. The Ammo rerun exposed and corrected a
test-only intermediate-state wait; the complete corrected matrix then passed.

The independent review and its reproduced/fixed first-controller rollback and
older-Arena conflict are in `tools/SharedBodyCompatChecks/REVIEW.md`. The ordinary
native speed-limit removal contract is assumed; unrelated mods suppressing that
native operation are not certified by these checks.

Native acceptance should cover standalone Redeploy without ArenaMode: first and
repeated death, naked respawn followed by picking up a gun, ordinary healing and
loading after replacement, a late old loading completion, raid exit and a second
raid. Arena regression should cover repeated death and a live round/wave refill,
with foreign mods present and absent. Do not reuse the pre-migration Arena GPU
reports as native acceptance for this shared implementation.

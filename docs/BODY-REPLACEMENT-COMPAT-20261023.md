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
| Recoil Rework Legacy 2.0.0+ | Transfer singleton recoil/camera components before the replacement's `Awake`; refresh the actual equipped firearm cache; fence retired updates. Failure recovery can restore ownership even to the still-dead original body. Never destroy a player's GameObject. The handover also applies to the own-Scav body, which is built with the replacement profile before the game commits it (see [PMC-TO-SCAV.md](PMC-TO-SCAV.md)). |
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

## Native validation 2026-10-23

Bounded checks ran on SPT-GPU-2 with Core and Redeploy built from `cac56a8`
and ArenaMode from `2a5c239`. No production code changed during these tests.
Loaded module checks confirmed Core 1.0.9 and the rebuilt consumers. The tested
foreign clients were Recoil Rework Legacy 2.0.0, Continuous Load Ammo 1.2.0 and
the already installed Continuous Healing 1.6.3. The full Arena status capture
confirmed all three foreign modules matched the files on both installations.
Recoil's separate server package was not installed.

Standalone testing temporarily removed Arena's client and server DLLs. The
subsequent Arena tests used the normal installation with a narrow temporary
allow rule; they did **not** exercise the isolated Arena-session launcher,
second server or session switching. Only the dedicated test profile was used.
Native test hooks supplied controlled damage/deaths; ordinary inventory and
interaction input exercised equipment, medical use and magazine unloading.

| Scenario | Observed result |
|---|---|
| Ordinary Factory PMC before replacement | Actual M9A3 shot; one IFAK activation healed both arms from 25 to 60 HP. CLA continued a real magazine unload with the inventory closed. |
| First standalone death and naked respawn | Distinct living body, correct Recoil/camera and CLA body/inventory references, empty hands and null firearm cache. The original pistol was recovered from the corpse, drawn and fired; its actual hands controller matched Recoil's cache. A further single medical activation healed both arms, and another closed-inventory unload completed. |
| Second standalone death | Another distinct naked successor with correct physical owners and no stale firearm cache. Settled movement-limit reads contained only the normal surface limit. |
| Second ordinary PMC, same process | Fresh living body and correct Recoil/CLA owners; the previous replacement had been destroyed. Native raid exit succeeded. This was an ownership/world-lifecycle control, not another firing or medical test. |
| Arena Deathmatch | Initial supplied kit and two death replacements retained correct physical owners. Real VSS, HK416 and MCX shots were observed. After the first death, one Car kit activation healed both arms from 20 to 60 HP; a native magazine unload continued outside inventory and completed. |
| Solo Zombies, supplied kit | Two living wave refills produced distinct bodies with correct Recoil/camera/CLA owners and restored ammunition. Wave 1 ended through the existing 100-second no-progress watchdog; wave 2 ended through a real Mosin headshot. After the second refill, another real shot and one Car activation healing both arms from 25 to 60 HP succeeded. |
| Fresh process without temporary Recoil/CLA | Original CH stayed installed. Factory Deathmatch completed a death respawn, real AK-74N/ADAR shots and one continuous activation healing both arms from 25 to 60 HP, then exited normally. |

The first corpse-looting attempt missed the interaction target. Corrected aim
acquired the native corpse and opened its inventory; no collider, ownership or
corpse-lifetime fix was needed. One fire command timed out across the immediate
Zombie wave-2 replacement. The native kill, score change and next-wave state
confirmed that kill; the timed-out command itself is not counted as a successful
driver receipt. The first Zombie transition proves a watchdog-triggered refill,
not a player or companion combat victory. PFT was not exercised.

Owner snapshots prove physical reference equality at the captured instants.
Shots, healing and inventory transitions also use the operator's direct
observations recorded in private execution notes; they are not inferred from
an owner-only snapshot or an accepted input command. In particular, the first
standalone pre-weapon-selection capture still shows a knife and null firearm
cache; the later drawn-pistol/cache match and shot are separate observations.

### Limits and log findings

No pending-load race was reproduced. The Deathmatch unload completed before
death, and the standalone timing did not establish a pending operation at death.
These runs therefore do not certify the 800-ms old-completion/new-load race,
pending work during a living replacement, forced replacement failure or a
late retired-body Continuous Healing callback. They also do not establish
native Arena-bot medical fallback, all recoil animations/settings or future mod
versions. The corresponding deterministic checks remain separate evidence.

The reviewed client/server windows contain no adapter rejection or failed body
replacement, but are not error-free:

- Temporarily removing Arena's server DLL left its directory empty. SPT logged
  a ModLoader Fatal, `No Assemblies found`, for that directory and continued.
  This was a test-installation side effect, not an adapter failure.
- Ordinary exit produced the previously observed inactive-`MenuScreen`
  coroutine error. The exits completed; the log does not attribute its cause.
- EFT traces include Factory spawn-marker corrections and transit diagnostics,
  missing muzzle-jet material messages, two VSS foldable-component messages,
  one unknown custom-item-type message and six G36 utility-operation messages.
  Startup also retained serialization/Harmony, collider, trader/Fence and
  companion-profile warnings. No attribution stack establishes a new adapter
  cause; these observations are not proof that every message is harmless.
- Missing muzzle-jet materials also appeared in the no-Recoil/CLA control.
  Three Animator synchronization-layer warnings occurred at the Zombie exit;
  the exit and subsequent control still completed.

### Cleanup and control

The temporary Recoil/CLA DLLs, generated configurations and temporary allow rule
were removed from both installations. Existing Continuous Healing was retained.
The original CH, ArenaMode and Redeploy configuration files were restored and
matched their archived hashes at cleanup. The rebuilt Core and consumers remain
deployed. A fresh process loaded 40 plugins and six libraries with matching
local/GPU/loaded identities, with Recoil/CLA absent and CH retained. The control
in the table passed; no adapter rejection or failed replacement appeared. This
is not a CH-absent control. After its normal exit, the game and server were
stopped and original configurations restored once more. The final DLL inventory
contained no added paths and no changed pre-existing payloads except the intended
Core/consumer updates.

The standalone test used normal death/disconnect semantics: the test profile's
original pistol, medical item and magazine were lost. A temporary incomplete
pistol and IFAK supplied for probing were left on the second corpse, not
extracted. No personal profile or raw profile JSON was changed to undo those
ordinary test outcomes. Mod/config restoration must not be read as an unchanged
test-profile inventory claim.

Private evidence is retained in sibling ArenaMode's
`artifacts/shared-body-gpu-20261023/`: owner captures, full module status,
execution notes, lifecycle/log extracts and cleanup receipts. These artifacts
are not release assets and are not copied into the public documentation.

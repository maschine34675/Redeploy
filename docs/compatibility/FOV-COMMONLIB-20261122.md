# Fontaine FOV Fix 5.0.1 and Realism CommonLib 0.1.0

Source, offline and scoped native validation: 2026-11-22. Shared implementation:
Redeploy.Core 1.0.28. The tested candidate is archived; no regular deployment or
release is implied by the test results below.

## Reviewed identities

| Package | Runtime identity | Reviewed version |
| --- | --- | --- |
| Fontaine FOV Fix | `com.fontaine.fovfix`, assembly `FOVFix` | 5.0.1 |
| SPT Realism Common Lib | `com.fontaine.spt-realism-common-lib`, assembly `spt-realism-common-lib` | 0.1.0 |

Official CommonLib DLL SHA256:
`A912D436A8778B90291E8E3BA4AC929E7CE9B25670022FC2F7076F923EC9A61F`.
Its product/assembly version is not its BepInPlugin version; admission reads the latter.

FOV's existing physical-player controller, watcher reset, aiming and compensation hook
contracts remain valid in 5.0.1. The reviewed baseline rises to 5.0.1 while the minimum
stays 4.1.0. FOV 5 adds a hard dependency on CommonLib; CommonLib needs its own adapter.

## Failure and repair

CommonLib creates nine MonoBehaviours on each player: PlayerState, WeaponState,
AimingState, HealthState, HazardsState, GearState, FiringState, SkillState and ReloadState.
Its global fields switch to the fresh components, but a retained outgoing body's components
remain enabled and subscribed to static events. In particular, an old ReloadState can
write into the new global player's hands animator. GearState's original OnDestroy removes
its weapon subscription through the mutable global WeaponState, rather than its original
publisher.

The shared adapter does the following only for Core-owned physical replacements:

- Capture the exact outgoing bundle and current enabled flags during read-only admission.
- Suspend that bundle when the native factory initializes the exact candidate. The SPT 4.1
  LocalPlayer factory calls Player.Init with `async: false`; world registration occurs
  before CommonLib's init postfix, allowing the existing Core candidate ownership check.
- Keep native-created fresh controllers and their initial state. Fence old instance
  callbacks and old player, firearm, movement and PWA callback sources. A suppressed
  foreign boolean prefix returns true to permit the native EFT operation.
- After physical commit, remove only delegates whose target belongs to the outgoing
  bundle, including its original WeaponState publisher. Preserve unrelated subscribers.
- Keep delayed old controller destruction from unsubscribing through the successor's
  globals. Ordinary destruction of one still-active controller retains its original
  cleanup and leaves sibling controllers alone.
- Restore the exact saved globals and latest enabled flags on failed creation. A partial
  foreign initializer is captured by a finalizer and its partial subscriptions are retired.
  If a failed rewire already committed the new LocalPlayer, retain the successor instead.
- Release current world references on retirement; weak component leases continue fencing
  delayed callbacks without a growing list of retained corpses.

There is no new Update/Tick pump, scene search or process-wide Harmony enumeration.
Contract reflection and event target removal occur at admission or replacement boundaries.
Hot callbacks use weak lease lookup and cached owner accessors. When the mod is absent,
no CommonLib Harmony hooks are installed. Core API v1 and RunRespawn's frozen statement
order are unchanged. No mod setting or physical inventory is modified by this adapter.

## Offline verification

`tools/RealismCommonLibChecks/Verify.ps1` builds the adapter against actual EFT assemblies,
runs its production code with real Harmony and independent controller fixtures, checks the
official CommonLib binary metadata/cleanup method shape and rejects nine compiled mutants.

Current results: 36 normal lifecycle assertions, eight partial-init assertions, official
metadata passed and nine mutants rejected. The enabled-state rollback regression was first
observed failing, then passing after refreshing the snapshot at each Prepare.

The shared coordinator harness includes CommonLib and Tarkova-Cola in its admission,
presence, prepare, cancellation, completion and retirement checks. These adapters add no
coordinator Tick calls. Its 23 cases passed 1,100 assertions and rejected 23 compiled
mutants, including four new CommonLib/Cola presence and failed-attempt cleanup mutations.
The existing FOV ownership/profile/source guards were also rerun: 174 tests passed,
none skipped. These checks establish the stated code contracts, separately from the
native observations below.

## Scoped native acceptance

SPT-GPU-2 ran the official FOV Fix 5.0.1, CommonLib 0.1.0 and Tarkova-Cola 4.0.0
packages with the frozen candidate A. Core DLL SHA256:
`1BEBDB16F58B899F89164F20044ED22957BAD72ED575F69984007DFBC7AE792C`.
The complete candidate and DLL/PDB manifest are retained in the adjacent ArenaMode
checkout under `artifacts/cola-fov-20261122/candidate-a/` (`files.json`, `sources.json`).

- The Core 1.0.27 control reproduced the defect: a real Core replacement retaining
  the living outgoing body left all nine old components enabled, with duplicate
  old/new GearState subscriptions to scope-input events.
- Standalone Factory with Arena's client absent passed a native death and the
  PMC-near-corpse respawn choice. G28 ADS, shooting and walking worked afterward.
- A real QuickRevive bottle, stimulant and lethal chest hit produced an active
  downed state. A subsequent real Core replacement retained the old living body.
  All nine old CommonLib controllers were disabled, their recorded event targets
  removed and all nine current controllers enabled on the replacement object.
  FOV's cached player was the new body. Cola's `Downed` cleared while `_reviveUsed`
  stayed true. The successor remained alive 35 seconds after the swap, beyond the
  old ten-second deadline, and fired three M4 rounds.
- Arena Deathmatch with the same official mods passed automatic death/respawn and
  a retained living-body replacement. Both preserved exact FOV/current-controller
  ownership and disabled all nine old controllers; ADS, shooting and walking worked.

Raw native records are in ArenaMode's `artifacts/cola-fov-20261122/native/`:
`control-after-live-swap.json`, `standalone-loaded.json`,
`candidate-standalone-death.json`, `candidate-standalone-downed-swap.json`,
`candidate-standalone-after-deadline.json`, `arena-loaded.json`,
`candidate-arena-death.json`, `candidate-arena-ads.json` and
`candidate-arena-live-swap.json`.

This covers those physical replacements, controller/event observations and input
checks. It does not establish every optic, every Cola perk combination, a long raid,
own-Scav behavior with these versions or forced foreign initializer/creation failures
in the game. The latter failure/rollback paths were tested offline. Regular deployment
was deliberately withheld because the installed Arena feature-branch build differs
from the main-branch candidate. The host's original binaries and settings were
restored, temporary test mods/profile were removed, and synchronization resumed.
The full Arena exit profile matches the actual arm checkpoint; original profiles
and PFT data remained byte-identical. The cross-repository native report is
`ArenaMode/docs/reports/2026-11/COLA-FOV-NATIVE-20261122.md`.

## Sources

- [FOV Fix 5.0.1 release](https://github.com/space-commits/SPT-FOV-Fix/releases/tag/v5.0.1)
- [FOV Fix 4.1.0 to 5.0.1 comparison](https://github.com/space-commits/SPT-FOV-Fix/compare/v4.1.0...v5.0.1)
- [CommonLib 0.1.0 release](https://github.com/space-commits/spt-realism-common-lib/releases/tag/v0.1.0)
- [CommonLib controller implementation](https://github.com/space-commits/spt-realism-common-lib/tree/v0.1.0/src/StateControllers/InstanceState)

# Amand, Quick Cancel and PiP body compatibility

Status: **Core 1.0.15 development work, 2026-10-26**. The adapters are shared by
standalone Redeploy and ArenaMode. Source/build checks and the completed Arena
tests are distinguished below; PiP's corrective path passed two scoped Arena
deaths and one scoped standalone death. A native Labyrinth initial activation
failure remains an explicit Amand blocker. Arena has added reviewed-version
entries for Quick Cancel, PiP, HUD Breakout and Fast Grenade Throw; Amand remains
unlisted. Final test cleanup is verified; this report does not publish a release.

## Exact foreign releases

| Mod | Plugin GUID / version | Released DLL SHA-256 |
|---|---|---|
| [Amand's Graphics](https://github.com/ArchangelWTF/AmandsGraphics/releases/tag/v1.8.0) | `com.Amanda.Graphics` / 1.8.0 | `0BAD292DA16386DE8A737C5C627016030A343256A3670823ABEE805439CB75BD` |
| [Manimal's Quick Cancel](https://sp-mod.com/mod/2969/manimals-quick-cancel) | `com.manimal.quickcancel` / 2.0.0 | `3C6C87112A7C0739C505A7F204DB2E3F13932DFDC8C568519E34B8BF685463BC` |
| [Picture in Picture Disabler](https://sp-mod.com/mod/2667/picture-in-picture-disabler) | `com.fiodor.pipdisabler` / 2.1.0 | `12712B3FDE8BC5C24031B2BFB2B54FF9383582E72F2608148CD391E2E4B83B44` |

Source revisions corroborated against these binaries were respectively
`5ad92998bc6acdaf9ba90286485e9a96d9c3d962`,
`2003d9e4280dadb8f00f388e14be39d0152ca138` and
`3258ebc65cdfffc454f045544e40d31f102d2c45`.
PiP's assembly version is 1.0.0.0 despite its plugin version being 2.1.0.
Foreign DLLs/source remain external test inputs and are not packaged with Core.

The optional contracts validate loader identity, minimum/reviewed version,
reflected members and required native hooks. A matching newer version follows
the validated API with a warning; version alone is not proof of compatibility.
Missing mods add no runtime behavior. An invalid contract refuses the relevant
replacement/admission instead of guessing at foreign state.

## Shared lifecycle authority

`BodyReplacementCompatibility` includes all three optional families in
admission, preparation and exact-world retirement. PiP and Amand also participate
in the world pump and cancel provisional preparation when needed. Quick Cancel
has no provisional mutable state to abort and needs no pump.

Preparation is read-only: merely accepting a request does not authorize a
foreign reset. The actual physical old body's native unregister boundary,
including concrete client-world overrides, establishes the replacement scope.
Amand's new-camera handoff additionally waits for the exact published replacement
body and live native camera. Failed/unpublished candidates, another world,
ordinary bots and a profile-ID match alone cannot grant ownership. Weak retired
object records fence late callbacks without keeping old worlds alive.

## Amand: replace the camera, preserve world defaults and the user toggle

Two real baseline Factory Arena respawns demonstrated the defect. Amand's
`Player.Init` hook already rebound its `Player`, but all three cached camera
references remained destroyed while a new enabled native FPS camera existed.
The baseline was Core 1.0.14; no exception was necessary for this failure.

The actual EFT camera path instantiates the active prefab before naming it
`FPS Camera`. Amand's Prism `OnEnable` hook activates only for that final name,
so it can miss a Core-created camera. Another native `OnEnable` on an already
bound live camera can also set GraphicsMode false before its delayed activation
finds the existing camera and skips setup.

`Compat/AmandsGraphics` waits for the current registered living replacement,
its matching game/world/camera-controller ownership and the actual main camera.
It then uses Amand's reviewed activation method. It does not invoke foreign
`OnEnable`, null out arbitrary camera fields or replay world startup.

The adapter preserves fourteen original scene/weather defaults instead of
recording already modified graphics as the next vanilla baseline. New camera
component defaults still come from the new native camera. Original values for
live registered lights survive activation; destroyed keys are retired only
inside the owned scope. An early legitimate native activation is recognized
without a second activation. The chosen GraphicsMode is preserved, including
failure paths and redundant current-camera Prism notifications. Temporary
restoration state is cleared in nested finalizers. World retirement releases
the adapter's strong references.

The final reviewed production source hashes were:

- `AmandsGraphicsCompat.cs`:
  `603C09A9BF8FA757DA5E212155FB9511EED0F1A2E5D8CB37BF0E077CBEB169F4`.
- `AmandsGraphicsContract.cs`:
  `6562EBAB509CB194D3775435963843F178C3908D173936E616FBF5117AE52870`.

These identify source evidence; they are not deployed DLL hashes.

### Labyrinth initial-scene failure is outside the replacement repair

Candidate 3, process `6533c0c32fa74cc6928ab3a7dd9cd4ac`, failed initial Amand
activation on Labyrinth at 09:41:33.699, before Core attempted Arena's initial
kit replacement. The stack enters Amand's native `ActivateAmandsGraphics` from
its own delayed Prism `OnEnableAsync`. The observed scene was `default`,
`levelSettings` null and GraphicsMode false. Core then refused preparation at
09:41:37.872 because the complete original baseline was unavailable.

The exact release IL corroborates the missing scene handling: its map dictionary
has no Labyrinth entry, falls back to `default`/`!settings`, then calls
`GameObject.Find` at `IL_02b1` and immediately dereferences it with
`GetComponent<LevelSettings>` at `IL_02b6`. There is no intervening null guard.
The observed state and exception strongly identify this initial scene lookup;
no separate ordinary PMC control was run. It is not a replacement-created
exception, even though the subsequent safe refusal prevents Arena's kit swap.

There is no demonstrated safe Core-only recovery: skipping validation would
lease partial foreign state; reactivation would repeat the same lookup. The
adapter does not invent scene defaults, add a fake `!settings`, globally disable
Amand or null foreign fields to force a retry. Recommendation: keep Amand
unlisted until this map blocker has a separately reviewed solution. The Factory
ON/OFF result remains a bounded successful replacement test.

Evidence: sibling ArenaMode's `candidate3-labyrinth-bepinex.log`,
`candidate3-loaded.json`, `candidate3-deployment.json` and archived binaries.
Verified Candidate 3 Core SHA-256:
`C437D1523F48498DC733D4A21902E86DB9AEC47A595A5C221ADCD4401D6BD8AF`.
Arena client SHA-256:
`56D2A63CD54FAF7AD2BCD02711C331FBD5E519A301B772387046CDDD1782253A`.

## Quick Cancel: retire only the old body's pending controllers

Quick Cancel keeps pending `MedsController` objects in a strong static set while
throwing the item. A native asynchronous inventory continuation can encounter a
dead player and report success without calling the Drop path that normally
removes the pending entry. Controller destruction does not clear that foreign
set. Repeated replacements can therefore retain old controllers and bodies.

`Compat/QuickCancel` removes only controllers belonging to the exact old body
when its Core unregister actually occurs. Weak retired-body records reject a
late Mark for the same retired body. There is no global Clear, inventory action,
forced completion of foreign asynchronous work or configuration rewrite.
Current-body and unrelated ordinary-player/bot entries remain foreign-owned.
This fixes the pending-set lifetime, not every native inventory callback race.

## PiP: physical optic/cache ownership

The foreign optic callbacks can replace global optic state or hide the reticle
for any `OpticSight`, including cold role-models and callbacks whose vanilla
original another Harmony prefix skipped. Its cut-mesh caches survive until
plugin shutdown. `Compat/PipDisabler` ties owned callbacks, subscriptions, lens
state and mesh profiles to actual local held roots, optics, bodies and worlds.
Retirement restores original meshes before disposing only owned generated cuts.
Other caches and corpse/original asset meshes are not globally cleared.

Before the first Core replacement, ordinary callbacks remain native. Core
refuses a preexisting active optic whose owner it cannot prove; it does not
reset arbitrary foreign globals to force admission. Arena separately guards
its own exact cold-kit preload callbacks, with production tests linking those
Arena files into `tools/PipDisablerChecks`.

Native Candidate 1 exposed an overly strict post-death ancestry assumption:
EFT may reparent/drop the held gun and remove hands before Core preparation.
The correction observes weak, read-only provenance of the exact local held
`ControllerGameObject` during native local optic/scoped callbacks. Actual
replacement can then recognize that same observed root after native death.
A root now owned by another player, a foreign/unobserved preload, or a destroyed
optic still fails ownership. The observation itself neither gates nor changes
an ordinary pre-replacement callback.

This correction has offline positive/negative coverage and passed two actual
scoped deaths in Candidate 3 Factory Arena and one scoped standalone death.
Candidate 2 is not PiP evidence: its DLL was deliberately disabled there.

## Verification record

| Check | Evidence and boundary |
|---|---|
| Amand offline | 187 positive assertions across ten modes; five compiling mutants rejected at intended assertions. Final leaf and root rerun agree. Real-EFT API build: zero warnings/errors. |
| Quick Cancel offline | 52 positive assertions across five modes; four compiling mutants rejected. Production adapter/context, real Harmony and released metadata checked; actual EFT API compile passed. |
| PiP offline | Leaf checkpoint reports 92 assertions, released metadata and actual EFT API compile; includes production Core/Arena classes, Harmony dispatch, detached-dead-root and bot-root controls. |
| Coordinator review | New-family admission, prepare/cancel/pump/retire wiring and test coverage independently reviewed; no concrete defect found in the reviewed integration. |
| Native baseline | Two Factory Arena replacements retained destroyed Amand camera references while its new physical Player and native camera were valid. |
| Native Candidate 2 | Two Factory Arena replacements with Amand ON retained ON; after a native Insert toggle to OFF, the next replacement retained OFF. New physical camera/body bindings were valid. Real Quick Cancel after replacement dropped the medkit and restored a functioning firearm. |
| Native Candidate 3 Factory | Two confirmed scoped deaths in the next raid passed. Old bodies remained; active/last optic references cleared and owned weapon caches returned to zero. A fresh Specter produced a centered magnified reticle after replacement. |
| Standalone Factory | Arena client absent, same Candidate 3 Core: one scoped M700 death and a second unscoped death passed. PiP cleared its old optic/cache state; Amand and HUD Breakout followed both new bodies. Real Quick Cancel and an FGT RGD throw worked. |
| Full integration suites | Redeploy 283 passed; ArenaMode 3,213 passed and one known skip. These are offline tests, separate from native acceptance. |

Native evidence resides in sibling ArenaMode
`artifacts/compat-remaining-20261026/native/observations.md`,
`candidate2-bepinex.log` and `candidate2-off-hud.png`, with action snapshots.
The screenshot was visually inspected: the scene is visible, HUD health reads
390/440 and MCX magazine ammo 27/30. HUD Breakout followed both replacements;
this required no Core HUD adapter. The full item/HUD observations are in
[Arena's report](../../ArenaMode/docs/REMAINING-MOD-COMPAT-20261026.md).

Candidate 3's scoped replacements changed body/camera
`-1775842/-1775988` to `-1805414/-1805560`, then `-1815380/-1815526`.
`candidate3-pip-after-active.png` was inspected and shows the new active Specter
reticle before the second scoped death. Two earlier unscoped deaths are excluded
from PiP acceptance. No new compatibility errors accompanied the scoped
replacements. Amand and HUD Breakout followed the new physical bodies.

Standalone process `242cfdb6`, recorded in `standalone-loaded.json`, used the
same archived Candidate 3 Core with the Arena client DLL absent. A scoped M700
death changed body/camera `-1401980/-1402128` to `-1562518/-1562664`; its old
optic `-1400132`/one cache cleared to no active/last optic and zero owned caches
while the corpse remained. A second, unscoped death produced
`-1631674/-1631820`. Both replacements completed without a corresponding error.
The second death is not claimed as another scoped test. Quick Cancel dropped
Analgin before death and again after replacement using native use/QuickReload
about one second apart; FGT's real underhand RGD throw succeeded at 09:53:43.

HUD Breakout's empty-hands `SHORTNAME` placeholder was a cosmetic observation.
Amand's `backLens`/`sightComponent` still referenced live retained-corpse objects,
although its player and FPS camera were current. Optional optic DOF was not
tested; the successful default-camera path does not clear that limitation.

Candidate 2 process was `2e6e5641`, started 09:28:30 local on SPT-GPU-2, using
SPT 4.1.6, ArenaMode 0.3.0, Redeploy.Client 1.0.2 and Core 1.0.15.0
(`fa72287` plus development changes, DLL timestamp 09:27:05). The operator
checked loaded files against the host disk at startup, but its exact Core DLL
SHA-256 was not archived. An earlier deployment manifest belongs to Candidate 1
and cannot supply Candidate 2's hash. A later rebuilt file cannot reconstruct it.

The rare Quick Cancel pending-death race was not observed natively. Attempts
at 150 ms did not prove cancellation; the successful test used medical speed 1
and about 700 ms between selecting slot 4 and QuickReload. A zero pending count
after a completed drop is not proof that the race was exercised.

At Candidate 2 raid exit (09:38:27.678), the log contains a null reference in
`BotFirstAid.CG_ApplyToSelf.method_1` through
`MedsInHandsOperation.Finish/OnEffectRemoved`. Its causal ownership remains
unresolved. Labyrinth exit separately logged a destroyed-Transform exception in
`TriggerZone.MoveInsideRoutine` at 09:42:50.016. No corresponding compatibility
failure occurred during the accepted replacements, but the complete runs must
not be described as error-free.

## Remaining limitations

- Resolve or explicitly retain the Amand Labyrinth initial-scene blocker before
  admission. Broader map, lighting, optional DOF/health-effect, NVG/thermal and
  killcam/spectator coverage. Factory defaults are not blanket certification.
- Resolution or explicit disposition of the observed bot-healing exit error.
- Reviewed-version Arena policy entries for the four accepted mods are present;
  Amand stays unlisted. Release work remains separate.

## Deployment and cleanup

The exact tested Candidate 3 Core (`C437D1523F48498DC733D4A21902E86DB9AEC47A595A5C221ADCD4401D6BD8AF`)
and Redeploy client (`31102063E9426967C584B653C59E426D59A1E09396F311A32AAA06ADB08CB8BA`)
are deployed on the development installation and GPU2. Core is 1.0.15;
the public Client/Server version remains 1.0.2. No server binary was replaced.

Cleanup removed all 161 temporary package/probe files and their temporary
configuration/policy, restored existing configs and the GPU2 test profile,
verified the other eight canonical profile copies unchanged, then restored
the original synchronization exclusions. GPU2 game/server/launcher are stopped.
Standalone raid exit logged an inactive `MenuScreen` coroutine; this separate
exit observation was not a body replacement failure. No release or push occurred.

No new HUD Breakout adapter or FGT body adapter is implied by this work. The
FGT priority/input correction is Arena-specific and remains outside Core.

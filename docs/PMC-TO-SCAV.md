# PMC death followed by a fresh Scav

Development work on `feature/pmc-to-scav`. This is an opt-in same-raid continuation,
not a change to the normal Player-Scav queue or cooldown. It is not released.

## Behavior

- With `RespawnAsScav` disabled, Redeploy retains its normal respawn choices.
- With it enabled, a dead PMC can continue once as a newly generated Scav at a
  Scav spawn on the same map. The PMC corpse remains in the world and can be looted.
- The Scav has a distinct identity, equipment, health and statistics. Regular
  Player-Scav equipment and cooldown are not used to generate this character.
- A subsequent death, leaving the raid or time expiry ends the raid. The Scav
  cannot respawn again. Transit to another map is outside this first version.
- The PMC's outcome remains a death. PMC progression ends at that death; Scav
  progression is not added to it.
- Successful Scav extraction delivers carried loot by a system message using
  the normal **Receive items** transfer. Run-through extraction can deliver loot,
  with its found-in-raid status handled separately from successful extraction.
- Recovered insured items are excluded from the original PMC's lost-item claim
  before message item IDs are generated. Retained PMC items must never also be
  included in the Scav loot message.

## Separation of responsibilities

Shared Core gains an optional replacement-profile input. Existing same-profile
callers, including ArenaMode, do not opt into the new flow. Core owns the body,
camera, AI and optional-mod handover. The standalone client owns the choice,
PMC snapshot, Scav extraction UI and original-PMC result presentation.

The server binds a prepared Scav to an actual server-issued PMC raid. Preparation
does not settle the PMC or alter the ordinary Scav. The final native raid-end
request settles the frozen PMC death and, on extraction, the separate Scav loot.
A persisted receipt and settlement journal prevent a retry from granting the
same loot or processing the same PMC death again. An ambiguous save failure must
stop for recovery rather than replaying native profile side effects.

The SPT regular-Scav result-save hook is bypassed only for the owned hybrid
result. Its normal path assumes that the final Savage profile is the account's
Player-Scav; that is deliberately not true here. Ordinary Scav results still
use the SPT handler.

Core registers the replacement's native death-stop callback after bot
registration, matching initial native spawn. This lets integrations such as
PitFireTeam process death before the raid-stop handler disposes their registry.
The callback is still attached if bot rebind throws, so failed-rewire recovery
can end the raid.

## Failure and recovery limits

- A map must provide native Scav spawns and exits. BTR/transit item deliveries
  already made in the PMC phase prevent conversion; empty native transfer roots
  do not. Transit is disabled and hidden during this first Scav implementation.
- Required respawn-item cost, health, delay and protection use Redeploy's existing
  settings. The new option does not enable Fika support.
- Journal files live beside the server DLL in `data/pmc-scav/`. They can contain
  private profile snapshots and authorization tokens while a raid is pending.
  They are runtime state and must never be included in a release package.
- An unresolved prepared/active raid or ambiguous save failure blocks new raids
  for that profile instead of guessing whether to replay settlement. Automatic
  recovery after a process/network failure is not implemented. Preserve the
  journal, profile and logs for diagnosis; deleting only the journal is unsafe.
- A final lost-insurance scan failure refuses settlement rather than inventing
  an empty claim. Ordinary PMC death remains available if preparation fails
  before the body switch.

## Acceptance checks

The following table defines the checks. Dated evidence below distinguishes
native runs from offline tests and remaining limits.

| Case | Evidence required |
| --- | --- |
| Switch | Different profile IDs; actual Savage side; independent gear; controllable body, camera and inventory |
| AI and corpse | Scav AI relationship after the switch; old PMC corpse remains lootable |
| Extract | Scav exit really extracts; PMC-only exit is disabled; original raid time remains |
| Loot | One receipt; Receive items succeeds; recovered insured item is not also insured for return |
| Failure | Second death/give-up/MIA ends; no successful-loot message; no third body |
| Persistence | PMC death and original progression; ordinary Scav gear/cooldown unchanged |
| Retry | Duplicate prepare/activate/end and save ambiguity cannot duplicate effects |
| Regression | Default-off normal PMC respawn and normal Scav respawn remain functional |

Native testing uses the dedicated SPT-GPU-1 test profile. Syncthing for its SPT41
folder is paused before test files or profiles are changed. Original files,
configuration and profiles are backed up with hashes and restored before the
previous synchronization state is resumed. Source checks, offline builds,
installed hashes and running-process gameplay evidence are recorded separately.

## Validation — 2026-10-27

SPT 4.1.6 / EFT 40743, SPT-GPU-1, Factory. Deaths used UiBridge's real health
controller kill hook; transfers used native UI actions, extraction used the
actual Camera Bunker Door trigger. PitFireTeam 1.0.0 was installed without an
invited squad. No temporary test plugin was installed.

Final native candidates (SHA-256):

| Component | Version | SHA-256 |
| --- | --- | --- |
| Core | 1.0.16 | `C54872B4B528315969C20125F3FC54E0D96F769E2D0BAE254AC29E89B8291E2F` |
| Client | 1.0.2, feature branch | `3EEA2ABEADB521BD2A3CBC5BD26B56F341E0609DC399EBE86B8AB22BFB0E86A6` |
| Server | 1.0.2, feature branch | `8B4F75D47E6BBA2DB19465190C5B9B929A61D4B60288F07E299B4C312AD99DA5` |

The binaries were built from the final source before feature commits; their
informational Git stamp still names the branch base `ecf40d9`. The hashes above
identify the tested files, rather than a later rebuild with a different stamp.

| Check | Result |
| --- | --- |
| Profile/body switch | Native pass: distinct Savage profile, fresh gear and local camera; corpse retained. |
| Scav AI | Native pass: the replacement object was present in an assault group's `Neutrals`, while its current enemy was a PMC. |
| Corpse recovery | Native pass: M9A3 taken from the original PMC into the fresh Scav's holster, preserving its pre-mail item ID. |
| Extract/timer/UI | Native pass: actual Scav exit, remaining raid time, Scav exit list without transit rows, PMC Killed result with original death duration, return to menu. |
| Loot and insurance | Native pass: exactly one extracted-loot receipt; recovered insured M9A3 excluded from insurance, abandoned insured backpack included. Two-item insurance was an isolated profile fixture, not a native insurance-purchase test. |
| Receive items | Native pass on the preceding client with the same message/settlement code: Receive all transferred the recovered M9A3 into the stash. Repeated final candidate extraction again produced one valid loot receipt. |
| Regular Scav isolation | Native pass: the entire server-side regular Scav object compared equal immediately before/after both final hybrid runs. |
| Second hybrid death | Native pass: no third body/prompt, result and menu complete, one receipt with `HasRewards=false` and no items; no PFT disposed-registry exception. |
| Ordinary Scav | Native pass on final candidate: same-profile respawn, subsequent final death at respawn limit, native `/raid/profile/scavsave` succeeds, result and menu complete. |
| Ordinary PMC | Native pass on previous candidate before the final callback-order correction: same-profile respawn, original PMC identity, another intercepted death, End raid and menu complete. Final callback ordering also has offline positive/negative controls. |
| Retry/persistence | 59 server tests: coordinator state, duplicate requests, failed and ambiguous saves, receipts, transfer roots, insurance, and delayed prepare after ordinary end. These use controlled native-service boundaries rather than fault injection into a real account. |
| Core/regression | 307 unit tests and BodyReplacementChecks (19 cases, 573 assertions); offline Core/Client/Server builds without warnings/errors. Five independent review findings were corrected. |

Main final client log: `LogOutput-SPT-GPU-1-2026-10-27_09-38-45.log`. Runtime markers include
ordinary Scav save at 09:41:52, hybrid extraction guard at 09:47:03, and final
hybrid death guard at 09:49:35. The preceding corrected candidate log is
`LogOutput-SPT-GPU-1-2026-10-27_09-19-21.log`.

One preceding long multi-raid process hung on the black screen while unloading
an ordinary Scav raid; it required a hard close. No corresponding exception was
logged. The final candidate's fresh-process Scav test completed normally, but
that single hang is not proven fixed. A separate native BotItemTaker exception
was traced by matching DLL/MVID and IL offset to a delayed callback after bot
disposal, before any loot transaction; it was not evidence of duplicated loot
or profile corruption.

Not established by this run: all maps, invited PFT squads, Arena gameplay on the
new Core, MIA timeout, real network/process fault recovery, or a long natural
combat session. PMC statistics separation is covered by frozen-request source
and server tests; the native extraction tests did not earn Scav combat XP.

## Test-host restoration

SPT-GPU-1's SPT41 Syncthing folder was paused before deployment/profile edits;
the other sync folder was untouched. Game and server were stopped for final
restoration. At 08:53:57 UTC, all **623** baseline files (profiles, configuration,
and replaced binaries) matched their saved SHA-256 hashes. **380** newly created
test files inside the managed profile/config/Redeploy directories were removed.
The original sync state was then restored and confirmed as `paused=false`.
No candidate DLL was left deployed on the test host or the main development
installation. Private evidence/backups remain outside the synchronized tree in
`C:\Temp\RedeployPmcScav-20261027`; they are not committed.

Implementation commits: `3735024` (shared Core) and `8d8195d` (standalone client,
server and server tests). Only end-of-file whitespace was normalized after the
last native build. No merge into main, push or release was performed.

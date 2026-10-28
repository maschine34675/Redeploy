# PMC death followed by your regular Player-Scav

Development work on `feature/pmc-to-scav`. This is an opt-in same-raid continuation
using the account's regular Scav and cooldown. It is not released. The 2026-10-28
revision replaces the earlier independent generated-Scav/mail prototype; that
prototype's dated evidence below is historical and does not validate the new flow.

## Behavior

- With `RespawnAsScav` disabled, Redeploy retains its normal respawn choices.
- With it enabled, a dead PMC can continue once as their regular Player-Scav at a
  Scav spawn on the same map. The PMC corpse remains in the world and can be looted.
- The Scav uses the account's current Scav identity, equipment and progression,
  separate from the PMC. The normal Scav cooldown must have expired; the prompt
  explains when it is unavailable, and the server independently enforces it.
  Preparation reserves this Scav without generating new gear. Refused preparation
  does not consume a respawn-cost item or reset the Scav cooldown.
- A subsequent death, leaving the raid or time expiry ends the raid. The Scav
  cannot respawn again. Transit to another map is outside this first version.
- The PMC's outcome remains a death. PMC and Scav combat progression are handled
  separately. Native shared effects, such as Fence reputation and Scav quests,
  still apply as in an ordinary Scav raid.
- Successful Scav extraction opens the native **Scav-to-stash transfer screen**.
  There is no loot message. Run-through extraction uses normal found-in-raid rules.
  Completing the transfer generates the next Scav and its regular cooldown;
  dying as the Scav generates the successor through the native death handler.
- Recovered insured items are excluded from the original PMC's lost-item claim
  before loot transfer. Retained PMC items must never also appear in Scav loot.
- Scav lifetime and time-based exit conditions use the actual replacement body's
  play time, excluding the PMC life and waiting at the death prompt. The shared
  raid timer and native extraction/run-through policy are unchanged.

## Separation of responsibilities

Shared Core gains an optional replacement-profile input. Existing same-profile
callers, including ArenaMode, do not opt into the new flow. Core owns the body,
camera, AI and optional-mod handover. The standalone client owns the choice,
PMC snapshot, Scav extraction UI and final Scav result presentation.

The server binds the reserved account Scav to an actual server-issued PMC raid.
The final request settles the frozen PMC death and then the Scav outcome, saving
both together. The complete native raid-end pipeline runs once for the PMC;
the native Scav outcome handler then applies the second life within that same
transaction. The original raid ID is preserved, including when ArenaMode's
ordinary-raid protection is installed. A durable journal with full-profile disk readback prevents a retry
from processing either outcome again. The native transfer moves items from the
settled Scav inventory; completion and late inventory uploads are guarded against
regeneration or item duplication. An ambiguous save failure stops for recovery.

The SPT regular-Scav result-save hook is bypassed only for the owned hybrid
result. Combined settlement already stores the account Scav inventory, so this
late client snapshot must not reinstall transferred items or overwrite the next
Scav after death. Ordinary Scav results still use the SPT handler.

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
- Pending loot transfer also prevents a new raid until it is completed. Old
  independent-Scav journals from protocol 1 require their original build/recovery;
  they are never interpreted as regular-Scav reservations.
- A final lost-insurance scan failure refuses settlement rather than inventing
  an empty claim. Ordinary PMC death remains available if preparation fails
  before the body switch.

## Acceptance checks

The following table defines the checks. Dated evidence below distinguishes
native runs from offline tests and remaining limits.

| Case | Evidence required |
| --- | --- |
| Switch | Different PMC/Scav profile IDs; actual account Scav identity and gear; controllable body, camera and inventory |
| AI and corpse | Scav AI relationship after the switch; old PMC corpse remains lootable |
| Extract | Scav exit really extracts; PMC-only exit is disabled; original raid time remains |
| Loot | Native transfer succeeds; no loot message; recovered insured item is not also insured for return |
| Failure | Second death/give-up/MIA ends; no loot transfer; no third body |
| Persistence | PMC death and original progression; Scav outcome, successor and cooldown saved |
| Cooldown | Native expiry boundary is enforced in prompt and server; unavailable Scav cannot be used |
| Retry | Duplicate prepare/activate/end and save ambiguity cannot duplicate effects |
| Regression | Default-off normal PMC respawn and normal Scav respawn remain functional |

Native testing uses the dedicated SPT-GPU-1 test profile. Syncthing for its SPT41
folder is paused before test files or profiles are changed. Original files,
configuration and profiles are backed up with hashes and restored before the
previous synchronization state is resumed. Source checks, offline builds,
installed hashes and running-process gameplay evidence are recorded separately.

## Regular-Scav validation — 2026-10-28

Candidate 3 uses SPT-GPU-1, SPT 4.1.6 / EFT 40743 and Factory. The installed
ArenaMode ordinary-raid protection and PitFireTeam 1.0.0 remain present, with
no invited companions. Focused persistence runs disable bots; they do not prove
combat AI behavior or companion handling. Deaths use the real health-controller
kill hook, corpse recovery and transfer use native inventory operations, and
extraction uses the actual Camera Bunker Door trigger.

| Component | Version | Tested SHA-256 |
| --- | --- | --- |
| Client | 1.0.2, feature branch | `D5EA11E21D179769801997E83E0676F7748829BC8B9486C8C324F680C8017DC3` |
| Core | 1.0.16 | `25DE9FB8F3853EFD1EC3B18EA650777ECFD6D21E02189D5AE6B3D2FEDBC4CA9C` |
| Server | 1.0.2, feature branch | `BDC6B3D0CB2E12BBCA567E2995094F26CD547B2FB035800AC83FB91DFE93F96B` |

These are pre-commit builds with informational Git stamp `9ecdb10`; the hashes
identify the tested candidate, not a later rebuild. Candidate 3's first client
process was `ff2be345eae64dc5a9db8a73be3302b6`, started at 09:46:42 UTC.

Confirmed native extraction results:

- The replacement uses the actual account Scav ID, equipment root and existing
  loadout. The original PMC corpse remains lootable.
- The insured PMC M4 was recovered and transferred through **ScavInventory**
  into the PMC stash. Its original item ID appears once in the PMC inventory
  and zero times in the Scav inventory. Remaining Scav gear was deliberately
  discarded using the native confirmation.
- The insurance queue contains only the abandoned insured backpack. Insurance
  enrollment was an isolated two-item profile fixture, not a purchase-flow test.
- The result shows the Scav nickname and **39 seconds** of Scav lifetime.
  Persisted Scav sessions increased from 5 to 6 and lifetime from 487 to 526;
  prior PMC time and prompt waiting were excluded.
- Transfer completion generated a new Scav equipment root and a future normal
  cooldown. There was no extracted-loot message.
- In the immediately following PMC raid, the death prompt showed the remaining
  24-minute Scav cooldown and offered only **End raid**, without a continuation
  button. Ending that raid completed normally.
- With the feature disabled, ordinary PMC respawn preserved the PMC identity
  and left the Scav-continuation context empty. The second death at the configured
  one-respawn limit ended the raid without another body or prompt.
- Death of the adopted account Scav ended the raid without a third body or
  another respawn prompt. The native result showed the Scav nickname, **Killed
  in Action** and its own **15-second** life. This result transition took about
  73 seconds on the test host but completed without a backend request timeout.
  It returned to the menu without a transfer, generated a successor and future
  cooldown, and persisted one Scav session plus 15 seconds of lifetime. Both
  unrecovered insured PMC items remained in the insurance claim.
- In an ordinary Scav raid with the feature disabled, respawn preserved the
  account Scav ID and Savage side while producing the normal new equipment
  root. The second death at the limit ended normally. The ordinary SPT
  `/raid/profile/scavsave` request remained enabled and succeeded.

The earlier candidates exposed two native failures that were corrected before
this result: reusing the extraction panel left an `EventTimer` dictionary entry,
and a second outer raid-end call with a fabricated Scav ID reentered ArenaMode's
ordinary-raid semaphore. Candidate 3 rebuilds only the owned native timer
instances and preserves one outer end with the real issued raid identity.
An independent source review also corrected Scav time before native lifetime
and exit-condition statistics run. No ArenaMode protection gate was weakened.

Offline validation: **351 client/Core tests**, including 44 focused continuation
tests, and **86 server tests** passed. Builds completed without warnings or
errors. Negative controls cover early cost consumption, late time correction,
duplicate custom timers and the former exact-raid gate reentry.

Final client logs: `LogOutput-SPT-GPU-1-2026-10-28_10-46-26.log` (extraction,
cooldown and ordinary PMC), `10-57-47.log` (own-Scav death) and `11-03-57.log`
(ordinary Scav). Separate fresh client processes were used for the two final
death scenarios. Private profile assertions independently verified extraction
and death outcomes against the immutable baseline.

Not established by these focused runs: combat AI/Scav karma from kills, invited
companions, other maps, MIA timeout, process/network fault recovery or native
respawn-cost-item failure injection. The inspected extraction was a run-through;
survived outcomes and retry/failure boundaries also have offline test coverage.
One death-result text snapshot included `EARLY EXTRACTION` alongside the correct
`Killed in Action` result. Native status mapping was reviewed and remains
`Killed`; without a screenshot, the subtitle's rendered presentation is not
confirmed. No transfer or progression error accompanied it.

### Test-host restoration — 2026-10-28

At **10:12:33 UTC**, all **9,954** baseline entries were restored and verified by
SHA-256 or their original absence. The backup covered the complete server user
tree, client configuration and replaced Client/Core binaries. **151** files
created during testing inside the managed directories were removed. Game and
server were stopped throughout restoration. Only after verification was the
original Syncthing state restored and confirmed as `paused=false`; the other
sync folder was untouched. A separate final check confirmed that all three
installed Redeploy DLLs match their original baseline.

Private evidence and immutable backups remain outside synchronization under
`C:\Temp\RedeployOwnScav-20261028`. No candidate binary remains installed on the
test host or the development installation. Implementation commit: `0047ad5`.
The feature remains on `feature/pmc-to-scav`; no main merge, push or release was
performed.

### Persistent feature deployment — 2026-10-28

At the user's subsequent request, the exact tested candidate 3 DLLs and their
matching PDBs were installed in `D:\SPT41` and `C:\SPT41` on SPT-GPU-1. All
three DLL hashes match the table above. `[General] RespawnAsScav = true` was
set in the existing installation configuration; the code default remains false.
Other settings and profiles were preserved. Original binaries and configuration
are backed up outside synchronization in
`C:\Temp\RedeployScavDeployment-20261028-130643-ae84a41f` on the development host.

SPT-GPU-2 was initially stopped, but another session started an Arena test before
deployment finished. Its client/Core files and enabled configuration reached
disk, while the running server kept the previous Server DLL locked. The user
will stop/restart that host themselves; no raid was interrupted. The server
update must finish synchronizing while both game and server are closed before
the feature is used there. This deployment records disk hashes, not a new
running-process or raid acceptance test. The feature branch remains unmerged
and unpublished.

## Historical validation — independent-Scav prototype, 2026-10-27

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

### Historical test-host restoration — 2026-10-27

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

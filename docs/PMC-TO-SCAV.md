# PMC death followed by your regular Player-Scav

Redeploy 1.1.0 provides an optional same-raid continuation
using the account's regular Scav and cooldown. The 2026-10-28
revision replaces the earlier independent generated-Scav/mail prototype; that
prototype's dated evidence below is historical and does not validate the new flow.

**Validation status:** candidate `6a1332d` resolves the three fixes identified at
`7037a25`: interruption recovery, BTR delivery restrictions after switching, and
availability checks before selection. **469 client/Core tests and 149 server
tests passed. Focused native recovery and control tests passed on SPT-GPU-1.**
See the [new native evidence and limitations](PMC-SCAV-RECOVERY-20261028.md) and
[release review](PMC-TO-SCAV-RELEASE-REVIEW-20261028.md). The earlier normal-flow
tests below retain their original dated scope. These tests used pre-1.1.0
binaries; see the [1.1.0 release notes](RELEASE-1.1.0.md) for release scope.

## Behavior

- `RespawnAsScav` defaults to enabled; an explicitly saved false is preserved.
  The death prompt offers naked PMC respawn near the corpse or at a map spawn,
  plus continuing once as the regular Player-Scav at a Scav spawn on the same map.
  With the setting disabled, only the normal respawn choices remain.
- Scav cooldown or unavailability known before selection disables only the Scav choice. Choosing PMC
  never reserves the Scav or starts its continuation transaction. Both choices
  respect the respawn limit and configured item cost. The PMC corpse stays lootable.
  The same check now includes missing/unverified Scav exits, BTR/transit items
  already sent during the PMC life and a raid reached by map transit (see below);
  it is repeated immediately before preparation.
- The Scav uses the account's current Scav identity, equipment and progression,
  separate from the PMC. The normal Scav cooldown must have expired; the prompt
  explains when it is unavailable, and the server independently enforces it.
  Preparation reserves this Scav without generating new gear. Refused preparation
  does not consume a respawn-cost item or reset the Scav cooldown.
- A subsequent death, leaving the raid or time expiry ends the raid. The Scav
  cannot respawn again. Transit to another map is outside this first version:
  the Scav choice is disabled, with a reason, in any raid you reached by map
  transit (second and later raid of a transit chain), because the server only
  registers a fresh raid for a Scav continuation.
- If the server refuses or cannot answer before anything changed locally (raid not
  registered, server restarted during the raid, older server mod, connection
  problem), the prompt comes back with the PMC choices and the Scav choice disabled
  with a readable reason; the raid does not end. See
  [Pre-change refusal](#pre-change-refusal-and-incoming-transits--hotfix-111).
- The PMC's outcome remains a death. PMC and Scav combat progression are handled
  separately. Native shared effects, such as Fence reputation and Scav quests,
  still apply as in an ordinary Scav raid.
- Successful Scav extraction opens the native **Scav-to-stash transfer screen**.
  There is no loot message. Run-through extraction uses normal found-in-raid rules.
  Completing the transfer generates the next Scav and its regular cooldown;
  dying as the Scav generates the successor through the native death handler.
  **Sell All is unavailable for this continuation's transfer.** Move wanted items
  to the stash, then finish the transfer. Ordinary Scav Sell All remains native.
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

## Optional-mod handover for the Scav switch

The engine builds the Scav body with the replacement profile and assigns
`game.Profile` only after the body was created and validated. While the body is
built, `body.Profile` therefore differs from `game.Profile`. Optional-mod adapters
that decide during construction must accept the profile the active replacement
expects (`BodyReplacementContext.ExpectedProfile`) and not only the game's current
one; `ReplacementProfileRule.CarriesRaidProfile` is that single rule. Recoil
Rework Legacy and Fontaine FOV Fix compared against `game.Profile` alone in 1.1.0,
so the Scav body was never handed over to them. By the decompiled mod and the
harness model, the Recoil singletons would then have stayed with the PMC corpse and
the mod's own `Awake` would have destroyed the new player object; this was never
observed in the game. For FOV Fix the foreign scale callback would have run against
the old main player. This is fixed after 1.1.0 (Core 1.0.18). Both mods are verified
offline only for the Scav switch (table below). A source guard
(`SourceGuardTests`) now fails the test suite if any Compat adapter
reads `game.Profile` for a body under construction outside that rule.

How each optional mod's handover is verified for the Scav switch:

| Mod | Evidence for the Scav switch |
|---|---|
| Recoil Rework Legacy 2.0.0+ | Harness only (`tools/RecoilLegacyCompatChecks`, production adapter against the pinned published DLL): a Scav-profile body created under the PMC game profile keeps its player object and owns both singletons, old components are released at commit, a failed replacement restores the old pair, an unrelated profile is left alone, PMC-to-Scav-to-PMC chains. Not run natively with the Scav switch. |
| Fontaine FOV Fix 4.1.0+ | The shared rule and the context (`tools/BodyReplacementChecks` case `profile-replacement`, unit tests, source guard). The adapter's own construction-time guard has no Unity-free harness. Not run natively with the Scav switch. |
| AI Limit, Healing Autocancel, Quest Tracker, Audio Accessibility Indicators | Native own-Scav run of 2026-10-29 (`STANDALONE-COMPAT-20261029.md`) plus their harnesses. |
| All other optional integrations | No decision depends on `game.Profile` (audited in source; the guard above keeps it so). They run the same shared handover as for a same-profile respawn and were verified natively or by harness only for ordinary respawns, not with the Scav switch. |

Adapters that read `body.Profile` (Quest Tracker quest data, Radar wishlist) use
the new body's own profile, which is correct for the Scav. Healing Autocancel and
PiP Disabler compare `game.Profile` only after the commit, together with
`game.LocalPlayer == body`, so they never apply to a body under construction.

## Failure and recovery limits

- A map must provide native Scav spawns and exits. BTR/transit item deliveries
  already made in the PMC phase prevent conversion; empty native transfer roots
  do not. Missing Scav exits or prior deliveries disable only the Scav choice
  before preparation, as does a raid reached by map transit. Transit is disabled and hidden during this first Scav
  implementation. BTR item delivery is blocked for the adopted Scav before items
  can move, including late purchase completion during raid teardown. Other BTR
  services and ordinary PMC/Scav delivery keep their native behavior.
- Required respawn-item cost, health, delay and protection use Redeploy's existing
  settings. The new option does not enable Fika support.
- Journal files live beside the server DLL in `data/pmc-scav/`. They can contain
  private profile snapshots and authorization tokens while a raid is pending.
  They are runtime state and must never be included in a release package.
- An unresolved prepared/active raid or ambiguous save failure blocks new raids
  for that profile until supported recovery completes. The main-menu flow below
  handles provable interrupted states. A partially applied native outcome with
  no complete recorded result remains blocked; preserve the journal, profile
  and logs for diagnosis. Deleting only the journal is unsafe.
- Pending loot transfer also prevents a new raid until it is completed. Old
  independent-Scav journals from protocol 1 require their original build/recovery;
  they are never interpreted as regular-Scav reservations.
- A final lost-insurance scan failure refuses settlement rather than inventing
  an empty claim. Ordinary PMC death remains available if preparation fails
  before the body switch: a failure that changed nothing returns to the PMC
  choices, any later failure ends the raid as a normal PMC death.

## Recovery and release-blocker fixes — 2026-10-28

Implemented and tested in candidate `6a1332d`. The recovery check runs when the actual main menu becomes ready, outside
raids. Accounts with no pending continuation see no recovery panel. Requests use
the signed-in account, and responses contain no private profile snapshot or
continuation token. Stale menu/session replies and repeated clicks cannot take
ownership of a newer operation; errors offer a status retry.

- **Unfinished Scav life:** choose **Abandon interrupted raid**, then confirm.
  The PMC remains dead, unfinished Scav loot is forfeited, and native Scav death
  and cooldown apply. This does not resume the abandoned raid. A fresh process
  can perform it without the old client's private continuation token.
- **Extracted loot:** choose **Resume loot transfer**. The native screen uses
  current authoritative profiles and only the remaining Scav inventory. Items
  already moved to the stash stay there; an old Scav snapshot is never uploaded
  to reinstall them. Completed inventory moves persist before acknowledgement.
- **Recorded save or acknowledgement:** a fully recorded outcome can be saved
  without rerunning native death, insurance, item movement or Scav generation.
  Exact memory/disk proof precedes native login changes. If further confirmation
  is needed, the panel offers **Complete interrupted save**. The original outer
  raid callback is completed with the exact recorded request so ArenaMode's
  ordinary-raid transaction does not remain active after a client-only restart.
- **After menu recovery:** the panel requires closing and restarting the game
  before another raid, ensuring menu controllers reload the resulting PMC,
  Scav and cooldown. **Close game** is available on the panel. Unproven partial
  native outcomes, incompatible old journals or changed profiles remain blocked.

The recovery path preserves the captured PMC insurance claim, including dropped
insured items, while excluding protected or already delivered items. A legacy
regular-Scav journal with insured gear but no provable insurance capture cannot
be abandoned automatically. New raids, regeneration and transfer operations stay
fenced while an outcome or outer acknowledgement remains unresolved. Concurrent
recovery attempts share the same exact outer completion, and acknowledged or
retired requests cannot be dispatched into a later raid.

Native SPT Sell All mails payment while removing the Scav's gear only on the
client. Allowing recovery between that payment and transfer completion would
offer the same gear again. Both client and server therefore reject Sell All for
the owned continuation transfer, with an explanation to move items to the stash.
This is a deliberate limitation; ordinary Scav transfers retain Sell All.

Source/build evidence: the integrated Client Release build against the installed
EFT assemblies passed, **469 client/Core tests** passed, and **149 server tests**
passed. Focused coverage includes eligibility/BTR/Sell All ownership, main-menu
session and callback guards, native save-cache retry, exact outcome persistence,
partial transfer, late control requests and outer-raid completion races. An
independent four-pass review resolved the login-mutation, raid-start race, Sell
All duplication and outer Arena lifecycle findings. The separate
[native acceptance record](PMC-SCAV-RECOVERY-20261028.md) covers the actual menu,
Prepared/Active restart recovery, partial transfer/resumption, subsequent raids,
Scav death, preflight and BTR movement controls, and ordinary Scav Sell All.
Native insurance, BTR rides/purchase UI, all maps and invited squads remain outside
that run's scope. An initial non-completing switch was not reproduced by the
clean multi-respawn repeat; its cause is not claimed resolved.

## Pre-change refusal and incoming transits — hotfix 1.1.1

Found in the 29 October release review: in the second or later raid of a map
transit chain the Scav button was enabled, but the server never registers such a
raid (`StartRejection`: `incoming-transition` or `non-fresh-transition`), so
preparation always failed with `no-issued-pmc-raid` and the failure ended the raid
instead of returning to the PMC choices. The same ending followed a server restart
during the raid, an older server mod without the Scav routes, or a connection
error.

- **Transit arrivals are detected on the client.** The start response the client
  keeps (`LocalRaidSettings.transition`) is read with the server's own rule:
  a nonzero `transitionCount` or a visited-locations list that is not empty marks a
  transit arrival. No transition data at all is a fresh raid. The Scav button is
  then disabled with the reason "Scav continuation is unavailable in a raid you
  reached by map transit." The PMC choices work as in any other raid. This check
  is a best-effort early disable: the server also refuses a request that carries a
  transition object even with count 0 and an empty history (`incoming-transition`),
  which the client cannot see in the start response. The probe below is the
  authoritative catch and returns the PMC choices with a readable reason either way.
- **A new read-only server question precedes any local change.** Before the client
  ends the dead PMC's statistics session (which adds that life's experience and a
  Killed session to the profile and cannot be reopened) it posts
  `/redeploy/pmc-scav/probe` with the raid's server ID. The server answers with the
  registration, pending-continuation, Scav-state and item-ownership half of `prepare`
  (the code is shared; an account Scav that shares item ids with the PMC is
  refused here too) and journals, reserves and generates nothing. `prepare` itself
  is unchanged and still validates everything again.
- **A refusal before any change returns to the PMC choices.** If the pre-checks
  fail, the probe is refused, the server does not answer (a connection error or a
  timed-out request counts as no answer), the respawn-cost item is gone from the
  corpse or the raid ended, the dead PMC, its profile, the respawn-cost item and
  the Scav are untouched. While the raid still runs and a PMC respawn is possible,
  the prompt is shown again; the Scav button stays disabled with the reason for
  the rest of that death, and a warning notification says that the PMC respawn
  choices are still open. When the raid itself ended meanwhile, no prompt is
  shown and the native ending runs.
- **Anything after that point keeps the old fail-closed ending.** Once the session
  has been ended (the `prepare` request and everything after it, including the
  cost commit and the respawn pipeline) a failure ends the raid as a normal PMC
  death, as before. A held reservation, an unsafe cost restoration or a prepared
  context also never returns to the prompt. A server restart that happens between
  the probe and `prepare` therefore still ends the raid; this window is a few
  frames.
- **Readable messages.** Known server codes (`no-issued-pmc-raid`,
  `previous-continuation-pending`, `regular-scav-unavailable`, `scav-cooldown`
  and the reservation-state codes) are mapped to plain sentences. The raw code is
  kept in the BepInEx log line (`[server code: ...]`) but not shown to the player.
  The probe codes are worded for the probe too (it never looks at the PMC death).
  A new test fails if `ScavContinuationService` adds a `Rejected("...")` or
  `Error = "..."` code without a client text; codes built any other way, or in
  other server files, are not covered by that scan.

Tests: `ScavEligibilityTests` (client eligibility rule, pre-change classification,
failure handling with the real `RespawnService`/`PmcScavSession` code, code map,
ordering of the probe before the session end, planted-bug controls) and
`ScavProbeTests` (server probe against the same states as `prepare`, nothing
journaled or written).

**Evidence limit.** The probe route, the refusal re-offer and the client-side
transit detection (which reads `LocalRaidSettings.transition`) have so far been
verified by these unit tests and the offline harness stubs only; none of them has
run natively in a game yet. The "Refused preparation" row and the extended
"Preflight" row of the table below are therefore requirements, not recorded
evidence.

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
| Restart recovery | Prepared/Active and partial-transfer restart; explicit abandon/save/remaining transfer; no repeated loot, insurance or cooldown; new raids work afterward |
| Client-only restart | Recovery clears the exact ordinary-raid transaction while the server and ArenaMode remain running |
| Restrictions | Owned Scav BTR delivery and Sell All are denied before item/payment effects; ordinary controls remain native |
| Preflight | Missing Scav exits/prior delivery/map-transit arrival disable only Scav; ordinary PMC respawn remains usable |
| Refused preparation | Server cannot register the raid, was restarted or is an older build: prompt returns with PMC choices, Scav disabled with a readable reason; no respawn item, statistics or Scav state consumed |
| Regression | Normal PMC respawn works with the additional Scav choice enabled by default and explicitly disabled; normal Scav respawn remains functional |

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
set in the existing installation configuration; at that point the code default was false.
Other settings and profiles were preserved. Original binaries and configuration
are backed up outside synchronization in
`C:\Temp\RedeployScavDeployment-20261028-130643-ae84a41f` on the development host.

SPT-GPU-2 was initially stopped, but another session started an Arena test before
deployment finished. Its client/Core files and enabled configuration reached
disk, while the running server kept the previous Server DLL locked. The user
will stop/restart that host themselves; no raid was interrupted. The server
update must finish synchronizing while both game and server are closed before
the feature is used there. This deployment records disk hashes, not a new
running-process or raid acceptance test. As of 2026-10-28 the feature branch remained unmerged
and unpublished.

## Fresh-raid transit registration correction — 2026-10-28

The user's normal Factory PMC raid was rejected with `no-issued-pmc-raid` at
13:25:15 local time. Its native transit log reported `Common`, `Count:0`; PFT
also reported `continuation=False`. The prepare and ordinary final-death requests
used the same server-issued raid identity. This was a Redeploy registration bug,
not an expired Scav cooldown or an identity mismatch.

`COMMON` and `EVENT` describe available map transitions on a fresh raid. The old
registration guard accepted only null/`NONE`, so it never recorded this otherwise
valid PMC start. Registration now accepts known flags for fresh starts, while
rejecting an incoming request transition or a response with nonzero/missing hop
count or nonempty/missing visited history. Arena and Scav starts remain excluded;
preparation still requires the exact server-issued PMC raid ID. Registration and
prepare rejection reasons are logged at Debug without profile IDs or tokens.

Offline validation: **103/103 server tests**, including 17 added cases for fresh
flags, actual/malformed transit, stale authorization removal, Arena/Scav exclusion
and exact raid identity. Restoring the old guard in an isolated negative control
reproduced the failure for `COMMON`, `EVENT` and their combination; the corrected
source was then restored and the complete suite passed again. An independent
source review checked the rule against native `StartLocalRaidAsync` and Arena's
ordinary/start-marker paths. Client and Core were not changed or rebuilt.

Corrected Server SHA-256:
`CC70BB90D488022F49576DC1BA85281A1821F4037835F67F6815254B3061871F`.

Native follow-up on SPT-GPU-1 passed with that Server DLL and the unchanged
candidate 3 Client/Core. The real PMC Factory start at 13:46:41 local time logged
`issued; transitFlags=1, incomingTransition=False, transitionCount=0, visitedCount=0`;
EFT independently logged `Flag:Common, Count:0`. After a test-triggered PMC death,
the native prompt offered **Continue as Scav**, prepare/activate succeeded and
the live player's profile ID matched the baseline account Scav. The journal also
matched its original equipment root. Eight AI bots were alive in the raid;
PitFireTeam was installed without invited companions, and ArenaMode stayed inactive.

A test-triggered Scav death ended the raid without another respawn, completed the
native result screens and returned to the main menu. The journal was `Committed`
with no error; the persisted profile had a successor Scav equipment root and a
future regular cooldown. No client error was logged during this Common-raid
continuation. Native extraction/loot transfer was not repeated in this focused
registration test; its earlier candidate 3 coverage remains separate.

Test setup matters: opening **GAME SETTINGS** in the pre-raid menu can lose the
Common flag through EFT's settings-clone/apply path. The first automated attempt
with `--bots as-online` still produced `None`, so it was exited and not counted as
Common coverage. The successful `gpu-run raid --mode pmc --map factory` omitted
`--bots` and kept Common; verify the actual start diagnostics in future tests.
The squad helper also could not read PFT 1.0.0's new roster screen; the visible
group contained only the local player. Neither issue required a Redeploy change.

The corrected Server DLL/PDB were installed in `D:\SPT41` and on SPT-GPU-1 before
the test. SPT-GPU-2 was later observed with game/server stopped and the corrected
Server DLL already synchronized (`CC70BB90...`); no process there was interrupted
or tested. A server restart and a newly started PMC raid are required wherever an
older server process is still running. This fix cannot authorize a raid that the
old process never registered.

For this follow-up, Syncthing's SPT41 folder on SPT-GPU-1 was paused before any
test and a new current baseline was captured after installing the corrected
Server DLL/PDB. At **12:54:36 UTC**, all **10,092** baseline entries were restored
and verified, and **76** newly created test files were removed from the managed
directories. The original synchronization state was restored (`paused=false`);
the other folder was untouched. Game/server were stopped for restoration. The
test profile was independently hash-checked afterwards, and the corrected
Server DLL/PDB matched the build on all three installations. No temporary mod
was installed. Private evidence and this run's backups remain outside sync in
`C:\Temp\RedeployScavCommon-20261028` on SPT-GPU-1 and
`C:\Temp\RedeployScavCommonDeploy-20261028` on the development host.

## Explicit respawn choice and configuration menu — 2026-10-28

The default-on Scav setting now adds a choice instead of replacing ordinary
respawn. The death prompt offers **PMC near corpse**, **PMC at spawn point** and
**Continue as Scav**. Ordinary Scav raids retain their same-profile respawn and
use Scav labels for the first two buttons. The selected body type is captured
before the respawn delay. Changing a setting during that delay cannot change
the decision. A known Scav cooldown or other pre-selection unavailability
disables only the Scav button; both choices retain the existing respawn limit
and item-cost checks. Transaction failures after Scav preparation still fail
closed; this change does not reopen a potentially reserved Scav transaction.

The F12 menu keeps all nine original configuration identities and saved values.
It uses readable labels, explicit units and General / Respawn / Diagnostics
ordering. Only detailed pipeline measurements are advanced. ConfigurationManager
shows matching advanced entries while searching, regardless of its Advanced
filter; they are hidden in the normal unfiltered view. Existing explicit
`RespawnAsScav=false` is preserved; new or missing entries default to `true`.

Offline checks: Client Release build without warnings/errors, **394 client/Core
unit tests passed** (including 43 production-boundary choice tests and two
negative controls), and **76 config checks passed** against the installed
BepInEx/ConfigurationManager parser. The server and shared Core were unchanged.

Focused native acceptance used SPT-GPU-1, SPT 4.1.6 / EFT 40743, two ordinary
Factory PMC raids with the native `Common` flag. ArenaMode was installed but
inactive; PitFireTeam had no invited squad. Client SHA-256:
`1D74A59FBD5A3D8805E0150CA9B1046740BCD23AB17D118F84FE7CA34C6A659E`.
The exact tested DLL/PDB were retained; its informational Git stamp names base
`616b8ee`, since compilation preceded this change's commit. Loaded code and the
host/development copies matched in UiBridge. Core remained
`25DE9FB8F3853EFD1EC3B18EA650777ECFD6D21E02189D5AE6B3D2FEDBC4CA9C`,
Server remained `CC70BB90D488022F49576DC1BA85281A1821F4037835F67F6815254B3061871F`.

| Check | Result |
| --- | --- |
| F12 menu | Labels, order and units were visually checked at 1920x1080. Diagnostics appeared with Advanced and disappeared without it after clearing the search. |
| Death prompt | Both PMC buttons, Scav and End raid were visible and legible; cooldown text also fit. |
| PMC while Scav enabled | At 16:18 local time, Near corpse returned the same PMC profile with empty ordinary equipment, preserving melee/container. This fixture started without a firearm, so it did not re-test firearm removal. No Scav prepare occurred for this choice. |
| Later Scav choice | After another PMC death, Continue as Scav activated at 16:23:54. The live profile ID and reserved equipment root matched the baseline account Scav, with its M4A1, sidearm and other equipment. |
| Scav death | The health-controller kill hook ended the raid without another prompt/body despite a configured limit of five. Native result screens returned to menu. Settlement was Committed, with no journal error, a regenerated Scav equipment root and a future normal cooldown. |
| Cooldown fallback | In the second PMC raid, the Scav button was disabled with 23 minutes remaining. PMC at spawn point returned the original PMC alive at 16:27:28. Disconnect, result screens and healing completed. Exactly one new continuation journal was created across both raids. |

Client log: `LogOutput-SPT-GPU-1-2026-10-28_15-50-09.log`. No Redeploy/Core
warning or error appeared. The log was not globally error-free: microphone
initialization failed at startup; ordinary disconnect logged an inactive
MenuScreen coroutine and one `BotWeaponSelector.OnWeaponTaken` null reference
at 16:27:40 during teardown. These did not interrupt either result flow. Their
origin was not established by this focused test and is not claimed fixed.
Extraction/loot transfer, ordinary Scav raids and fault recovery were not
repeated here; their previous validation and limitations remain applicable.

The current Client DLL and matching PDB were hash-verified in `D:\SPT41`,
SPT-GPU-1 and SPT-GPU-2. Only GPU-1 was restarted and tested; the GPU-2 check
establishes files on disk, not which code its process has loaded. Existing Core,
Server and configuration files were retained. No temporary test mod was added.

Syncthing's SPT41 folder on GPU-1 was paused for the entire test. After stopping
game/server, all **10,039** current baseline files were restored and hash-verified
at **15:32:33 UTC**; **76** test-added files were removed from the managed
directories. The original `paused=false` synchronization state was restored;
the other folder was untouched. The baseline was taken after installing the
Client, so restoration retains the exact tested DLL/PDB. Private backups and
evidence are outside sync in `C:\Temp\RedeployChoice-20261028` on GPU-1 and
`C:\Temp\RedeployChoiceDeploy-20261028` on the development host. Profiles and
private journals/logs are not committed.

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

# PMC-to-Scav release readiness — 2026-10-28

Original reviewed source: `7037a25` on `feature/pmc-to-scav`, against `main` at
`ecf40d9`. The findings below describe that source, including its historical
line references. Candidate `6a1332d` now implements their corrections.
**All three confirmed blockers are resolved.** 469 client/Core tests and 149
server tests passed, followed by focused native recovery and ordinary-control
tests on SPT-GPU-1. Exact binaries, scenario limits and host restoration are
recorded in the [native acceptance report](PMC-SCAV-RECOVERY-20261028.md).
Code consolidation does not publish a release; package/version work remains below.

## Implemented corrections and acceptance

| Finding | Candidate implementation | Evidence |
| --- | --- | --- |
| R1: interrupted continuation | Authenticated main-menu status, confirmed abandon, recorded-save completion and native remaining-loot transfer; durable per-operation persistence and exact outer raid acknowledgement | Prepared/Active and partial-transfer restart passed, including Active recovery with the same server process. Actual subsequent PMC raids started after all three cases. Native serialization hashes matched the recorded outcomes; no duplicate loot/cooldown. Failed-save/concurrent-request paths additionally have offline tests. |
| R2: owned Scav BTR delivery | Deny the adopted Scav's delivery before item movement and at late purchase completion, with an explanation | Real patched native item movement accepted the temporary test item for ordinary PMC and Scav, and refused it for the adopted Scav. Ownership cleanup preserved all prior item owners. Purchase/late-callback scope has offline coverage; a BTR ride/priced UI purchase was not tested. |
| R3: unavailable choice | Read-only exit/delivery prerequisites before selection and immediately before Prepare; disable only Scav and retain PMC choices | Missing-exit/prior-delivery reasons appeared before selection; ordinary PMC near-corpse and spawn-point respawns succeeded. |

R1 does not restart the interrupted raid. Explicit abandonment preserves the
PMC death and applies the unfinished Scav's native death/cooldown, forfeiting
its remaining loot. Extracted recovery opens current authoritative inventories;
already transferred items are not reinstalled from a saved Scav snapshot. A
complete recorded result is persisted only with exact memory/disk proof before
native login changes. Unproven partial effects stay blocked. Menu recovery
requires a game restart before another raid so all menu controllers reload the
updated profiles.

The independent follow-up review also corrected four integration defects:
normal login invalidating exact-save proof, raid-start/transfer retirement races,
native Sell All payment duplication, and an outer ArenaMode ordinary transaction
left active after a client-only crash. Final outer completion retains the exact
canonical request, coalesces concurrent attempts, fences new raids until it
finishes, and acknowledges only the committed matching result. Delayed control
requests cannot cancel an already requested recovery; a genuine late result can
win without being replaced by a second death. No ArenaMode protection was weakened.

**Sell All is unavailable in an owned continuation transfer.** Native SPT pays
via mail but removes that equipment only on the client; interruption before
transfer completion would otherwise make the same server inventory recoverable
again. Client and server reject the sale before its effects. Move wanted loot
to the stash and finish the transfer. Ordinary Scav Sell All is unchanged.

Current offline evidence: the integrated native-assembly Client Release build
passed, the full client/Core suite passed **469 tests**, and the server suite
passed **149 tests**. The independent four-pass review found no remaining
actionable source defect in its final scope. The native tests separately verified
owned-transfer Sell All denial without payment/item loss, while ordinary Scav
Sell All paid exactly 2,044 RUB and removed its sold item. Normal continued-Scav
death ended the raid without another respawn prompt. The initial switch that
did not complete before termination was not reproduced in the clean two-PMC-
respawn repeat; its exact cause remains unknown and is documented, not claimed fixed.

## Original required fixes at `7037a25`

### R1 — P1: interrupted continuation can block all subsequent raids

Choose the own-Scav option, then terminate EFT before the combined raid result
is saved. The durable journal remains `Prepared` or `Active`; neither expires
out of the new-raid guard. The new client process has lost the continuation
context/token and has no supported recovery or abandon action. The server's
start prefix applies to ordinary PMC and Scav starts too, so the profile cannot
start another raid until its pending transaction is resolved manually.

Evidence:

- `Redeploy.Server/Scav/ScavJournal.cs:93-103`: unresolved body phases block
  starts even after preparation expiry.
- `Redeploy.Server/Scav/ScavContinuationService.cs:72-83`: restart reconciliation
  only completes transactions whose expected saved profile already matches disk;
  it cannot resolve the unfinished body phases above.
- `Redeploy.Server/Patches/ScavContinuationPatches.cs:18-22`: the guard precedes
  every native raid start for that profile.
- `Redeploy.Client/Services/PmcScavSession.cs:56-61`: the active context and
  authorization data are process-local.
- `LostActivatePreparedJournalBlocksNewRaidEvenAfterExpiry`,
  `EveryUnresolvedBodyPhaseFencesNewRaids` and
  `UnresolvedPreparedJournalRejectsAnotherGeneration` exercise these guards.

Add a supported, verified recovery/abandon path before release. Preserve
identity, insurance, cooldown and exactly-once persistence checks. Expiry alone
cannot prove a body was never activated: the activation acknowledgement may
have been lost. Simply dropping the guard or deleting the journal is unsafe.
An unresolved loot transfer also needs a usable post-restart resolution path;
partial transfers must never replay items already moved to the stash.

### R2 — P1: BTR delivery after switching prevents settlement

The owned Scav can still use the native BTR item-delivery service. Native EFT
initializes its transfer stash for an entering local player, explicitly supports
the Savage price modifier and includes the BTR stash in the final raid report.
Redeploy's client only blocks transit after the switch. Its server then refuses
the nonempty delivery report before settlement, leaving the journal blocking
subsequent raids as in R1.

Evidence:

- `Redeploy.Client/Patches/PmcScavPatches.cs:147-172`: transit-only guards.
- `Redeploy.Server/Scav/ScavContinuationService.cs:291-295`: real transfer
  contents reject the final report before changing the journal to `Settling`.
- `EndAcceptsEmptyNativeTransferStashAndRejectsRealContentsBeforeSettlement`
  confirms rejection and the retained unfinished journal.
- Local EFT 40743 source: `EFT.Vehicle/BtrController.cs:685-690`,
  `EFT/BtrTransferItemsController.cs:48-63`,
  `EFT.UI/TraderDialogScreen.cs:131-139`, and
  `EFT/BaseLocalGame.cs:1036-1051` establish the native delivery/report path.

Either disable item delivery before it can accept items for the owned
continuation Scav, with a visible explanation, or implement full transactional
delivery support. Keep ordinary PMC/Scav BTR services unchanged. Empty native
transfer roots must remain accepted. Do not discard delivered contents from
the final report to make validation pass.

### R3 — P2: known unavailable Scav choices end the raid unnecessarily

`UnavailableReason` checks patch readiness, the regular Scav profile and cooldown,
but not missing Scav exits or a delivery already made during the PMC life. The
Scav button is therefore enabled in these known unsupported cases. `Prepare`
rejects the choice and `RespawnService` ends the raid, losing the still-valid
ordinary PMC respawn option without any Scav reservation having been made.

Evidence: `Redeploy.Client/Services/PmcScavSession.cs:65-75,145-153` and
`Redeploy.Client/Services/RespawnService.cs:221-228`.

Evaluate those read-only local prerequisites before selection and display a
disabled Scav button with the reason. Retain PMC choices. Preserve the current
safe handling of ambiguous failures after preparation has begun; this is not
permission to reopen an ordinary respawn after an unconfirmed transaction.

## Acceptance plan used for the fixes

- Reproduce restart at prepared, activated and pending/partially completed
  transfer states on an isolated test profile. Demonstrate a supported way back
  to normal raids and no duplicate loot, insurance, cooldown or settlement.
- Include both client+server restart and a client-only Active restart while
  the server retains ArenaMode's ordinary-raid transaction. Confirm visible
  abandon/save/resume actions, the restart requirement and a subsequent normal raid.
- Verify post-switch BTR delivery is either visibly unavailable before item
  movement or settles correctly. Check ordinary BTR use remains unaffected.
- Verify owned-transfer Sell All is visibly unavailable and cannot pay or
  consume inventory; ordinary Scav Sell All keeps its native behavior.
- Verify missing Scav exits and prior deliveries disable only the Scav option;
  the ordinary PMC respawn still succeeds.
- Repeat focused own-Scav extraction/transfer and death on the resulting build.
- Record exact loaded candidate identities and scenario limits, restore test
  profiles/configuration and verify hashes, and restore the original synchronization state.

The previous successful Factory tests remain valid for their recorded scopes.
No additional break in shared Core's binary API or normal same-profile requests
was found in this review. Invited squads, every map and long combat sessions are
coverage gaps, not additional confirmed defects from this audit.

## Review evidence and publication work

The original review used two independent source reviews for Server persistence
and Client/Core behavior. A focused nine-case server run passed; the primary
review separately reran the expiry/unresolved-state tests against the production
code at that time. These
original tests confirmed the protective rejection behavior before recovery was
implemented. Native EFT source corroborated R2; no native crash or BTR test was
run during that audit. No game/server process, test profile or synchronization
setting was changed by that audit. Its installed Server DLL retained its
SHA-256 `CC70BB90D488022F49576DC1BA85281A1821F4037835F67F6815254B3061871F`.

The completed R1-R3/Sell All checks support the requested consolidation after
test-host restoration. The [native report](PMC-SCAV-RECOVERY-20261028.md) and
[repository status](REPOSITORY-STATUS.md) record that completion. The published
package is still Client/Server 1.0.2 with Core 1.0.13; the development feature uses
Core 1.0.16. Publishing a new package
also requires a new synchronized Client/Server version, a committed-source build
of all three DLLs, final smoke validation, secret/package checks, VirusTotal
reports and the release ZIP/notes. Runtime profiles, configuration, journals
and test probes must not enter the package. No public release has been created
by these corrections or this readiness review.

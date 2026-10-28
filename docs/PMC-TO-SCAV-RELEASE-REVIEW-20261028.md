# PMC-to-Scav release readiness — 2026-10-28

Reviewed source: `7037a25` on `feature/pmc-to-scav`, against `main` at
`ecf40d9`. **Not ready for a public release.** The requested merge, feature
branch/worktree removal and push were conditional on release readiness and
remain deferred. This review does not change the deployed binaries.

## Required fixes

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

## Acceptance needed after the fixes

- Reproduce restart at prepared, activated and pending/partially completed
  transfer states on an isolated test profile. Demonstrate a supported way back
  to normal raids and no duplicate loot, insurance, cooldown or settlement.
- Verify post-switch BTR delivery is either visibly unavailable before item
  movement or settles correctly. Check ordinary BTR use remains unaffected.
- Verify missing Scav exits and prior deliveries disable only the Scav option;
  the ordinary PMC respawn still succeeds.
- Repeat focused own-Scav extraction/transfer and death on the resulting build.

The previous successful Factory tests remain valid for their recorded scopes.
No additional break in shared Core's binary API or normal same-profile requests
was found in this review. Invited squads, every map and long combat sessions are
coverage gaps, not additional confirmed defects from this audit.

## Review evidence and publication work

Two independent source reviews covered Server persistence and Client/Core
behavior. A focused nine-case server run passed; the primary review separately
reran the expiry/unresolved-state tests against current production code. These
tests passing confirm the current protective rejection behavior; they do not
mean recovery is implemented. Native EFT source corroborates R2; no native
crash or BTR test was run during this audit. No game/server process, test profile
or synchronization setting was changed. The installed Server DLL retained its
SHA-256 `CC70BB90D488022F49576DC1BA85281A1821F4037835F67F6815254B3061871F`.

After R1-R3 are resolved and tested, merge the feature and update the current
development status. The published package is still Client/Server 1.0.2 with
Core 1.0.13; the development feature uses Core 1.0.16. Publishing a new package
also requires a new synchronized Client/Server version, a committed-source build
of all three DLLs, final smoke validation, secret/package checks, VirusTotal
reports and the release ZIP/notes. Runtime profiles, configuration, journals
and test probes must not enter the package. No release was requested or created
by this readiness review.

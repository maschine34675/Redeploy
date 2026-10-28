# Own-Scav recovery validation — 2026-10-28

Status: scoped native acceptance passed; test-host restoration and consolidation
are recorded below. This is not a public release.

## Candidate and scope

Production source: `6a1332d`. Client/Server 1.0.2; shared Core 1.0.16 unchanged.
SPT-GPU-1, SPT 4.1.6 / EFT 40743, Factory PMC raids with the native Common
transit flag. ArenaMode was installed but inactive; its server-side ordinary-raid
protection remained enabled. PitFireTeam was installed without invited companions.
No claim is made for Arena gameplay, all maps, BTR rides/pricing, or multiplayer.

The dev-only `ScavRecoveryNativeProbe` exercises bounded native entry points and
reports their ownership checks. It was installed temporarily for these tests
and is excluded from the final installation and release packaging. Its first
build was from `6a1332d`; read-only diagnostics were added
in `1c76459`. Production binaries did not change for this probe update.

| Loaded production component | SHA-256 |
| --- | --- |
| Client | `297A9D0C1042295C477070253EDD819BB1EF9B211DE1C25A5704EFC88466CCFD` |
| Server | `E051FE2B0AB061354875EA28CB67A59FB5FCACEDB55E26B31CF3162679C962B4` |
| Core, existing installation | `25DE9FB8F3853EFD1EC3B18EA650777ECFD6D21E02189D5AE6B3D2FEDBC4CA9C` |

UiBridge verified the Client's loaded `6a1332d` stamp against the GPU disk after
each restart. The fresh server process used the hash-verified Server candidate.
The development installation still had the preceding binary during these tests;
that intentional difference was not treated as runtime validation of that host.

## Completed checks

| Scenario | Evidence |
| --- | --- |
| Missing Scav exits | Owned temporary empty exit array produced `This location has no Scav extraction points.` before selection. Both PMC choices remained visible; ordinary near-corpse respawn succeeded after restoring the array. |
| Prior delivery | One synthetic item in an owned native transit grid produced the BTR/transit unavailability reason. Removing only that test item/grid and choosing PMC at a spawn point succeeded. |
| Ordinary PMC BTR movement | Actual patched native transfer method moved the synthetic item: temporary count 1→0, delivered 0→1; item owners 181→181 after cleanup. |
| Adopted Scav BTR movement | Same native method left temporary count 1→1 and delivered 0→0; item owners 132→132. No real inventory item moved. This does not exercise a BTR UI purchase. |
| Prepared interruption, client and server restart | Server journal was `Prepared`, insurance snapshot marked captured. After restart the menu offered explicit abandon with confirmation. The result became committed/abandoned, normal Scav cooldown applied, and no duplicate item IDs appeared. A new native PMC raid then started; the journal retired. |
| Active interruption, client-only restart | Native replacement was alive/Savage, journal `Active`. EFT was killed while the server remained running. Menu recovery completed, with zero duplicate item IDs and a committed/abandoned result. A subsequent native PMC raid started on the same server process (PID 11208, started 21:52:58), retiring the journal. |
| Native extraction and Sell All restriction | The adopted Scav used the actual Camera Bunker Door extraction trigger. The native transfer screen opened. Sell All displayed its restriction notice; both inventories, all currency totals and mail/attachments remained unchanged. |
| Interrupted partial transfer | One UXPRO item was moved through the native quick-move action, persisted in the PMC stash, and absent from Scav inventory before terminating EFT. After a server/client restart, Resume loot transfer opened the native screen with only the remaining 51 Scav items. The transferred item still existed exactly once in the PMC stash. |
| Resumed transfer completion | Sell All remained blocked in the recovery-owned screen. A rig and its 14 children moved normally, then the native confirmation explicitly forfeited the remaining loot. The transfer completed, generated a new Scav and applied the regular cooldown. No duplicate item IDs appeared. After restarting EFT, another ordinary PMC Factory raid actually started (66-second recipe). |
| Two PMC respawns followed by Scav | Clean fixture without exit/grid/acknowledgment faults: near-corpse PMC respawn, PMC spawn-point respawn, then the actual account Scav. The final switch succeeded in about 11 seconds; current body/profile matched, journal Active, no duplicates. |
| Continued Scav death | Death of that adopted Scav ended the raid without another respawn prompt. Native result screens completed in 27 seconds. Journal committed/complete with Extracted=false, successor and cooldown saved, no duplicate item IDs. |
| Ordinary Scav control | Native Scav near-corpse respawn succeeded without the continuation choice. BTR movement accepted the temporary item (`adopted=false`, moved=1, owners 142→142). Actual Camera Bunker Door extraction and native Sell All succeeded: the retained knife disappeared and one Fence mail contained exactly the quoted 2,044 RUB. No recovery dialog, new continuation journal or duplicate item IDs appeared. No synthetic loot was added. |

An independent read-only audit loaded the saved profiles with SPT's real
`JsonUtil`/`SptProfile` serialization. Both abandonment results matched the
journal's expected profile hash. Their 416-item PMC stash trees were unchanged,
all protected items remained, no old Scav item was regenerated, and both cooldowns
were exactly 1,500 seconds under the installed native configuration. PMC XP/level
were unchanged. The active journal additionally retained a frozen snapshot for
exact Skills comparison; the prepared journal had already compacted that snapshot.

The partial transfer conserved all 478 pre-transfer PMC+Scav item IDs: exactly
one item changed placement, with no new/deleted/duplicate IDs or money/mail
change. The original extraction hash remains an extraction checkpoint; completed
transfer-mutation snapshots are compacted, so it is not a post-move hash.
The persisted profile and subsequent real restart supplied the latter evidence.
After completion, all 16 transferred item IDs were present exactly once in the
PMC stash, and all 36 explicitly forfeited old-Scav IDs were absent. The new
Scav retained the account's Scav profile identity but had a fresh equipment root
and 29 fresh item IDs. The final canonical profile matched the recorded
`TransferExpectedProfileHash`; the cached native completion response matched
the successor and its 1,500-second cooldown. No transfer mutation remained open.

The first attempted switch after two ordinary PMC respawns did not complete
before the controlled termination. Its preparation request had succeeded, but
the local body remained the dead PMC and activation acknowledgment had not been
reached. The original status probe did not locate the exact wait; resource
preloading is a possible cause, not an established diagnosis. This is evidence
for a prepared transaction interrupted before adoption, not for an
adopted body whose activation acknowledgment was lost. A fresh-process first-death
Scav switch completed in about 24 seconds. The clean two-PMC-respawn repeat with
read-only diagnostics succeeded in about 11 seconds. The initial stall's cause
is not established; it is not claimed fixed or reproducible. The failed run had
also used the explicit temporary exit/delivery probes and armed acknowledgment
suppression, which had not yet been reached. No production behavior was changed
to conceal this observation.

UiBridge's godmode hook rejected the adopted Scav because it classified the old
PMC as another human. That hook was not forced or bypassed. The scoped BTR probe
independently required the exact single-player LocalGame/current adopted body.

## Offline verification

- Client builds against native EFT assemblies: zero errors; eight obsolete API
  warnings for the existing native raid-state property.
- 469 client/Core tests and 149 server tests passed.
- Independent persistence/lifecycle review resolved login mutation, new-raid
  retirement, Sell All duplication and outer Arena callback ordering findings.
- Probe builds: zero warnings/errors. Parent reverified its bounded-state rules.
- Gitleaks 8.30.1 scanned the 15 commits ahead of `origin/main` through `1c76459`:
  no findings. Final documentation scanning is recorded at consolidation.
- The native fixture contained no insured items. Insurance filtering and
  exact-once handling have offline coverage, not a new native insurance claim.

## Test logs and limits

Main client logs (all `LogOutput-SPT-GPU-1-2026-10-28_*.log`):

- `21-37-01`: preflight/ordinary-PMC controls and Prepared interruption.
- `21-46-11`: Prepared recovery; `21-48-30`: subsequent raid.
- `21-53-20`: active Scav and BTR restriction; `21-57-15`: client-only recovery.
- `21-59-28`: subsequent raid with the original server still running.
- `22-03-39`: extraction and first partial transfer; `22-09-21`: resumed transfer.
- `22-13-47`: actual PMC raid after recovered transfer completion.
- `22-20-08`: clean two-PMC-respawn/Scav-switch and final Scav death.
- `22-25-58`: ordinary Scav respawn, extraction and native Sell All.

The final three scenarios were run by a delegated native-test worker under an
exclusive GPU1 handoff; the parent checked the saved profile summaries and payment
evidence. The parent also independently reran all three native serialization-hash
comparisons for Prepared, Active and transfer-completion recovery.

Startup emitted the host's existing microphone/media and native Factory
collider/spawn-point diagnostics. The normal-flow controls reported no new
Redeploy errors. The initial non-completing switch remains an unexplained
observation, not an established fix. Real disk-write failures, insurance returns,
concurrent requests and late callbacks have automated/source coverage where
documented, rather than equivalent fault injection in this live profile.

## Restoration and deployment

The host's SPT41 Syncthing folder was paused before testing. A fresh backup
contains 10,049 baseline files. Between independent scenarios only the isolated
test profile and Redeploy journals are reset while game/server are stopped.
Full restoration finished at **2026-10-28 21:36:33 UTC**: all **10,049** baseline
files matched their saved hashes, **62** new test-state files were removed, and
the original Syncthing state was restored to `paused=false`. The temporary probe
DLL/PDB were separately verified against their built hashes and removed.

After restoration, the exact tested Client/Server DLLs and their matching PDBs
were deployed to `D:\SPT41` and `C:\SPT41` on SPT-GPU-1 and verified by SHA-256.
Core and its PDB remained unchanged and matched the tested installation. The
final copy was a disk verification; no additional gameplay run after restoring
the profiles is claimed. EFT and the server were left stopped. GPU2 was neither
controlled nor restarted, and synchronization is not a runtime acceptance claim.

Private profiles, journals and raw logs live
outside the repository/synchronized tree under
`C:\Temp\RedeployRecoveryEvidence-20261028` and
`C:\Temp\RedeployRecovery-20261028` on SPT-GPU-1; they are not committed.

# Raid Review body-replacement checks

This harness compiles the production `Redeploy.Core/Compat/RaidReview` files and runs them with the installed **real HarmonyX** runtime against a small Raid Review fixture. Fixture member signatures, packet properties and enum values are independently compared with the SHA-pinned **released Raid Review 1.10.0 client DLL**, rather than treating the fixture as proof of the external contract. It also builds the complete Core against the installed EFT assemblies with `SPTModType=Library` (no deployment).

Run from the repository:

```powershell
./tools/RaidReviewChecks/Verify.ps1
```

Requires Windows/.NET Framework 4.7.2, .NET SDK, Node with TypeScript stripping and `node:sqlite` (tested on Node 26.3.0), installed EFT/BepInEx references, and NuGet access/cache for Newtonsoft.Json 13.0.3. `-PublishedDll`, `-SourceRoot`, and `-EvidenceRoot` override local audit inputs/output. Missing reference inputs are downloaded from the pinned upstream release/commit into ignored test artifacts. The DLL and three imported upstream helpers are hash checked. Nothing is copied into the game installation, and no game/server is started. `-SkipMutants` is a quicker development pass, not full acceptance.

Success: `RAID_REVIEW_CHECKS_PASSED`, with the exact evidence directory. Evidence includes build/runtime logs, emitted projected packets, CSV round-trip fixture, upstream replay results, and one build/failure log for each mutant. Mutation execution requires the intended assertion failure; a compile failure or unrelated crash does not count as rejection.

## Contract and semantics

- Reviewed client: Raid Review **1.10.0**, plugin `ekky.raidreview`, SHA-256 `1D10AFA713CB520FE2D43D271526C817FAF51D58071F76D9CE85FF3BA4777872`.
- Upstream source: `Chazut/SPT-RaidReview`, commit `b2a7a9b46fbfb46137a76b0e190f318d230d3208`. The replay check imports the actual `playerStatuses.ts`, `playerPresence.ts`, and `playerIdentity.ts` files. The fixture is a reduced reproduction, not a copy of RR's full Unity runtime.
- Core preserves real START/END session and recording-owner IDs, authentication/game profiles, and entry raid type. Only detached recording snapshots use `<real-profile>:redeploy:<life>`, including the first recorded physical life. Names show `[life N]`.
- RR displays **separate rows and statistics for each life**, not one aggregated player row. A PMC-to-Scav recording remains labelled with its entry raid type. The Scav participant metadata records the actual faction and its explicit final extraction/death/removal status.
- No synthetic recording restart per respawn, global RR reset, RR server component change, or extra Redeploy.Server dependency is used.
- Startup buffers at most 256 snapshots for at most ten seconds, then faults RR's recording explicitly if ownership cannot resolve. It uses an actual current Core driver claimant, not merely “a driver is registered”. A verified replacement before queue flush establishes ownership even if a same-frame final Stop releases the driver.
- If Core's entry replacement is still creating when RR's original start handler runs, that **original handler** is deferred once until the attempt settles, bounded at 35 seconds (Core's own creation deadline is 30 seconds). The uncommitted candidate never becomes a recorded life. Ordinary start callbacks are unchanged.
- Every committed life immediately publishes metadata and inventory so rapid swaps cannot lose participant rows between RR's five-Hz samples. RR's own inventory settings and capture function remain in charge of the inventory snapshot.
- Physical callbacks preserve old grenade/bullet/corpse identities. Candidate registration is not a commit. Failed, abandoned and retired body callbacks cannot poison the new body's profile-keyed roster caches. Core construction inventory moves are not logged as ground loot; ordinary post-replacement looting is retained.
- Unknown local string-only kill/shot/loot/inventory provenance faults the recording rather than guessing a life. Newer RR versions are structurally checked and warned about; this is not a promise of native compatibility with untested releases.

## Executable coverage

- Two thousand absent-plugin polls cause zero patch/unpatch calls. A partial binding failure and throwing cleanup retain the held-patch flag, retry only cleanup, and cache the rejected plugin identity through one thousand further polls and a new raid.
- The exact required prefix/postfix/finalizer methods are verified at binding and before replacement, never through a process-wide scan on admission polling. Removing a required finalizer/postfix refuses replacement and faults the incomplete recording.
- Active standalone and Arena ownership, inactive Arena normal PMC, disabled Redeploy, missing server, Fika/no-driver availability, original startup callback deferral, completed early entry swap, next-frame startup, same-frame END, replacement before queue flush, and Core start-reset ordering.
- Same-profile replacement, two PMC deaths followed by own-Scav extraction, real owner/session preservation, immutable queued snapshots, old-body kill/shot provenance including another creation window, pending loot, metadata, inventory, position batches, explicit final status and one START/END.
- Aborted preparation, old-body recovery, committed new-body recovery, the post-attempt/pre-recovery interval, silent abandoned candidates, nested/throwing callback scopes, late prior-world callbacks, voluntary recording stop, final live leave versus genuine death, and release of physical-body/world references.
- Unowned ballistic callbacks do not access source bridges or Unity colliders before RR's own recording/tracking guards. A throwing collider remains an upstream event-local failure in ordinary/inactive/unavailable modes; in an owned recording the same failure is contained and explicitly marks that recording incomplete. Weak retired-world checks fence late callbacks without traversing their Unity objects. A dedicated mutant removes the passive fast path and must fail this test.
- Overflow, timeout, enqueue rejection, ambiguous source, and injected partial patches have negative controls. Retired physical players are garbage collectible while their scalar life labels remain available.
- The emitted trace is consumed by upstream final-status/human-group helpers. Independent SQLite uniqueness and CSV round-trip fixtures test identifier persistence. The separate historical presence control proves that reusing a profile ID reproduces the original permanent-death/earliest-kill problem and that per-life aliases avoid it.

## Limits / native acceptance still required

These are offline source, contract, actual-Harmony, game-reference compile, storage-fixture and frontend-helper checks. They do **not** execute the real RR WebSocket worker/server/database or Unity lifecycle in an EFT process, and do not establish native compatibility. The SQLite fixture mirrors relevant identity constraints but is not RR's production database implementation. Fixture modes such as Fika and a disabled driver model the existing Core availability/claim contract; they do not load Fika or Arena.

Before advertising native acceptance, use the exact built Core DLL on a GPU host with RR 1.10.0: one first-life kill/loot, two PMC respawns with kill/loot after each, an old corpse hit and delayed old grenade, own-Scav continuation and extraction, then inspect RR's browser replay, per-life rows and final statuses. Repeat a normal no-replacement raid with inactive Arena, recording disabled/stopped, and an absent-RR map-menu performance comparison. Record process-loaded build identity and restore any temporary mods. No such native run is claimed by this harness.

# Redeploy repository status

## Consumer documentation and interop revision 1 — 2026-11-09

The first external mod is expected to consume `RespawnApi` soon. The API is unchanged
(`ApiVersion` 1). New: the English consumer set ([API](API.md),
[soft dependency](SOFT-DEPENDENCY.md), [rules](RULES.md), [recipes](RECIPES.md)), interop
file revision 1 (header, `LocalBodyTracker.Revision`, revision in the bind log line,
corrected ProfileId comments) and a compiled [example plugin](../interop/examples/RespawnConsumerExample.cs).
Killcam's copy is still revision 0; it works unchanged and can be recopied with Killcam's
next release. A native acceptance run with the external mod waits for a build of it that
uses the tracker.

## Core 1.0.22 in development — 2026-11-08

Core adds a body handoff for [Amand's Sense Updated 3.1.0](AMANDS-SENSE-20261108.md).
Drivers built from this source require Core 1.0.22. Published Redeploy 1.1.2
ships Core 1.0.20.

## Core 1.0.21 in development — 2026-11-08

The shared library now handles body replacement for P.L.A.T.E., Loot In Vicinity,
Skills Extended and Climbable Ladders. Drivers built from this checkout require
Core 1.0.21. Redeploy 1.1.2 ships Core 1.0.20. See the [handoff and verification
record](BODY-HANDOFF-20261108.md).

## Redeploy 1.1.2 published — 2026-11-07

- Tag `v1.1.2` = `3b68193`; [GitHub release](https://github.com/maschine34675/Redeploy/releases/tag/v1.1.2)
  with `Redeploy-1.1.2.zip` and its checksum, latest, not a prerelease; `main` pushed with the
  record commit `1763854` and this note.

- Client/Server bumped to **1.1.2** in `Shared.props`, `Redeploy.Server.csproj`,
  `ModMetadata.cs` and `Plugin.cs`; Core stays **1.0.20** (`CoreVersion.props`,
  unchanged since 2026-11-02). No Client or Server code change since 1.1.1.
- Documentation: `docs/RELEASE-1.1.2.md` (new), changelog section `[1.1.2]`, README
  version lines and the three Core 1.0.20 integrations in the optional list,
  `docs/README.md` row. The release notes' native-check and package sections are
  filled by the record commit after packaging.
- Branch state: `main` only; the merged `fix/absent-adapter-unpatch` was deleted on
  2026-11-07.
- Offline checks on this state (no deploy, `-p:SPTModType=Library`): Core, Client and
  Server build (eight pre-existing client warnings); `Redeploy.Tests` 601 passed,
  `Redeploy.Server.Tests` 162 passed, `RESPAWN_INTEROP_CHECKS_PASSED`.
- Packaged from `3b68193`: `artifacts/publish-112/package-20261107-203151/Redeploy-1.1.2.zip`
  (SHA-256 `e3a4a511…447d4`, 234,823 bytes; Core `1.0.20+3b68193`, SHA-256 `150851f5…`), the
  three DLLs clean on VirusTotal; ArenaMode 0.5.0 bundles the byte-identical Core. The record
  of the native GPU cycle is in the release notes.

## Recorder and popular-mod integration — 2026-11-02 (unreleased)

- Current checkout is `main`; the earlier performance commits `51ac95c` and
  `3c41bd6` are already in its history. The branch/build details in the dated
  2026-10-31 entry below describe that earlier state.
- Shared Core is now **1.0.20** in source. Added Raid Review 1.10.0,
  HollywoodFX 3.0.0 and Hands Are Not Busy 1.7.1 body replacement adapters.
  See [design, popular-mod audit and validation](RECORDING-AND-POPULAR-COMPAT-20261102.md).
- Client/Server stay 1.1.1 and API stays v1. Source builds of both drivers must
  accompany Core 1.0.20 because their minimum-version stamp follows it.
- No release, push or Arena whitelist change is part of this work. Deployment
  and native gameplay evidence are recorded separately in the validation report.
- Offline validation passed: 601 Core/client tests, 47 interop checks, shared
  adapter composition, both new real-Harmony harnesses, replay/storage checks
  and mutation controls. Core and both clients build; installed DLL hashes
  remained unchanged. **Not deployed or tested in game.**
- StatTrack remains a concrete next candidate; asynchronous save/clear behavior
  and PMC/Scav accounting require a separate design before changing it.

## Absent-adapter unpatch guard, Core 1.0.19 — 2026-10-31 (unreleased)

- Branch `fix/absent-adapter-unpatch` (from `main` at `70bceb1`, not merged, not pushed).
  Five Core adapters (Quick Cancel, PiP Disabler, Quest Tracker, Audio Accessibility
  Indicators, Healing Autocancel) called `Harmony.UnpatchSelf()` on every admission check
  while their mod was absent; HarmonyX walks every patched method for it. ArenaMode's map
  screen polls admission five times a second, which cost one long frame per poll on GPU-2
  (2026-10-31). The adapters now unpatch only while their Harmony instance may hold patches;
  `Redeploy.Tests/AdapterUnpatchGuardTests` pins that for all sixteen adapters. See the
  [changelog](CHANGELOG.md), "Unreleased".
- `CoreVersion.props` is now **1.0.19**. ArenaMode's branch `fix/map-screen-admission`
  stamps and requires it; release the two together. The commit `51ac95c` itself ran offline
  tests only. On 31.10. evening the orchestrator built Core `1.0.19+51ac95c` and deployed it
  with ArenaMode `0.4.1+60795208` to all Syncthing hosts for the native measurement; on the
  development machine `Redeploy.Core/bin/Debug` holds that 1.0.19 build, `bin/Release` still
  a 1.0.18 build (`70bceb1`), and `CoreVersionTests` read the newer of the two. No native
  measurement yet.
- Review follow-up (31.10. evening): `AdapterUnpatchGuardTests` now requires the flag to be
  raised on the path to the `Patch` call (an unconditional statement of an enclosing block,
  after the method's last return before it); a flag raised in the absent branch used to pass.
  `Redeploy.Tests` 593 total, 592 passed, 1 skipped (Redeploy.Client not built).

## Repository hygiene — 2026-10-30

- `AGENTS.md` was rewritten as short working notes for agent sessions (layout, no-deploy
  build and test commands, Core versioning rules, verified traps, conventions). The previous
  content, the CorpseRun handoff with the Stufe-0/1 engine notes, is archived verbatim in
  [docs/history/CORPSERUN-HANDOFF.md](history/CORPSERUN-HANDOFF.md); nothing in it was
  edited. Documentation only: no source, package or installation change.
- Local, not in the repository: gitignored `artifacts/` folders older than 2026-10-28 and all
  `bin/`/`obj/` output were deleted on the development machine (`publish-111`,
  `publish-20261029` and `standalone-compat-20261029` were kept). Published packages live on
  GitHub; the next build recreates the outputs.

## Published 2026-10-29 — Redeploy 1.1.1

[GitHub release](https://github.com/maschine34675/Redeploy/releases/tag/v1.1.1) is public,
latest and not a prerelease. Tag/build source: `232af19ac2fac1c82acd5c6f43e0dcf8c2c80398`.
`Redeploy-1.1.1.zip` contains exactly Client/Server 1.1.1 and Core 1.0.18; 226,569 bytes,
SHA-256 `38a8e33c19ee38f2fd1be906847c1d21937e10aeedcd0e243735f6efe5ca2ca3`.
The public ZIP and checksum were downloaded and verified against the local bytes and the
GitHub digests. 522 client/Core and 162 server tests, the interop checks and the Core startup
guard passed. All three DLLs had no malicious or suspicious VirusTotal labels. Core matches
the copy in ArenaMode 0.4.1 exactly. One native GPU check with these binaries and Recoil
Rework Legacy 2.0.0 passed Continue as Scav through extraction; see the
[release notes](RELEASE-1.1.1.md). The v1.1.0 release page now names 1.1.1 as its successor
and lists the fixed issues. Later documentation commits do not move the verified tag or
change the published package.

## Published 2026-10-29 — Redeploy 1.1.0

[GitHub release](https://github.com/maschine34675/Redeploy/releases/tag/v1.1.0) is public,
latest and not a prerelease. Tag/build source: `f7eae6f59a5cc9c4bc69a0268bb0ce8d1a5f44fc`.
`Redeploy-1.1.0.zip` contains exactly Client/Server 1.1.0 and Core 1.0.17; 223,169 bytes,
SHA-256 `aa1c2f0f705b83d268702ce0b3bdd61bea9089b35789553336b5486676d1e093`.
The public ZIP/checksum were downloaded and verified against local bytes and GitHub digests.
469 client/Core and 149 server cases plus 47 interop checks passed; initially absent sibling
Arena fixtures were rerun successfully against the final 0.4.0 source/binary. All three DLLs
had no malicious/suspicious VirusTotal labels. Core matches ArenaMode's supplied copy exactly.
See [release notes](RELEASE-1.1.0.md) for scanner links, install steps and native evidence limits.
No live deployment/settings/synchronization changes were performed for publication. Later
documentation commits do not move the verified tag or change the published package.

## Release preparation — Redeploy 1.1.0, 2026-10-29

- The source then targeted Client/Server **1.1.0** and shared Core **1.0.17**.
  Existing assembly names, plugin identities and install paths are unchanged.
- [Release notes](RELEASE-1.1.0.md) and the [versioned changelog](CHANGELOG.md)
  cover the changes since the published 1.0.2 package.
- The native checks below used pre-version-bump binaries. Fresh 1.1.0 builds,
  final archive validation and publication are separate release work; this
  preparation entry does not certify new binary hashes or a published asset.

## Native release check and logging — 2026-10-29

- This check used Client/Server 1.0.2 with Core 1.0.17.
- Nine routine messages moved to Debug; safety, protection and real failures
  remain visible. All 469 client/Core tests passed.
- Fresh GPU-2 checks cover naked PMC respawn, original corpse-weapon recovery and
  firing, second-death give-up, own-Scav extraction and native partial transfer.
  GPU-1 separately covers Core through Arena modes and a final TDM respawn/end.
  See [candidate identities and exact limits](RELEASE-CHECK-20261029.md).

## Own-Scav validation and consolidation — 2026-10-28

- Checkout at that date: `D:\SPT41\Development\Redeploy`, branch `main`.
- The complete `feature/pmc-to-scav` history through `703401f` was fast-forwarded
  into `main`; the merged branch and its separate checkout were removed.
  All 55 ignored non-build files were preserved and hash-verified in the private
  evidence archive, together with a verified Git bundle. No history was rewritten.
- At that date Client/Server remained 1.0.2; development required shared Core 1.0.16.
- Default-on additional own-Scav choice alongside naked PMC respawn, using the
  regular account Scav, its normal cooldown and the native loot transfer. This replaces the earlier independent
  generated-Scav/mail prototype. See [contract and validation](PMC-TO-SCAV.md).
- The readiness checks and requested local consolidation are complete.
  No new public release is part of these corrections. The published 1.0.2 package
  is unchanged; publication work is listed in the release review below.
- Candidate `6a1332d` implements the interruption recovery, post-switch BTR
  delivery restriction and pre-selection availability fixes identified at
  `7037a25`. Recovery uses current inventories, explicit unfinished-life abandon
  and provable saved outcomes; owned-transfer Sell All is deliberately unavailable.
  The native-assembly Client build, **469 client/Core tests** and **149 server
  tests** passed. Independent source review resolved all actionable findings.
  **Scoped native acceptance passed on SPT-GPU-1**, including client-only/server
  restart, partial-transfer recovery, subsequent raids, continued-Scav death,
  preflight/BTR controls and ordinary Scav Sell All. All 10,049 baseline files
  were restored and hash-verified; the probe was removed and synchronization
  restored. Exact tested Client/Server DLLs/PDBs are deployed in the development
  installation and GPU1; Core remains unchanged. See the
  [native report and remaining limits](PMC-SCAV-RECOVERY-20261028.md) and
  [resolved release blockers](PMC-TO-SCAV-RELEASE-REVIEW-20261028.md).
- Earlier choice/menu update added an explicit naked-PMC / own-Scav death-menu choice
  and readable F12 settings with advanced diagnostics. It passed 394 unit tests,
  76 config checks and focused native choice/cooldown tests on SPT-GPU-1. The
  exact tested Client/PDB were installed in the development installation and both
  GPU hosts at that stage; GPU-2 was not restarted or tested for that change. The test profile,
  configuration and original synchronization state were restored. See
  [choice and menu validation](PMC-TO-SCAV.md#explicit-respawn-choice-and-configuration-menu--2026-10-28).
- Earlier follow-up deployment retained `RespawnAsScav=true`.
  A server-only correction allowed fresh PMC starts with the native Common
  transit flag. It passed 103 server tests and a focused native PMC-to-Scav/death
  run on SPT-GPU-1. The corrected server was installed in the development installation
  and both GPU hosts, resolving GPU-2's earlier locked-file deployment.
  See the [correction and validation record](PMC-TO-SCAV.md#fresh-raid-transit-registration-correction--2026-10-28).

## Historical development snapshot — 2026-10-25

- Checkout: `D:\SPT41\Development\Redeploy`, branch `main`.
- Public repository: <https://github.com/maschine34675/Redeploy>.
- Local `origin` fetch/push URL: `https://github.com/maschine34675/Redeploy.git`.
- Published release: Client/Server 1.0.2 and shared Core 1.0.13; see
  [final package verification](RELEASE-1.0.2.md#final-package-verification).
- Current development uses Core 1.0.14 for ImpactCue and Enemy Markers body and
  camera handover. Client/Server remain 1.0.2; no replacement release is implied.
  See [implementation and native verification](ITEMS-HUD-COMPAT-20261025.md).
- Compatibility worktrees have been consolidated; the retained archives and
  their verification are recorded in [worktree cleanup](WORKTREE-CLEANUP-20261025.md).

The record below describes the earlier private-repository consolidation. Its
private URLs and versions are historical, not the current installation target.

## Repository consolidation — 2026-10-12

- Checkout: `D:\SPT41\Development\Redeploy`.
- Branch: `main`.
- Private GitHub repository: <https://github.com/maschine34675/Redeploy-dev>.
- Local `origin` fetch/push URL: `https://github.com/maschine34675/Redeploy-dev.git`.
- The project rename and shared respawn-engine extraction happened in `cda21a9`.
  This cleanup changes repository organization and documentation only, not plugin
  identities, binaries or runtime behavior.

GitHub repository `CorpseRun-dev` was renamed in place, retaining repository ID
`1321886013`, private visibility, default branch `main` and its existing history.
No new repository or public release was created. Other existing clones should
update their remote URL to the address above on their next use.

## Archived CorpseRun checkout

The previous `D:\SPT41\Development\CorpseRun` directory was a separate, clean
clone at `d5c0aea86c3e8ea378a3177ac52243c0e34312b7`. All 21 reachable commits,
including the `spt-4.0-final` tag, were already present in Redeploy. At audit time,
Redeploy `main` was `26835653992ceb4db3d85e94dd811e4718778baa`, 49 commits ahead.
The old clone had no stashes, untracked files, linked worktrees, alternate object
stores or unreachable Git objects. Current neighboring build references did not
depend on that directory.

The entire checkout, including `.git` and ignored build outputs, was moved to:

`D:\SPT41\Development\_archives\CorpseRun-20261012\CorpseRun`

The adjacent `archive-manifest.json` records the original relative paths, sizes
and SHA-256 hashes. All 321 files were verified after the move. A separately
verified `CorpseRun.bundle` preserves every original Git ref; the full checkout
also retains ignored binaries and Git-local state that a bundle does not contain.
The archived checkout's configuration is intentionally unchanged as a snapshot.
Keep this archive local; old ignored artifacts are not a release package.

For recovery, copy the archived checkout into a new working directory and update
its remote if development is to continue. Normal builds and ArenaMode's Core
reference use the active Redeploy checkout, not the archive.

## Scope and verification

History ancestry, reachable commits, worktree ownership, repository metadata and
archive file hashes were checked. The renamed remote was fetched successfully.
No build, deployment, profile edit, game-configuration edit or game test was needed
for this administrative change. Unrelated new `assets/` content in Redeploy was left
untouched and excluded from the documentation commit.

[docs/history/CORPSERUN-HANDOFF.md](history/CORPSERUN-HANDOFF.md) (formerly `AGENTS.md`,
before that `CLAUDE-CODE-HANDOFF.md`) and `CODE-REVIEW-2026-07-29.md` retain historical CorpseRun
details; their old paths and identifiers are not current build instructions.

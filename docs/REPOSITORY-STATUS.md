# Redeploy repository status

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

`CLAUDE-CODE-HANDOFF.md` and `CODE-REVIEW-2026-07-29.md` retain historical CorpseRun
details; their old paths and identifiers are not current build instructions.

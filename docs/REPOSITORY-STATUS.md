# Redeploy repository status

## Experimental worktree — 2026-10-28

- `feature/pmc-to-scav`, based on `ecf40d9`, is developed separately in
  `D:\SPT41\Development\Redeploy-pmc-scav`.
- Client/Server remain 1.0.2; this branch requires shared Core 1.0.16.
- Default-off PMC-to-Scav continuation using the regular account Scav, its normal
  cooldown and the native loot transfer. This replaces the earlier independent
  generated-Scav/mail prototype. See [contract and validation](PMC-TO-SCAV.md).
- No main-branch merge, push or release is part of this feature work.
- Follow-up deployment: the tested candidate is now persistently installed in
  the development SPT installation and on SPT-GPU-1, with `RespawnAsScav=true`.
  SPT-GPU-2 still needs its locked server DLL synchronized after the user stops
  its running test. See the [deployment record](PMC-TO-SCAV.md#persistent-feature-deployment--2026-10-28).

## Active development — 2026-10-25

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

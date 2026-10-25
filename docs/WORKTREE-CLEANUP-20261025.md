# Worktree cleanup — 2026-10-25

All compatibility branches were already integrated into `main` at `9d1672b`.
There were no pending source changes or stashes. That revision was pushed to
`origin/main` without force before removing these local branches:

- `compat/four-mods-20261025`, tip `2e8cb76`.
- `compat/magcheck-20261025`, tip `e3c2b36`.
- `compat/hud-20261025`, tip `9d1672b`.

The `Redeploy-compat-four-20261025` and `Redeploy-hud-compat-20261025`
worktrees were removed after checking their absolute paths, clean source,
remote ancestry and every archived file hash. Only `main` and the primary
`Redeploy` checkout remain. Existing release tags were retained; no release,
deployment or gameplay change was made.

## Local archives

| Path under `artifacts/archived-worktrees/` | Files | Bytes |
|---|---:|---:|
| `compat-four-and-magcheck-20261025` | 985 | 118,138,737 |
| `compat-hud-20261025` | 606 | 89,290,784 |

The archives retain all ignored data, including exact tested DLLs, build output,
unit/harness results, compatibility reviews and task ledgers. Their
`archive-manifest.json` files record original locations, commits, file lengths
and SHA-256 hashes. They remain ignored/local, outside source publication and
release payloads. The 24 independent source clones under the primary checkout's
existing audit artifacts were left untouched.

This cleanup was verified through Git ancestry, file hashes and final local/
remote inventories; no runtime tests were repeated for documentation changes.

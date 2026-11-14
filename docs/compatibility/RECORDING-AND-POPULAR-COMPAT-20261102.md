# Recording and popular-mod body compatibility — 2026-11-02

Work for **Redeploy.Core 1.0.20**, which shipped in Redeploy 1.1.2 on 2026-11-07.
At the time of writing Client/Server were 1.1.1; the public Core API remains version 1.
The changes belong to shared Core, so they apply to its standalone Redeploy and
ArenaMode drivers. They do not add or whitelist these mods in Arena's isolated
session policy.

## Review scope

The Forge catalogue was refreshed in download order: four pages of 50 entries,
200 unique mods, including older SPT versions. This was a catalogue screen with
focused source and released-binary reviews, **not 200 gameplay tests**.
Already integrated body replacement adapters were considered before selecting
additional work.

| Mod | Finding and scope |
| --- | --- |
| [Raid Review 1.10.0](https://github.com/Chazut/SPT-RaidReview/releases/tag/1.10.0) | A respawn reuses a profile ID, but the recorder retains that profile's confirmed death. Clearing that state alone cannot fix the replay: its map and status helpers also treat the earliest death as terminal. Separate recording identities are required. |
| [HollywoodFX 3.0.0](https://github.com/SleepingPills/HollywoodFX/releases/tag/v3.0.0) | `ImpactStatic.LocalPlayer` is assigned at raid start and can retain the old body. Update this exact cache after a committed replacement. This does not certify the separate HollywoodGraphics component shipped in the same archive. |
| [Hands Are Not Busy 1.7.1](https://github.com/Lacyway/HandsAreNotBusy/releases/tag/1.7.1) | The old body's hotkey component can remain active and rebuild its hands. Fence Core-owned prepared, candidate and retired bodies, and disable the old component only after the replacement commits. Keep the new body's native component. |
| StatTrack 2.1.1 | A further concrete candidate: local-player unregistration triggers an asynchronous end-of-raid merge and clear. Correct support needs separate handling of save completion and PMC/Scav accounting; it is not included in this change. |
| Amand's Sense 3.1.0, Player Encumbrance Bar 1.3.0, Search Open Containers 1.5.0 | Focused source inspection did not establish another body replacement defect: they refresh the relevant owner or use the current interaction owner. This is not native certification. |
| Game Panel HUD 3.4.0 | Older SPT 4.0 target; no port is included. |

The released client DLLs inspected were:

| DLL | SHA-256 |
| --- | --- |
| `RAID_REVIEW.dll` 1.10.0 | `1d10afa713cb520fe2d43d271526c817faf51d58071f76d9ce85ff3ba4777872` |
| `HollywoodFX.dll` 3.0.0 | `2a703911e13a7afd03dde047b3441bb3a67735093eee28ad575a073d1851507a` |
| `HandsAreNotBusy.dll` 1.7.1 | `a03baf23199accced769f162cd256f2e8eb07d9063595927759a87eeb78f4189` |

## Raid Review recording contract

Each Core-controlled physical life receives a separate **recording-only** ID,
including the first life. Real EFT profile IDs, inventory ownership, session ID
and Raid Review's recording owner remain unchanged. The existing replay can
therefore show each life independently without replacing Raid Review's server or
web interface. Statistics are shown **per life**, rather than as one combined
player row. A raid entered as a PMC keeps its PMC raid type after continuing as
the own Scav; the final Scav life receives its own outcome.

The adapter must map all participant-bearing telemetry consistently, including
positions, status, inventory, loot and combat events. Historical deaths and
old-body events remain associated with their original physical life. A new life
is admitted only after Core confirms the replacement, or confirms recovery onto
that exact body after a failed attempt. Merely registering a player is not a
successful replacement.

Startup packets are briefly held until raid-start callbacks establish whether a
Core driver actually owns this raid. Ordinary raids with no active driver and
Fika retain the original recording behavior. Recording errors must be explicit;
an incomplete or ambiguous alias mapping must not silently produce a plausible
but incorrect replay.

## Performance constraints

The implementation preserves the fixes in `51ac95c` and `3c41bd6`. Missing mods
must not call `Harmony.UnpatchSelf()` from repeatedly polled admission checks.
That method scans all patched methods in the process. An adapter records patch
ownership immediately before its first installation attempt, retains ownership
after a partial installation or failed unpatch, and clears it only after a
successful unpatch.

HollywoodFX needs no Harmony hook. Hands Are Not Busy and Raid Review join the
source guard's adapter inventory. Optional contracts and packet accessors are
resolved once per installed plugin binding; no scene-wide object search or
global Harmony scan is added to per-shot or per-frame work.

## Validation

The following checks passed without deploying into the installation:

- Core and both consumer clients compiled. Redeploy.Client reported eight existing
  obsolete-API warnings; ArenaMode.Client reported four existing nullable-context
  warnings. Neither consumer had a build error.
- **601 Core/client unit tests**, including **79 adapter-unpatch source guards**;
  no skipped tests. **47 respawn interop checks** also passed.
- Shared compatibility composition: **20 cases / 742 assertions** and **10 mutation
  controls** passed, including the new adapters' admission, prepare, cancellation,
  pump and world-disposal wiring.
- HollywoodFX/HANB: production-linked execution with the installed Harmony,
  actual release metadata and real-EFT compilation; reviewed and structurally
  compatible future-version fixtures passed. Two 10,000-poll absent-mod phases
  performed zero global unpatch scans. **12 mutation controls** failed at their
  intended assertions, including partial installation, failed cleanup, abandoned
  candidates, recovery timing and destroyed Unity wrappers.
- Raid Review: **3,202 production-linked assertions** with real Harmony and the
  released DLL contract; **48 checks** using pinned upstream replay helpers,
  packets emitted by the adapter, and SQLite/CSV boundary fixtures; **11 mutation
  controls** rejected at their intended assertions. Tests cover initial entry
  swaps, two PMC deaths followed by own-Scav extraction, old projectile/loot
  attribution, recovery, stopped recording and final status. Inactive recordings
  bypass Core's ballistic lookup and retain upstream event-local error handling.
- Raid Review's repeated absent and failed-bind admission tests perform no
  repeated patch installation or global unpatch scan. Held cleanup alone retries
  after an injected unpatch failure. Retired player objects remain collectible;
  accepted recording END releases world/body references.

Independent adversarial review covered the new adapters against EFT decompiles
and the released mod contracts. Installed Core, Redeploy.Client and ArenaMode.Client
hashes were unchanged after the no-deploy consumer builds. No test mods were
installed, and no native gameplay or frame-time measurement was performed.
The storage fixture is independent of Raid Review's real WebSocket/server process;
the frontend checks call its actual helpers but do not render the full browser UI.

Before releasing this Core, use an isolated test profile and verify the loaded
candidate version/hash, then cover:

1. Standalone Redeploy: two PMC respawns, post-respawn combat/loot, continuation
   as the own Scav and extraction. Open the saved Raid Review replay and check
   separate life rows, earlier death markers and the Scav's final extraction.
2. Arena: initial kit/body replacement and repeated respawns; its recording must
   start once and continue through the replacements. Test admission policy
   separately; these source changes do not authorize an Arena whitelist entry.
3. HollywoodFX impact/optic behavior and the HANB reset hotkey after replacement,
   including a second respawn. The old body must not react to that hotkey.
4. An ordinary raid with Core's drivers inactive, plus recording disabled and
   manually stopped: native recording and normal raid exit must still work.
5. A mod-absent map-selection performance comparison. The offline scan counters
   establish the narrow regression guard, not a new frame-time measurement.

Restore temporary test mods and settings after the native cycle. Do not certify
the separate HollywoodGraphics DLL through the HollywoodFX player-cache check.

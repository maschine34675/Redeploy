# Redeploy documentation

Start with the [project README](../README.md) for installation, settings, updates, removal and build/deployment behaviour.

This folder holds only the eight current documents. Release notes, compatibility records, the own-Scav documents, dated reports and historical material are in the subfolders listed further down.

## For mod authors

How another mod follows the local player through a respawn. English; everything a consumer needs.

| Document | Scope |
| --- | --- |
| [Soft dependency](SOFT-DEPENDENCY.md) | Copying the interop file, setup, availability tiers, how players get Core, keeping the copy current, log lines, licence. |
| [Rules](RULES.md) | The consumer checklist: what breaks when the body is replaced, and what to do instead. |
| [Recipes](RECIPES.md) | Worked cases with code, including mods with a downed or revive state. |
| [API reference](API.md) | `RespawnApi` v1 and `LocalBodyTracker`: members, timelines, guarantees, versions, what is not part of the contract. |
| [Interop file](../interop/RedeployRespawnInterop.cs) and [example plugin](../interop/examples/RespawnConsumerExample.cs) | The file consumers copy (revision 1) and a minimal plugin compiled against the game by the tests. |

## Project

| Document | Scope |
| --- | --- |
| [Changelog](CHANGELOG.md) | Changes since 1.0.0, the separate Core version line and the public API and interop revisions. |
| [Repository status](REPOSITORY-STATUS.md) | The dated working state: what is released, what is on `main` only, open work. |
| [Respawn API design record](RESPAWN-API.md) | German design record: decisions, implementation history and verification notes behind the API. Not needed to use the API; the English set above is the contract. Dated status entries supersede earlier proposals. |
| [Agent working notes](../AGENTS.md) | Layout, no-deploy build and test commands, Core versioning rules, traps and conventions for agent sessions. |

## Release notes (`releases/`)

| Document | Scope |
| --- | --- |
| [Redeploy 1.1.2](releases/RELEASE-1.1.2.md) | Core 1.0.20: Raid Review, HollywoodFX and Hands Are Not Busy integrations, the absent-adapter performance fix of Core 1.0.19, update instructions. |
| [Redeploy 1.1.1](releases/RELEASE-1.1.1.md) | Hotfix: Recoil Rework / FOV Fix with Continue as Scav, transit arrivals, refused Scav preparation, Core 1.0.18. |
| [Redeploy 1.1.0](releases/RELEASE-1.1.0.md) | Own-Scav continuation and recovery, optional integrations, Core 1.0.17 and update instructions. |
| [Native release check, 2026-10-29](releases/RELEASE-CHECK-20261029.md) | Standalone respawn, corpse loot, own-Scav transfer, exact candidates and quieter routine logging. |
| [Redeploy 1.0.2](releases/RELEASE-1.0.2.md) | Client/Server 1.0.2, bundled Core 1.0.13, optional integrations and update instructions. |

## Own-Scav continuation (`own-scav/`)

| Document | Scope |
| --- | --- |
| [Own-Scav continuation](own-scav/PMC-TO-SCAV.md) | Regular-Scav choice, native loot transfer, recovery limits and dated validation. |
| [Own-Scav recovery validation](own-scav/PMC-SCAV-RECOVERY-20261028.md) | Prepared/Active interruption, partial-transfer restart, BTR/preflight controls and isolated-host restoration. |
| [Own-Scav release review](own-scav/PMC-TO-SCAV-RELEASE-REVIEW-20261028.md) | Historical (2026-10-28, superseded by the 1.1.0 release): blockers, implemented fixes and acceptance. |

## Compatibility records (`compatibility/`)

How Core carries other mods' per-body state across a replacement, one dated record per Core version. Mod authors do not need these to integrate; they document Core's own adapters.

| Document | Scope |
| --- | --- |
| [Amand's Sense Updated](compatibility/AMANDS-SENSE-20261108.md) | Development Core 1.0.22. Same-raid markers, recorded corpses and inventory ownership across body replacement. |
| [Body handoff for blood, loot, skills and ladders](compatibility/BODY-HANDOFF-20261108.md) | Development Core 1.0.21. Supported versions, Skills Extended issue 136, item ownership and test coverage. |
| [Recording and popular-mod compatibility](compatibility/RECORDING-AND-POPULAR-COMPAT-20261102.md) | Core 1.0.20 (released with Redeploy 1.1.2): Raid Review, HollywoodFX and Hands Are Not Busy; popular-mod audit and performance constraints. |
| [Standalone compatibility](compatibility/STANDALONE-COMPAT-20261029.md) | Core 1.0.17: AI Limit, Healing Autocancel, Quest Tracker and Audio Accessibility Indicators. |
| [Quick Cancel, PiP and Amand's Graphics](compatibility/REMAINING-BODY-COMPAT-20261026.md) | Core 1.0.15: body/camera integrations, standalone evidence and the Labyrinth limit. |
| [ImpactCue and Enemy Markers](compatibility/ITEMS-HUD-COMPAT-20261025.md) | Core 1.0.14: HUD, body and camera handover; native tests for both drivers. |
| [Radar and crosshair compatibility](compatibility/HUD-COMPAT-20261025.md) | Core 1.0.13: Accurate Circular Radar and Simple Crosshair; exact body/camera handover and native limits. |
| [Magazine panel compatibility](compatibility/MAGCHECK-COMPAT-20261025.md) | Core 1.0.12: Magazine Check Interrupt; panel retirement and deferred callbacks. |
| [FOV and loading animation compatibility](compatibility/FOV-MANIMAL-COMPAT-20261025.md) | Core 1.0.11: Fontaine FOV Fix and Manimal Ammo Loading Animations; native candidate and test boundaries. |
| [Shared body compatibility](compatibility/BODY-REPLACEMENT-COMPAT-20261023.md) | Core 1.0.9: Recoil Rework Legacy, Continuous Load Ammo and Continuous Healing, exact ownership and migration test boundaries. |
| [Published owned-mod audit](compatibility/OWN-MOD-COMPATIBILITY-20261024.md) | All 27 published mod repositories and four beta packages: body-replacement handling, consumer-side hardening candidates, release coordination and native test gaps. Its identity manifest is [audits/OWNED-MODS-20261024.json](audits/OWNED-MODS-20261024.json). |

## Reports (`reports/`)

| Document | Scope |
| --- | --- |
| [Probe results, 2026-09-21](reports/RESPAWN-API-PROBE-RESULTS-2026-09-21.md) | Tested scenarios and remaining coverage limits for the recorded builds. The [native probe instructions](../tools/RespawnApiProbe/README.md) are developer-only; the probe is not part of a release. |
| [Logging follow-up, 2026-10-18](reports/LOGGING-FOLLOWUP-20261018.md) | Core 1.0.8 timing opt-in, logging changes and offline verification. Not a complete native acceptance report. |
| [Worktree cleanup, 2026-10-25](reports/WORKTREE-CLEANUP-20261025.md) | Integrated compatibility branches removed after `main` was pushed. |

## History (`history/`)

| Document | Scope |
| --- | --- |
| [Code review, 2026-07-29](history/CODE-REVIEW-2026-07-29.md) | Historical CorpseRun review; not a list of current unresolved defects. |
| [Historical CorpseRun handoff](history/CORPSERUN-HANDOFF.md) | The earlier implementation notes and pitfalls, verbatim (until 2026-10-29). Its old CorpseRun names, paths and open items are not current instructions. |

Old reports retain names such as CorpseRun, KillcamReplay and the former handoff filename for provenance. Current standalone identities are Redeploy and Killcam. Offline test results, deployment and native gameplay acceptance are separate evidence; a report only covers its stated builds and scenarios.

## Where new documents go

The folder root stays at these eight documents. Release notes go to `releases/`, per-mod compatibility records to `compatibility/`, own-Scav material to `own-scav/`, other dated reports to `reports/`, and superseded material to `history/`. Dated records keep their date in the file name, and every new document gets a row in this index.

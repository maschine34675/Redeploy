# Redeploy documentation

Start with the [project README](../README.md) for installation, settings, updates, removal and build/deployment behaviour.

| Document | Scope |
| --- | --- |
| [Changelog](CHANGELOG.md) | Changes since 1.0.0 and the separate Core version line. |
| [Native release check, 2026-10-29](RELEASE-CHECK-20261029.md) | Standalone respawn, corpse loot, own-Scav transfer, exact candidates and quieter routine logging. |
| [Own-Scav continuation](PMC-TO-SCAV.md) | Regular-Scav choice, native loot transfer, recovery limits and dated validation. |
| [Own-Scav release review](PMC-TO-SCAV-RELEASE-REVIEW-20261028.md) | Historical blockers, implemented fixes, acceptance and remaining publication work. |
| [Own-Scav recovery validation](PMC-SCAV-RECOVERY-20261028.md) | Prepared/Active interruption, partial-transfer restart, BTR/preflight controls and isolated-host restoration. |
| [Redeploy 1.0.2 release notes](RELEASE-1.0.2.md) | Client/Server 1.0.2, bundled Core 1.0.13, optional integrations and update instructions. |
| [Respawn API](RESPAWN-API.md) | Core's observer contract, design decisions, implementation history and verification notes. German; dated status entries supersede earlier proposals. |
| [Shared body compatibility](BODY-REPLACEMENT-COMPAT-20261023.md) | Core 1.0.9: Recoil Rework Legacy, Continuous Load Ammo and Continuous Healing, exact ownership and migration test boundaries. |
| [FOV and loading animation compatibility](FOV-MANIMAL-COMPAT-20261025.md) | Core 1.0.11: Fontaine FOV Fix and Manimal Ammo Loading Animations; native candidate and test boundaries. |
| [Magazine panel compatibility](MAGCHECK-COMPAT-20261025.md) | Core 1.0.12: Magazine Check Interrupt; panel retirement and deferred callbacks. |
| [Radar and crosshair compatibility](HUD-COMPAT-20261025.md) | Core 1.0.13: Accurate Circular Radar and Simple Crosshair; exact body/camera handover and native limits. |
| [Published owned-mod audit](OWN-MOD-COMPATIBILITY-20261024.md) | All 27 published mod repositories and four beta packages: body-replacement handling, consumer-side hardening candidates, release coordination and native test gaps. |
| [Native probe instructions](../tools/RespawnApiProbe/README.md) | Developer-only in-game checks. The probe is not included in normal installation. |
| [Probe results, 2026-09-21](RESPAWN-API-PROBE-RESULTS-2026-09-21.md) | Tested scenarios and remaining coverage limits for the recorded builds. |
| [Logging follow-up, 2026-10-18](LOGGING-FOLLOWUP-20261018.md) | Core 1.0.8 timing opt-in, logging changes and offline verification. Not a complete native acceptance report. |
| [Repository status](REPOSITORY-STATUS.md) | Current checkout/remote and the historical CorpseRun archive. |
| [Code review, 2026-07-29](CODE-REVIEW-2026-07-29.md) | Historical CorpseRun review; not a list of current unresolved defects. |
| [Historical handoff](../AGENTS.md) | Earlier implementation notes and pitfalls, now stored as `AGENTS.md`. Its old CorpseRun names and paths are not current installation instructions. |

Old reports retain names such as CorpseRun, KillcamReplay and the former handoff filename for provenance. Current standalone identities are Redeploy and Killcam. Offline test results, deployment and native gameplay acceptance are separate evidence; a report only covers its stated builds and scenarios.

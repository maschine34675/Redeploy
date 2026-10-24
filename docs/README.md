# Redeploy documentation

Start with the [project README](../README.md) for installation, settings, updates, removal and build/deployment behaviour.

| Document | Scope |
| --- | --- |
| [Changelog](CHANGELOG.md) | Changes since 1.0.0, with unreleased work and the separate Core version line. |
| [Respawn API](RESPAWN-API.md) | Core's observer contract, design decisions, implementation history and verification notes. German; dated status entries supersede earlier proposals. |
| [Shared body compatibility](BODY-REPLACEMENT-COMPAT-20261023.md) | Core 1.0.9: Recoil Rework Legacy, Continuous Load Ammo and Continuous Healing, exact ownership and migration test boundaries. |
| [Native probe instructions](../tools/RespawnApiProbe/README.md) | Developer-only in-game checks. The probe is not included in normal installation. |
| [Probe results, 2026-09-21](RESPAWN-API-PROBE-RESULTS-2026-09-21.md) | Tested scenarios and remaining coverage limits for the recorded builds. |
| [Logging follow-up, 2026-10-18](LOGGING-FOLLOWUP-20261018.md) | Core 1.0.8 timing opt-in, logging changes and offline verification. Not a complete native acceptance report. |
| [Repository status, 2026-10-12](REPOSITORY-STATUS.md) | CorpseRun archive and in-place repository rename. Historical checkout details. |
| [Code review, 2026-07-29](CODE-REVIEW-2026-07-29.md) | Historical CorpseRun review; not a list of current unresolved defects. |
| [Historical handoff](../AGENTS.md) | Earlier implementation notes and pitfalls, now stored as `AGENTS.md`. Its old CorpseRun names and paths are not current installation instructions. |

Old reports retain names such as CorpseRun, KillcamReplay and the former handoff filename for provenance. Current standalone identities are Redeploy and Killcam. Offline test results, deployment and native gameplay acceptance are separate evidence; a report only covers its stated builds and scenarios.

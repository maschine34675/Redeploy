# Body replacement composition checks

This offline console harness compiles the actual `BodyReplacementContext.cs`,
`BodyReplacementCompatibility.cs`, and the sibling ArenaMode checkout's
`ArenaMedicalBodyScope.cs` as linked source. Normal checks do not copy or
reimplement their ownership or lifecycle logic. Lightweight game, Unity,
Chainloader, public lifecycle-event, and adapter stand-ins expose the external
boundaries. Adapter-specific tests and native checks establish their individual
behavior; this harness checks the coordinator, including Radar/Crosshair and
ImpactCue/Enemy Markers admission, cancellation and exact-world retirement.

Run from the repository root with PowerShell 7 and .NET 10:

```powershell
pwsh -NoProfile -File tools/BodyReplacementChecks/Run-Checks.ps1 -Mutations
```

The project has no project/package references, deployment imports, game DLLs or
network package feeds. Restore occurs only if its assets file is absent; all
builds then use `--no-restore --disable-build-servers -p:UseSharedCompilation=false`.
Outputs stay below this tool's `bin`, `obj`, and `artifacts` folders. The sibling
ArenaMode checkout is required for the production scope file.

The 20 scenarios cover absent optional mods, admission of every adapter family,
exact game/world/profile/body ownership (including same-ID impostors and the own-Scav replacement profile), preparation
order, cancellation before any success and after a successful replacement,
post-construction contract faults, stale candidates, cached UI reads, current and
late old-world pumps, missed-end and normal raid cleanup, ordinary second raids,
explicit Arena medical scope, registration errors/disposal, and sticky tick faults.
Separate in-memory plugin assemblies cover old Arena plus a matching optional
mod being refused before every Core adapter, old Arena without a matching mod
remaining unchanged, rebuilt Arena admission, and retry after an unloaded Arena
instance becomes available. The fixture does not bypass the real assembly check.
The Unity stand-in models destroyed-object equality so managed tombstones and
world disposal are checked even when Unity would compare the object to null.

`-Mutations` additionally compiles ten deliberately damaged copies into unique
configurations. Each must build successfully and then fail the targeted behavior
assertion: profile-object identity, the expected replacement profile, provisional lease removal, committed lease
preservation, no-mod inactivity, legacy Arena double hooks, ordinary-bot exclusion, null-scope quarantine,
late-world disposal, and current-world cleanup. A compile error or missing success token never counts as
a killed mutant. The script hashes all three production sources before and after
the run and retains bounded test/build logs plus hashes in `artifacts/<run-id>`.

Success tokens are `BODY_REPLACEMENT_CHECKS_PASSED`,
`BODY_REPLACEMENT_MUTATIONS_PASSED killed=10`, and the final
`BODY_REPLACEMENT_COMPOSITION_VERIFIED`. Individual cases can be selected on the
built executable with `--case <name>`; unknown names fail rather than skip checks.

These checks establish source-level composition behavior against stand-ins. They
do not establish native Unity timing, actual third-party reflection contracts,
Harmony patch execution, deployment, or in-game standalone/Arena acceptance.

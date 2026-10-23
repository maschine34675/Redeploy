# Composition harness review — 2026-10-23

Ownership: only `tools/BodyReplacementChecks/**`. No production source, shared
project, root gate ledger, deployment, native host, profile, or commit changes.

## Four passes

1. **Implementation:** linked the actual Core context/coordinator and actual Arena
   medical scope. Added 19 behavioral scenarios using observable external
   stand-ins; no ownership or lifecycle implementation is copied into assertions.
2. **Expert reread:** checked every source link, preparation/cancellation order,
   exact physical identity, managed versus Unity-null semantics, error paths,
   registration lifetime, and old/current-world pump routing. An independent
   read-only reviewer also checked the initial four harness implementation files
   against the three production files and the build-import ancestor chain.
3. **Defect hunt:** the independent reviewer refuted the first null-body assertion:
   it ran after scope disposal, so it could pass without calling the production
   scope. Moved it inside the registered lifetime and required no logged warning
   or cached admission failure. Added a compiled null-guard-removal mutation;
   it now fails on the intended quarantine assertion. Tightened the Unity
   stand-in so different destroyed objects do not compare equal to one another.
   Parent integration added a legacy Arena ownership guard after a separate
   cross-component review. Added real in-memory assemblies for each older adapter
   family, unrelated adapter types, rebuilt Arena, and unavailable/destroyed
   plugin instances with a successful retry. An additional mutation removes the
   legacy refusal. The other seven compiled mutations attack ownership,
   cancellation, no-mod, Arena-scope, and world-cleanup decisions.
4. **Polish and final reread:** verified failure exit codes, precise mutation
   anchors, separate build configurations, empty package feeds, source hash
   stability, success tokens, ignored generated files, and documented evidence
   boundaries. The final pass found no further concrete issue. The corrected
   harness passed again, with every mutant rejected by its behavioral assertion.

No further production defect was found after the parent added the legacy guard.
The independent reviewer did not execute any build or script. Parent verification
of the leaf remains separate from this self-check.

## Final execution evidence

Command, reviewed before execution:

```powershell
pwsh -NoProfile -File tools/BodyReplacementChecks/Run-Checks.ps1 -Mutations
```

Exit code `0`; `BODY_REPLACEMENT_CHECKS_PASSED cases=19 assertions=349`;
`BODY_REPLACEMENT_MUTATIONS_PASSED killed=9`;
`BODY_REPLACEMENT_COMPOSITION_VERIFIED`.

Artifacts: `artifacts/20261023151837139/`. The baseline and all nine mutant builds
reported zero warnings and zero errors. Each mutant then exited `1` with the
designated `BODY_REPLACEMENT_CHECK_FAILED <case>:` diagnostic, not a compile error.

| Compiled mutant | Observed behavioral rejection |
|---|---|
| legacy-arena-double-hooks | Expected rejection containing: Update ArenaMode and its matching Redeploy.Core together |
| same-id-profile | foreign-profile-same-id candidate was accepted |
| cancel-leaks-provisional | Cancellation leaked first provisional body lease |
| cancel-erases-committed | Cancellation erased earlier successful-body leases |
| no-mod-adoption | Absent mods must not adopt adapter state |
| ordinary-bot-scope | Installed Arena scope claimed ordinary raid |
| null-scope-quarantine | Null body faulted and quarantined the registered Arena scope |
| late-world-clears-new | Late old pump cleared new world's context |
| current-world-leaks | Destroyed current world retained context |

The script compared these SHA-256 hashes before and after the final run:

| Linked production source | SHA-256 |
|---|---|
| BodyReplacementContext.cs | 00327FB8E2F54FA65DCDBDD948667509514CD2BEA2B108F69D369B57641A4A62 |
| BodyReplacementCompatibility.cs | EAED089DFD7DE776DC0A48EC093E2D437C041631FFCAE1FA2C9BD2BA4779561F |
| ArenaMedicalBodyScope.cs | 1FE0ECCC80CF215D2EC179C5CEDD5086CE3916A20746F835618456B379F025E4 |

The earlier `artifacts/20261023150648764/` and `artifacts/20261023151120693/`
runs are superseded by this corrected and expanded run.

## Evidence boundary

The harness proves the linked source's composition decisions against lightweight
external boundaries. Real adapter contracts/Harmony interactions are covered by
their respective harnesses. Native timing, Unity destruction scheduling,
installation, and in-game standalone/Arena acceptance require separate evidence.

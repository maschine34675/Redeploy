# Shared Continuous Healing body-replacement checks

Run from the repository root:

```powershell
pwsh -NoProfile -File tools/ContinuousHealingCompatChecks/Verify.ps1
```

Supply `-PublishedAssembly <path>` if the separately downloaded official 1.6.3
DLL is elsewhere. The known SHA256 and metadata are inspected with Mono.Cecil;
the foreign DLL is never executed, installed or bundled. Local .NET 10 and the
existing `D:/SPT41/BepInEx/core/Mono.Cecil.dll` are required. Inputs are explicit
so running from a worktree cannot silently select another SPT installation.

The executable compiles the three real production compatibility files plus the
real `BodyReplacementContext` against controlled boundary types and independent
foreign method-shape fixtures. There
is no production-project reference or deployment target. It runs ordinary/Arena
body ownership, standalone first/repeated/death/live/failed replacements, late
old-world callbacks, unrelated bot pass-through, explicit Arena consumer scope,
current-local result preservation, native fallback, absent-mod, metadata and
installation-fault controls. `Verify.ps1` copies production source
to a fresh ignored artifact folder and compiles fourteen intentional defects;
each must fail its named behavioral assertion, then the unmodified control must
pass again. Source snapshots must remain unchanged throughout verification.
Generated build/run evidence is ignored beneath this harness's `artifacts/`.

Success token: `CONTINUOUS_HEALING_COMPAT_VERIFIED`. The script reports the
evidence directory with individual build/run logs. This is a boundary harness,
not native gameplay acceptance: it checks method contracts and prefix behavior,
not Unity timing or actual medical animations.

# VisualAssist body replacement checks

`dotnet run --project tools/VisualAssistChecks -c Release -- <released-VisualAssist.dll>`

The harness links the production contract, lifecycle adapter, ownership scope and rendering-resource cleanup. EFT/Unity are narrow external doubles; Harmony executes the actual prefixes against a separate foreign-shape assembly with the reviewed defects deliberately present. No deployment target or Core project reference is involved.

Checks cover absence, exact release metadata (including generated assembly revision), missing hooks, foreign physical ownership, unchanged ordinary raids, read-only preparation, pre-adoption death cleanup, a ClientGameWorld unregister that returns without its base, publication, failed handoff recovery, rollback, repeated replacements, late old-body/world callbacks, optional-plugin removal, owned-resource-only teardown and disabled preview visibility. A released-DLL Cecil pass checks the shape and the native cleanup defect independently of the fixture. `Redeploy.Tests.SourceGuardTests` protects the steady-frame path against reflection/allocation/global searches; the coordinator harness verifies admission, cancellation and retirement wiring.

Three separate processes inject partial `Awake` failure (both throwing and Unity swallowing it) and a failed owner-field assignment. Each retires the exact new arc, line and sphere, leaves no singleton authority and withholds later unsafe replacements. Materials are released only after native Awake completed: a partial initializer may still hold a shared primitive material, so those ambiguous references are conservatively preserved. The fixture positively checks a shared default remains alive. Cases run separately because an owned runtime contract failure is deliberately sticky until restart.

Run the complete offline subset with `./tools/VisualAssistChecks/Verify.ps1 -ReleasedDll <path>`.

`./tools/VisualAssistChecks/VerifyMutations.ps1 -ReleasedDll <path>` additionally proves that old-body singleton clearing, lost initial-death resources and premature candidate adoption each fail an explicit behavioral assertion. Mutations are isolated beneath this harness's ignored `obj` directory; it finishes by rebuilding and running the unchanged production adapter.

Native validation remains separate: first-life high/low throw; death with the arc visible; two replacements; preview off/on; failed/recovered handoff when available; raid-end cleanup. Arena's quick-throw/Molotov composition is owned by ArenaMode, not this Core adapter. This harness is not in-game acceptance.

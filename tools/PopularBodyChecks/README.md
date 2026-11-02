# HollywoodFX and Hands Are Not Busy body checks

Run from the Redeploy checkout:

```powershell
powershell -NoProfile -File tools/PopularBodyChecks/Verify.ps1
```

The runner requires the local SPT 4.1 managed/BepInEx assemblies and the reviewed
release DLLs. It defaults to the audit copies under
`artifacts/rr-compat-20261102/popular`; use `-PublishedHollywoodDll` and
`-PublishedHandsDll` for other locations. The files must match these releases:

| Mod | Release | Reviewed SHA256 |
|---|---|---|
| HollywoodFX | [3.0.0](https://github.com/SleepingPills/HollywoodFX/releases/tag/v3.0.0) | `2A703911E13A7AFD03DDE047B3441BB3A67735093EEE28AD575A073D1851507A` |
| Hands Are Not Busy | [1.7.1](https://github.com/Lacyway/HandsAreNotBusy/releases/tag/1.7.1) | `A03BAF23199ACCCED769F162CD256F2E8EB07D9063595927759A87EEB78F4189` |

All projects are isolated from deploy targets. The runner compiles the linked
production adapters against the real EFT assemblies, then executes the linked
sources on small runtime/plugin fixtures under .NET Framework 4.7.2 with the
installation's actual Harmony. It repeats with structurally compatible newer
fixture versions, and inspects the released DLL metadata with Cecil.

Checks cover read-only preparation, successful same-profile and own-Scav swaps,
cancel/recovered-old/recovered-new paths, yielded candidates, late old-body and
failed-candidate callbacks, exact physical/world identity, disabled component
preservation, early HollywoodFX initialization, foreign cache refusal, and late
world disposal. Harmony instrumentation proves the absent-mod path performs no
UnpatchSelf scan; injected post-install and unpatch failures verify cleanup
responsibility and cached admission failures. Twelve isolated production mutations
must each fail their intended runtime assertion. `-SkipMutants` is for a quick
diagnostic run only.

Success prints `POPULAR_BODY_CHECKS_PASSED`; logs and mutant copies are written
to the reported ignored evidence directory. The stand-ins cannot establish EFT
visual correctness, Unity frame scheduling, or the live reset-hotkey experience.
They are not in-game acceptance. HollywoodGraphics (the separate DLL in the
HollywoodFX package) is outside this adapter's scope.

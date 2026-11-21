# WTT Armory body replacement checks

This standalone harness links the production `WttArmoryBodyCompatibility.cs` and
`BodyReplacementContext.cs` into an independent behavioral fixture. It does not
reference or build the deployable Core or Client projects and cannot deploy files.
The fixture is independently written; no WTT Armory source or binaries are bundled.

Run from the Redeploy checkout:

```powershell
powershell -NoProfile -File tools/WttArmoryBodyChecks/Verify.ps1 `
  -PublishedDll 'D:\SPT41\BepInEx\plugins\WTT-ArmoryClient\WTT-ArmoryClient.dll'
```

The command compiles the adapter against the installed EFT assemblies, runs real
Harmony detours over the fixture's three state-writing callbacks, and compares
the fixture's reflected contract with the actual installed Armory assembly using
Cecil. The installed DLL is only read. The metadata/IL checks cover `You`, weapon
classification, all four burst-state properties, the supported weapon list,
callback signatures, and the installed trigger-release/reset path.

The behavioral scenarios cover initial ordinary play, absent plugins, read-only
preparation and cancellation, exact physical ownership, PMC-to-Scav profiles,
retained old bodies, new hyperburst/non-hyperburst/empty-hands classifications,
duplicate and delayed events, failed rewires before/after commitment, early
asynchronous candidate callbacks, world teardown, destroyed Unity references,
foreign cache ownership, bounded failures, and rejection of older versions.

Eight mutations must compile and then fail for their specific regression:
stale cached player, missing shot-counter reset, cleared new weapon flag,
late old callback writes, uncommitted candidate adoption, leaked cancellation
context, untracked failed candidate, and retained destroyed-body reference.

Evidence is written beneath `artifacts/wtt-armory-body-checks/<run>/`. DLL hashes
are recorded alongside the installed-contract result. These checks establish
source, shape, and lifecycle behavior; native AN-94 firing before and after a
real death/respawn is a separate acceptance requirement.

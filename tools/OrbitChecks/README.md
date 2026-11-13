# ORBIT body-replacement checks

Run from the Redeploy repository root:

```powershell
pwsh -NoProfile -File tools/OrbitChecks/Verify.ps1
```

`-PublishedDll <path>` selects the original ORBIT 3.1.1 release DLL when the
ignored audit download is elsewhere. The reviewed SHA-256 is
`008EFDB871A5696740E57A5CD5E32E6063591454E7EEDA78A16E0A4656735EDA`.
The check requires that artifact and never silently skips release verification.

The harness links the production adapter and `BodyReplacementContext`, with
small EFT/BepInEx doubles and a dynamically emitted foreign assembly matching
the released private layout. It exercises read-only preparation/refusal,
same-profile physical aliasing, repeated PMC replacement, own-Scav, exact
committed failure recovery, absent/late manager initialization, stale-world
callbacks, live owner/registration checks, plugin changes and runtime list
drift. It confirms all three consumers retain the original list and observe
the replacement's live position, while unrelated humans and bot state survive.

Release checks read the DLL with Cecil: assembly/plugin identity, every bound
private field, manager initialization, and the actual movement, occupied-door,
convergence and hard-rescue list reads. ORBIT stamps an assembly wildcard
revision (`3.1.1.28828` in the reviewed binary); the runtime contract compares
its first three components to BepInPlugin's version. Newer versions are admitted
only when the entire private contract still matches, with one warning per
resolved plugin identity. No release files are bundled or modified.

`NativeCompile.csproj` compiles the linked adapter against the installed real
EFT/BepInEx/Comfort API. Neither project references Core or deploys anything.
Seven deliberately broken source variants must compile and fail their intended
behavior assertion. `-SkipMutants` is available for quick local iteration;
the full gate does not use it. Bounded build/test logs are stored in a unique
ignored audit folder printed by the script. Success is `ORBIT_CHECKS_PASSED`.

These are offline contract and mutation checks. They do not demonstrate
native bot-recovery timing, visible teleport/door safety, or convergence in a
live raid; those still require the exact installed candidate and a scoped raid.

# PiP Disabler body compatibility checks

`pwsh -File tools/PipDisablerChecks/Verify.ps1 [-PublishedDll <PiP-Disabler.dll>]`

The harness compiles the actual Core adapter and BodyReplacementContext, runs
real HarmonyX patches against deliberately small Unity/EFT/foreign boundaries,
and checks the released 2.1.0 DLL metadata and concrete upstream defects with
Mono.Cecil. The fixture is a test model, not redistributed foreign implementation.

Coverage includes absent/wrong/future plugin identity, changed future teardown
fields, ordinary raid positive controls, canceled preparation, poisoned foreign
preload state, exact native unregister, local/bot/old-body/unmounted optics,
vanilla `__runOriginal=false`, exception unwind, thermal/rangefinder dispatch,
retained corpse mesh restoration, exact cache ownership, unchanged unrelated
empty cache, failed-replacement restoration of the old physical body, repeated
IDs and two body replacements, world retirement, pooled optic reuse, and a late
old-world callback during a second ordinary raid. It additionally links Arena's
actual KitOpticPreloadGuard and PipPreloadCompat to test cold preload enable and
paired disable with real Harmony ordering before any Core ownership exists.
The same-key physical-root handover follows the foreign GetCache-then-Rebind call
order; the old filter must be restored while its cut mesh remains valid for the
new filter. Failed world teardown must release Core's strong lease references.
Pre-Core native death is modeled by detaching the previously observed local gun
and clearing hands before Prepare. Its weak provenance may be adopted only at
Core commit; a gun now parented to a bot remains rejected. Ordinary callbacks and
foreign state are unchanged while provenance is merely being observed.

NativeCompile uses the actual installed EFT/Unity/BepInEx API and compiles only
these production files. It imports no deploy targets. Its output remains under
this harness. No game process is started or changed.

This proves control-flow and API contracts; it does not prove rendered reticles,
live thermal/NVG behavior, FOV Fix interoperability or the native object hierarchy.
Those require the parent task's native test plan.

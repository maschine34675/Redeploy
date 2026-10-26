# Amand's Graphics Core handover checks

This harness exercises the production adapter using real Harmony dispatch on a deliberately small synthetic graphics component. It does not execute Unity or claim visual acceptance.

`Verify.ps1` builds an additional project against the installed EFT/BepInEx API with no deployment, checks the exact reviewed Amand 1.8.0 DLL through Mono.Cecil, and confirms EFT creates the camera before assigning its final name. Pass `-PublishedDll` and `-GameDll` when the reference files live elsewhere. The default evidence directory is ArenaMode's ignored compatibility artifacts.

Covered boundaries: plugin absent, reviewed/newer/older identity, changed layout, real native/compat hook presence, read-only and aborted preparation, multiple engine frames before publication, unpublished failed candidates, exact body/world/camera ownership, deferred destruction, two replacements, retained original scene defaults, live versus destroyed light keys, native Prism re-enable, disabled graphics mode, native activation failure, world retirement and old callbacks. `early-native` covers upstream activation that already bound the new camera before Core's commit. Five deliberate production mutants must fail the matching behavior assertion after compiling.

Native acceptance remains necessary: standalone Redeploy and Arena, actual deaths/respawns, visual ON/OFF results, camera/Prism identities, scoped/flashlight behavior, map-specific settings and optional effects. Do not interpret synthetic activation counters or metadata matches as an in-game pass.

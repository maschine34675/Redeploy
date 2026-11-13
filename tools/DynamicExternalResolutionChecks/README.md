# Dynamic External Resolution body checks

`dotnet run --project tools/DynamicExternalResolutionChecks -c Release` links the
production adapter and replacement context into a .NET Framework fixture. The
game/native callbacks are doubles, while detouring uses the installed Harmony.
The tool has no game-project reference and cannot deploy a mod.
`pwsh -NoProfile -File tools/DynamicExternalResolutionChecks/Verify.ps1` also
requires five compiled runtime mutants to fail their named assertion and reruns
the unchanged baseline afterwards.

It exercises ordinary passthrough, prepared and cancelled replacements, actual
private-cache handoff without a later optic event, creation suppression, retired
gun/optic callbacks (including a detached gun), empty hands, unrelated bot
callbacks, split physical ownership, a second replacement with an own-Scav
profile, legitimately pooled optic reuse, exact world retirement, a following
ordinary raid, and removal of an upstream native hook.

These are lifecycle checks, not rendered evidence of SSAA, DLSS or FSR. Native
tests must aim through an optic before and after a real replacement and check
the actual applied scaler state, HUD projection and return to normal graphics.

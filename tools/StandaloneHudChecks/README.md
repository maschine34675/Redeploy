# Standalone HUD body compatibility checks

Run from the repository root:

```powershell
pwsh -NoProfile -File tools/StandaloneHudChecks/Verify.ps1
```

All builds explicitly use `-p:SPTModType=Library`; this harness never deploys or starts the game. It downloads the two pinned public releases only if the local reference DLLs are missing, verifies their SHA-256, then uses Mono.Cecil to compare every reflected member with the actual released metadata. The normal and synthetic forward-version fixtures execute the production adapters under the installed Harmony library on .NET Framework 4.7.2. The Audio Indicators callback is also invoked through a Harmony-patched native-unregister fixture, rather than only calling the adapter directly.

The runner additionally compiles the production sources against the installed EFT 4.1 managed assemblies, then tests nine source mutants. Evidence is written to `artifacts/standalone-compat-20261029/ui/checks-<id>/`. Successful completion prints `STANDALONE_HUD_CHECKS_PASSED`.

## Reviewed release contracts

| Mod | Plugin GUID | Plugin version | Assembly version | Released DLL SHA-256 |
|---|---|---|---|---|
| Quest Tracker | `xyz.drakia.questtracker` | 1.7.0 | 1.7.0.0 | `A75CC4ADADDE28A7EDC6EF70F89A86AFA981FC6D4829BA0BF8A1E0E76DF1CAB1` |
| Audio Accessibility Indicators | `com.acidphantasm.accessibilityindicators` | 2.1.0 | **1.5.0.0** | `78902CFE2AB1CA47AA26B6277C46E1A440E518801D1584E11D506F15F1778B5F` |

Sources: [Quest Tracker 1.7.0](https://github.com/DrakiaXYZ/SPT-QuestTracker/releases/tag/1.7.0), [Audio Accessibility Indicators 2.1.0](https://github.com/acidphantasm/acidphantasm-accessibilityindicators/releases/tag/2.1.0). Reflection contracts were taken from the downloaded release DLLs, not an assumed repository branch.

Quest Tracker keeps a GameWorld component with a physical `player`, `questController`, and lists of actual Quest objects. Rebinding replaces only those caches and seeds the progress hash. The existing panel, visibility, hide timer, user preferences and settings remain intact. Its subscriptions belong to global settings/tracking events, not to an old quest controller; leaving them attached avoids duplicate callbacks. Native `OnDestroy` still detaches them. `QuestsTracker.Load` belongs to the main-menu PMC preference owner and is deliberately never called on a Scav replacement.

Audio Accessibility Indicators' unregister postfix calls `Panel.Dispose`, which stops `KeepNorthRotation` for every local body. The adapter suppresses that foreign callback only for the exact Core retirement or a previously replaced physical body. The HUD already reads the current MainPlayer's camera each frame, so neither its object nor its indicator pools need recreation. Its update waits during replacement and resumes after confirmed authority. Final current-body unregister still calls Dispose normally.

## Lifecycle coverage

- No plugin and ordinary initial raids: no adoption or foreign mutation.
- Read-only preparation, cancellation, subsequent retry, incomplete creation.
- Failed creation followed by recovery of the original living body: compass resumes only after the transaction finishes and all current-body references/registration are restored. Preparation/freeze state clears without retiring the surviving body, permitting another replacement attempt.
- Same profile ID with a different physical player does not grant callback suppression.
- Repeated PMC replacement and PMC-to-Scav replacement using different quest-controller/Quest objects.
- Completed recovery if the replacement event was missed.
- Tracking preferences, single global subscription, panel identity, visibility and timer survive.
- HUD/rotation identity survives; compass follows the confirmed new physical camera.
- Late old-body unregister cannot stop the current HUD, including after world change.
- Final current-body unregister and native Quest Tracker OnDestroy remain effective.
- Future metadata with matching contracts warns once; older, lying or wrong-GUID metadata refuses.
- Mutants cover profile-only ownership, final-cleanup suppression, old controller, old map cache, stale disposal, cancelled-preparation retention, frozen recovered body, incorrectly retired survivor and missing compass reactivation.

These are source, released-metadata and executable lifecycle tests. They do not establish native EFT visual acceptance; the GPU run must additionally record loaded Core identity, repeated replacements, actual quest-panel state, compass updates, raid exit and test-mod cleanup.

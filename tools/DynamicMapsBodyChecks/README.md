# DynamicMaps body checks

The harness links both production compatibility files and uses bounded foreign
fixtures for the released event/visibility contract. No Core project reference,
deployment target, Unity runtime, or game startup is involved.

```powershell
dotnet run --project tools/DynamicMapsBodyChecks -c Release
dotnet run --project tools/DynamicMapsBodyChecks -c Release -- --legacy
# Also check the exact reviewed official client DLL's private metadata:
dotnet run --project tools/DynamicMapsBodyChecks -c Release -- <path-to-DynamicMaps.dll>
# Run the above and three mutation controls, without building/deploying Core:
pwsh -NoProfile -File tools/DynamicMapsBodyChecks/Verify.ps1 -ReleasedDll <path-to-DynamicMaps.dll>
```

The success marker is `DYNAMICMAPS_BODY_CHECKS_PASSED`. Assertions cover actual
old/new event delivery, idempotence, private arrow property, disabled providers,
continuous/hidden/out-of-raid maps, PMC-to-Scav quest retirement, repeated physical
transitions, stale-body/world rejection, optional-plugin absence, old-version
behavior, contract drift and isolated failures during provider hide/show.

These checks establish lifecycle behavior and the reflection contract, not native
quest-marker rendering or door colours. Native acceptance must keep the minimap
visible across a replacement, verify the new controller identities and exercise
new-body quest/key events.

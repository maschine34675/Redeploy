# AI Limit physical-body checks

Run from the Redeploy root:

```powershell
pwsh -NoProfile -File tools/AiLimitChecks/Verify.ps1 -PublishedDll <released-dvize.AILimit.dll>
```

The default DLL path is the local, ignored download under
`artifacts/standalone-compat-20261029/ai/release/BepInEx/plugins/`. The check
requires the published binary; absence is a failure, not a skipped check.

Reviewed source and release:

- <https://github.com/asler/SPT-AILimit/releases/tag/1.9.2>
- <https://github.com/asler/SPT-AILimit/blob/main/Component.cs>
- `dvize.AILimit-v1.9.2.zip`, DLL SHA256:
  `578EBFE8510087F1B6B58EEA7F53F4B4EE8979640D929E735661A4F3E476CA75`.

The native compile uses the real local EFT/BepInEx references. The behavioral
executable compiles the production adapter and `BodyReplacementContext` against
explicit test doubles. A generated foreign assembly exposes the released private
API and exercises actual reflection/list mutation. Cecil separately verifies the
released binary, including the single `Start` caller of `GetPlayers` and the use
of cached human positions for bot distance. This does not establish in-game
acceptance.

## Four review passes

1. **Released contract:** exact GUID, plugin/assembly version agreement, case
   sensitive namespace names, world/component ownership and field/method shapes.
   Matching forward APIs warn once; old versions and shape drift refuse before
   teardown. Native compile proves actual EFT member compatibility.
2. **Physical ownership:** preparation only records references; publication must
   match the prepared old body and Core-owned, registered, alive, fully committed
   current body. Same-profile peers are preserved; own-Scav continuation does not
   rely on profile IDs. Only one existing list slot changes.
3. **Lifecycle and failure:** ordinary/absent-mod paths remain untouched;
   cancellation clears only pending references. Native initialization before or
   during replacement is respected. Recovery waits for a failure event and the
   end of creation. Wrong-world callbacks, stopped raids, owner mismatch,
   unregistered/dead/unowned candidates and late retirement cannot adopt a body.
   Runtime cache replacement is left untouched and refuses later admission.
4. **Preservation and tests:** bot state, settings, component/list identity and
   unrelated human entries are never reset. Six mutation tests must fail at their
   intended assertion: eager preparation mutation, wrong-human update, missing
   rebind, adopting an unowned candidate, recovery while creating, and foreign
   world retirement. No test installs a mod, deploys a DLL or modifies a profile.

For native acceptance, compare the exact physical references in private
`AILimit.AILimitComponent.players` with `GameWorld.MainPlayer` before and after
ordinary respawn and PMC-to-Scav continuation. Preserve the component/list and
foreign bot state throughout, and verify that distance from a bot follows the
new position rather than the corpse. The parent task owns this separate gate.

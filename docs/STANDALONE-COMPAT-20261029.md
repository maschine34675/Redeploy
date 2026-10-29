# Standalone body compatibility — 29 October 2026

Development Core **1.0.17**, Client/Server **1.0.2**. No release or push is part
of this change. All four integrations live in shared Core and are optional;
ArenaMode is not required and its whitelist is unchanged.

## Implemented behavior

| Mod | Reviewed release | Body-switch correction |
|---|---|---|
| AI Limit | 1.9.2 | Replace the exact old human-player reference used for distance calculations; keep the component, list, other players and bot state. |
| Healing Autocancel | Forge 5.0.1, DLL/plugin 1.1.0 | Move health-event ownership and cached globals to the committed body. Late old-body and nested events cannot cancel another body's treatment. |
| Quest Tracker | 1.7.0 | Rebuild quest/controller caches while keeping the panel, global subscriptions and PMC tracking preferences. |
| Audio Accessibility Indicators | Plugin 2.1.0, assembly 1.5.0.0 | Preserve/resume the existing directional HUD across a handover. Final raid cleanup remains effective. |

Healing Autocancel was already in Arena's whitelist, but had no body-replacement
adapter. The existing **Continuous Healing** and **Quick Cancel** integrations
cover different mods. The current Forge download and installed HealingAutoCancel
DLL are byte-identical (`FA9478A1…2081C11`). Its released handler subscribes only
at `OnGameStarted`, which a Core respawn intentionally does not replay.

Contracts are based on the actual released DLLs. Matching newer APIs are allowed
with a warning; incompatible contracts refuse a replacement before teardown.
Preparation does not reset foreign settings or synthesize a raid start.
Physical-body ownership also supports a different profile in PMC-to-Scav
continuation. Ordinary initial raids and absent mods keep their behavior.

Review additionally caught a failed living-body replacement that restores the
original player: the audio adapter now clears its temporary freeze only after
that exact body is registered and wired again. It does not mark the surviving
body as retired. A separate native Harmony warning was fixed by resolving the
actual declared `ClientGameWorld.UnregisterPlayer` implementation.

## Automated checks

All commands ran against the final production sources, with deployment disabled.

- `dotnet test Redeploy.Tests/Redeploy.Tests.csproj -c Release -p:SPTModType=Library`:
  **469 passed, 0 failed, 0 skipped**.
- `tools/BodyReplacementChecks/Run-Checks.ps1 -Mutations`: **19 cases,
  665 assertions, 9 mutants rejected**. This includes manager dispatch,
  no-mod behavior, refusal, preparation cancellation and old-world disposal.
  The profile-identity mutant was updated to the current separate-profile API.
- `tools/AiLimitChecks/Verify.ps1`: reviewed/newer cases each **59 assertions**,
  older/drift cases each 3, released metadata 11; **6 mutants rejected**.
- `tools/HealingAutoCancelChecks/Verify.ps1`: reviewed/newer each **35**,
  older/drift each 3, early entry 11, recovered old body 7, released/native
  contract 11; **6 mutants rejected**.
- `tools/StandaloneHudChecks/Verify.ps1`: reviewed/newer each **68 assertions**
  under actual Harmony; **9 mutants rejected**.
- Actual EFT-reference compilation for all adapters, Redeploy.Client and
  ArenaMode.Client succeeded. Remaining full-client compiler warnings concern
  pre-existing deprecated APIs; the new adapters compile successfully.

Pinned release sources, hashes and reproducing instructions are in each harness
README. Downloaded mod DLLs, EFT references and build outputs are ignored and are
not committed or bundled.

## Native acceptance

Host **SPT-GPU-2**, SPT **4.1.6**, EFT **0.16.9.40743**, ordinary Factory PMC
raid with low bots and bosses left enabled. ArenaMode.Client was temporarily
disabled. The four reviewed mods loaded successfully; no Arena session/policy
bypass was used. A read-only temporary probe observed foreign fields and physical
object identities; it did not invoke repair methods or mutate mod state.

Final loaded Core:

- Version: `1.0.17+c912ce57ab7082030f520bb434dd820a34755e6d`
  (candidate built from that base plus the changes in this commit).
- SHA-256: `9D702C407DB2F2F8BB043538CF2335195C79F42920464257CDC088A4E15EED3E`.
- Loaded/disk MVID: `c36d612e-9dfb-4093-bd2c-bf2fe85d11b1` — equal.
- Client SHA-256: `84951C14DA4B718278041F94367643F5C1586764A74122A1B71903197C9C9C75`.

Final run, 08:48–08:53 local:

| Stage | Physical body | AI Limit cache | Healing globals | Quest body/controller | Audio HUD |
|---|---:|---|---|---|---|
| Initial PMC | -1385344 | current | current | current | active |
| PMC near corpse | -1566850 | current | current | current | same object, active |
| PMC at spawn | -1597596 | current | current | current | same object, active |
| Own Scav | -1605076 | current, changed profile | current | current | same object, active |

Each replacement had a new health/quest controller; old bodies remained retained.
AI Limit's reference followed the new spawn position. Quest panel `-1545134`
and Audio HUD `-1545194` survived all three handovers. A 90-degree view turn
changed the compass angle from 181.53 to 271.53 degrees. After the first respawn,
a Salewa used through quick slot 4 healed the left arm from 55/60 to 60/60,
consumed exactly 5 charges and returned to normal hands. Raid exit returned to
the menu, and normal Audio Indicators cleanup stopped its rotation component.

Earlier candidates were also exercised, but the table and hashes above describe
the final rerun after both review corrections. Evidence is under the local
ignored `artifacts/standalone-compat-20261029/native/` directory, including
`final-loaded-status.json`, `final-*.json` and `native-final.log`.

### Limits and remaining observations

- The test profile had no active map/tracked quests. Actual objective completion
  and its visual notification were not exercised natively; production cache
  rebinding with distinct Quest objects is covered by the harness.
- The Salewa check proves treatment on the new body, not a timed comparison of
  the automatic-cancel animation. Old/nested callback races and recovery of a
  living original body are automated-test coverage, not injected native faults.
- AI distance ownership was observed directly. No claim is made about every
  distance threshold, bot role or long-session performance setting.
- No new adapter warning/error occurred in the final run. Other startup Harmony
  warnings remain. `MenuScreen` logged one inactive-object coroutine error when
  leaving through the pause menu, without a stack identifying its owner; the
  menu transition completed. The earlier candidate additionally logged an
  inactive `Bot2` coroutine before Scav preparation, absent in the final run.
  Neither is established as a Core regression from this evidence.
- Arena native gameplay was not rerun in this task. Its client builds against
  the shared Core, and early Arena initialization has harness coverage.

## Deployment and cleanup

The exact tested Core DLL/PDB and Redeploy.Client DLL were installed in
`D:\SPT41\BepInEx\plugins` and on GPU2. No server DLL, public version or release
was changed. GPU2 game and server were stopped before cleanup.

Temporary AI Limit, Quest Tracker, Audio Indicators and the probe were removed.
The previously installed Healing Autocancel DLL stayed installed. ArenaMode was
restored. All **219 original configuration files** and the GPU2 test profile
were restored; the two newly created temporary configs and the completed test
journal were removed after archiving. All **nine profile copies** across the
development machine/GPU1/GPU2 matched their original hashes afterwards. Original
Syncthing exclusions were restored last. The final `cleanup-result.json` confirms
this restoration and preservation of the tested production binaries.

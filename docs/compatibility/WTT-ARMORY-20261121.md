# WTT Armory body replacement

Development update, 21 November 2026. Core 1.0.27; public API v1 and the engine statement order remain unchanged.

## Installed contract

The installed client and server report GUID `com.wtt.armory`, runtime version **2.0.5**. Forge labels the distribution **3.0.0**; that package label is not the BepInEx/server metadata used by Arena's whitelist. The client SHA-256 is `78563135fb3c280e87c4e2b6e3b270811a685912f85a2a3940ca8583db3383e9`, server SHA-256 `a323ee985d72dd2f9a185e88ba1dc4cf7e24cd3df3211867bca8b61a3b38653f`.

Armory's `Plugin.You` cache is refreshed only when Unity considers the old player null. Core can retain the outgoing body. Armory's shot patches already update the replacement firearm, but its Update then tests the old controller when deciding whether to reset the shared AN-94 firing timer and burst counters. Missing old hands can prevent the reset entirely.

## Shared repair

The optional adapter validates the installed plugin identity, reflected member signatures and its own targeted hooks at admission. Only a Core-owned replacement grants authority. Preparation captures the exact outgoing body without modifying Armory; successful replacement rebinds to the committed current body and clears transient firing/timer/counters. Hyperburst classification is recomputed from the replacement's actual held weapon and Armory's weapon list. A failed rewire receives the same repair only if the replacement has already been committed as both the game's local player and the world's main player.

Delayed state-writing callbacks from a retired Core body cannot overwrite the successor's global burst state. Their guards use cached typed field access and exact body/world identity; there is no per-frame reflection, scene search, global patch enumeration, or replay of Armory startup. Ordinary unowned players and installations without Armory retain their behavior. Later Armory versions need matching contracts and receive one advisory warning; older than 2.0.5 is not reviewed by this adapter.

The server's startup content and welcome gifts are not changed. Native preservation checks must include `SPT_Runtime/user/profileData/<profile-id>/WTT-Armory.json`, in addition to profiles, mail, PitFireTeam storage and settings. Armory code/assets are neither copied into Core nor bundled by this change.

## Verification

- Independent adversarial review identified and corrected provisional game/world retention after cancellation, early failed-candidate callbacks, and a destroyed Unity body's strong reference at attempt end.
- The production-linked Armory harness passes 81 assertions, including actual installed DLL metadata/IL and real Harmony detours. All eight deliberately broken variants compile and fail the intended behavioral assertion. Parent rerun: `artifacts/armory-body-20261121/parent-recheck/9370737511cb401d83e291e7e7904a9a/`.
- Shared coordinator: 23 scenarios, 1,048 assertions and 19 rejected mutants, including Armory-only admission and omitted attempt-end recovery.
- Core unit/API/golden-order suite: 637 passed. Actual Core and both drivers compile without deploying during verification.
- Arena's affected run completed 50 steps successfully and one existing startup test failed once; the full 568-case startup harness then passed separately. The full xunit run passed 4,422 tests with three known environment skips. No production/test change was made for the transient failure; see the Arena report for its evidence limits.

## Native acceptance on SPT-GPU-2

The official AN-94 preset was fired through native game input. A temporary observer recorded the physical current/cached player, old-body retention, trigger state, ammunition and Armory counters; it never wrote Armory fields or invoked the repair. Standalone Redeploy and Arena ran in separate fresh processes. The standalone process had no Arena client; the Arena process used the candidate default whitelist for Armory, with only the test probe in the temporary user allow list.

| Case | Observed result |
| --- | --- |
| Unfixed Core 1.0.26, initial body | Three 250 ms triggers fired 4/4/4 shots; counters reset after release. |
| Unfixed Core 1.0.26, retained living outgoing body | Armory still referenced the old player. Counters reset while the new trigger remained held; two triggers fired 9/9 shots. The third dry trigger is excluded. |
| Core 1.0.27 standalone, real death and normal PMC respawn choice | The corpse stayed present; Armory followed the new player. With the AN-94 redrawn, three triggers fired 4/4/4 shots, with normal resets after release. |
| Core 1.0.27 standalone, deliberate live Core swap | The old body remained alive and present; the new player owned Armory state. Three triggers fired 4/4/4 shots and reset normally. |
| Core 1.0.27 Arena, real death and automatic respawn | The AN-94 was restored, the old corpse remained, and the new player owned Armory state. Three triggers fired 4/4/4 shots and reset normally. |
| Core 1.0.27 Arena, deliberate live Core swap | The old living body remained; the replacement owned Armory state. Three triggers fired 4/4/4 shots and reset normally. |

The first **pre-respawn** Arena sample yielded 5/4/4 shots, including two shots in one frame. Its trace had no premature reset. This initial timing variation is recorded rather than silently counted as 4/4/4; the test is not a frame-independent fire-rate guarantee. Own-Scav, forced construction failure and delayed callback faults were covered offline, not forced in these native sessions. Only the AN-94 was exercised in-game; this is not a feature-by-feature Armory content review.

Raw reports and preservation receipts are under the ignored `artifacts/armory-body-20261121/native/`; the independent parent report parser covers all eight burst reports and both loaded-identity records (`native-parent-audit.json`). The accepted ten-file manifest is `candidate-a/files.json`. Core is **1.0.27+42839bd**, MVID `1adae98b-a3c1-41cc-ad3a-b60ecd10b42d`, SHA-256 `5740571b7c07f7b427762393f1f00cbea4586be9b88fd5f38e41b0491df73d3d`. Arena client is **0.6.0+552abd3a**, SHA-256 `eaff0bd25daf4e2dabfd590463af8c790db890cdc230c56f29f849370a8edcf8`. These development build stamps precede the evidence commits; `candidate-a/sources.json` binds the reviewed production changes. The server is unchanged at 0.6.0+026d4c44. Nothing is published by this task.

## Preservation and regular deployment

The full Arena clone profile before entry and after the native restore differs only at `characters/pmc/Hideout/sptUpdateLastRunTimestamp`; inventory, skills, quests, statistics, mail and the complete Scav branch are identical. The server's snapshot/restore timestamps identify the same clone. No canonical profile was rolled back.

After cleanup and synchronization, all three installations were checked against their 3,593-file baselines. Every original profile, PitFireTeam file, profileData sidefile and configuration is unchanged; the only three modified baseline files are APBS bot-generation diagnostic logs. Two test-edited settings were restored byte-for-byte and 99 missing historical backup files were restored additively. Ordinary new SPT backups and the newly generated, unchanged-default `com.wtt.armory.cfg` were retained. Twenty-two owned runtime files and three clone-only registry keys were removed, with both disposable profile IDs absent from user data after cleanup. The GPU2 Armory payload remains byte-identical across all 954 files (4,882,035,008 bytes).

The exact accepted ten-file candidate, including matching PDBs and the default whitelist, is deployed on the VM and both GPU hosts; all 30 hashes match. The unchanged server was verified rather than rebuilt. Both GPU games/servers are stopped and GPU2 synchronization is restored to its original active state. Native acceptance was on GPU2; GPU1 was checked for deployment/data preservation, not a second gameplay test.

No Armory/Core compatibility exception was observed. The standalone exit still logged the existing inactive `MenuScreen` coroutine error. Existing Harmony/Fika-helper notices and ArenaMapPort's skipped DynamicMaps integration warning remain outside this fix.

## References

- [WTT Armory on Forge](https://sp-mod.com/mod/2246/wtt-armory)
- [Upstream repository](https://github.com/WelcomeToThursday/WTT-Armory/tree/4.0)
- [Arena's initial installed-WTT audit](../../../ArenaMode/docs/reports/2026-11/WTT-WHITELIST-20261121.md)

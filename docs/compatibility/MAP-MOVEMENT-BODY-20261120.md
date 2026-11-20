# DynamicMaps and Advanced Movement body replacement

Development update, 20 November 2026. Core 1.0.26; public API v1 and the engine statement order remain unchanged.

## DynamicMaps 1.3.0

The retained player arrow already supported the new private Player property. The new gap was the quest controller and inventory/key subscriptions: a continuously visible minimap does not receive a hide/show when Core preserves the battle UI. Every newly constructed local body has fresh controllers.

The existing RebindPlayerMarker boundary now refreshes only the two enabled providers while their screen is active in a raid. Each provider's own hide callback detaches the old controller before show subscribes to the current one. The exact current GameWorld/MainPlayer is required. Repeated calls and providers that naturally refreshed are inert. PMC-to-own-Scav detaches the old PMC quest subscriptions before the upstream Scav early return. Hidden or disabled providers keep their native lifecycle. Failures are isolated between the arrow, quests and doors and produce one bounded warning with a map-reopen fallback.

No raid-end, disable or world-start callback is replayed. Same-world quest scene data is preserved; Core does not discover scene objects or hold old controllers. DynamicMaps 1.2.1 keeps the existing arrow-only path. Arena reviews 1.3.0 in its whitelist and corpse-marker advisory, keeping 1.2.1 as the lower admission bound.

## Advanced Movement 1.0.1

Advanced Movement reads the current local player dynamically in most paths and already ends a move when its owner dies. The vulnerable case is a living body replaced during an active Slide or Dive: its global Busy state and old-body pose, gesture, pin or audio can outlast the handoff.

The optional adapter resolves and validates the private contract once. Preparation captures only Core's exact outgoing physical body and leaves it unchanged until the actual GameWorld.UnregisterPlayer call. It hooks only that method and the bounded runtime world override, including ClientGameWorld's early-return path after a prior death unregister. The hook does nothing outside an active exact-world Core replacement.

Retirement clears the old movement state and restores only its owned native override/speed limit. It avoids upstream ExitMove/ExitDive, which can jump or force prone. Old landing and jump queues, prone settling, gesture, local body animation, firearm pin and audio are released by exact owner. A replacement that already owns one resource, unrelated overrides and remote animation state remain untouched. Each independent resource still gets cleanup if another fails. Contract or cleanup failures refuse later unsafe replacement attempts; preparation cancellation and attempt/world end release provisional references.

There is no per-frame reflection, global Harmony scan, scene search or foreign startup replay. Without the optional mod there is no new runtime hook. Advanced Movement is not added to Arena's default whitelist by this change; general movement-feature overlap is a separate review.

## Offline verification

- DynamicMaps: linked production adapter, 53 lifecycle/official DLL checks, 5 legacy checks and three rejected behavioral mutants.
- Advanced Movement: actual Harmony against production adapter and ownership context, 39 normal, 7 fault and 6 older-version assertions; 35 actual release DLL field/method/IL checks and four rejected behavioral mutants.
- Shared coordinator: 23 scenarios including the new adapter's optional presence, admission, preparation/cancellation and attempt/world retirement; 17 existing rejected mutants.
- Core unit/API/golden order suite: 637 passed.
- Arena corpse integration: installed 1.3.0 contracts, 86 assertions and 11 rejected mutants; existing corpse cleanup regression passed.

## Native acceptance on SPT-GPU-2

Tested with the official DynamicMaps 1.3.0 and Advanced Movement 1.0.1 DLLs, first with standalone Redeploy (Arena client absent), then in Arena Deathmatch. The disposable profile had Arena rewards disabled. Loaded assembly identities were checked in each fresh process, rather than inferred from files on disk.

- Standalone: real Slide and Dive starts followed by Core body replacements while the outgoing body remained alive and retained. The old move, gesture, animator and audio ownership retired before profile mutation. The minimap followed the new body; old quest/inventory delegates were absent and the new controller had the expected subscriptions.
- Real key movement between pockets and native key dropping updated the new inventory provider. The inventory-give probe did not raise native inventory events and is not counted as this acceptance.
- Death during Slide followed by the normal Redeploy PMC choice passed. Death during Dive followed by the normal own-Scav choice passed: old PMC quest delegates were detached, Scav quest state stayed empty, and the new body could walk and fire (magazine 5 to 4).
- Arena: a real death while holding a VSS produced a new firearm controller; three subsequent shots reduced ammunition from 11 to 8. A separate armed Slide replacement retained the living outgoing body and cleared its movement/gesture/audio ownership. Across 352 observed frames the minimap stayed active and in-raid without an interruption, excluding a native hide/show cycle as the explanation for recovery.
- Negative control: hiding DynamicMaps with End before a real Arena death left it hidden after respawn. Showing it again bound the current body correctly.

No actual quest completion was performed. Native evidence proves the new/old controller subscriptions and actual inventory events; quest event delivery and legacy behavior are covered by the offline checks. This does not approve every interaction between Arena's movement mutators and Advanced Movement.

The final Arena log contains 26 existing EFT diagnostics (muzzle materials, foldable/weapon animation setup, Factory spawn categories and Transit); matching messages predate this change. No new DynamicMaps/Advanced Movement/Core compatibility exception or stuck replacement was observed. The standalone MenuScreen exit coroutine diagnostic also predates the change.

Cleanup removed 28 owned temporary files (including clone-only Bot Placement storage), restored both changed settings byte-for-byte and additively restored 77 original backups rotated by SPT. All 213 canonical profile/PFT file checks across the VM and both GPU hosts matched their initial hashes. Server startup legitimately reconciled five older reward journals with zero eligible rewards, without changing a canonical profile; those records and APBS generation logs were retained. No original profile was rolled back.

Detailed receipts, loaded identities, frame samples, full logs and the data preservation audit are under the ignored `artifacts/body-compat-20261120/native/` directory. The final accepted bundle is `candidate-b/files.json` beside it. Core is 1.0.26+5283c7c, MVID `09757c01-4ad6-445f-b60d-1066e70cb7d7`, SHA-256 `ef3f5e15f2c091374b027d1632526f9d46851ae0b770934115dff22ecb8b6034`. Arena's accepted client is 0.6.0+026d4c44, SHA-256 `385305a694f39b3750077c3185eaa445eb69936b8820e6fba1e6a305e7a56e15`. These are development build stamps preceding the evidence commits, not new releases.

## Regular deployment

After the final 64-step Arena verification passed, those exact ten accepted files (including matching PDBs and the whitelist) were deployed to the VM and synchronized to GPU1/GPU2. All ten hashes match on each root. Synchronization is restored, and both GPU games/servers are stopped. The final post-sync preservation audit checks 309 original profile/PFT/settings files per root: all match, and all 28 removed runtime test files remain absent. Native execution was on GPU2; GPU1's result is deployment/data verification, not a second gameplay run. No release or push was performed.

## References

- [DynamicMaps 1.3.0](https://github.com/acidphantasm/SPT-DynamicMaps/releases/tag/1.3.0)
- [Advanced Movement 1.0.1](https://github.com/SageSPT/Advanced-Movement/releases/tag/1.0.1)
- [Prior source/release audit](RESPAWN-CANDIDATES-20261120.md)

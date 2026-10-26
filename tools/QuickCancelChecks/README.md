# Quick Cancel body-lifetime checks

`Verify.ps1 -PublishedDll <QuickCancelClient.dll>` first compiles the production adapter and context against actual installed EFT/Unity/BepInEx DLLs using `NativeCompile.csproj`, then builds the isolated .NET Framework executable. Neither project deploys. The executable runs the production adapter and `BodyReplacementContext` with real installed Harmony and explicit physical-body/world/loader boundaries. Four compiled mutants must fail assertions. The published-DLL check verifies the released GUID/version, exact SHA-256, strong pending set and Mark signature. These are offline contract/scope checks, not native gameplay acceptance.

## Demonstrated lifetime defect

Quick Cancel 2.0.0 marks a `MedsController` in a static strong `HashSet`, then calls `InventoryController.TryThrowItem`. Removal occurs in its MedsController.Drop prefix, on a failed throw/catch, or when the whole plugin is destroyed. SPT's `Player.SinglePlayerInventoryController.ExecuteAsync` yields once while alive. If the player dies before continuation, `Player.TryRemoveFromHands` can return **success** for a dead player or missing hands without calling Drop. The successful callback does not clear Quick Cancel's set. Native `MedsController.Destroy` also does not clear that foreign set. Repeated Core body swaps can therefore retain old controllers/bodies even if subsequent quick cancels still work.

The adapter removes entries for the exact old physical body only when Core really calls the world's unregister entry. Prepare is read-only, so a refused replacement does not change the mod. A weak retired-body tombstone refuses a delayed Mark for that retired body. No global Clear, profile-ID matching, inventory action, foreign option or ordinary bot/player is modified.

## Native acceptance

Capture actual QuickCancelClient 2.0.0 assembly location/hash and configuration; perform ordinary control, standalone Redeploy and Arena tests using temporary disposable profiles. Do not change canonical profile inventory to fabricate fixtures.

1. Ordinary PMC: begin using a food/medical item and deliver the configured native QuickReload command (default double R). Confirm a real dropped usable item, unchanged configuration and working next firearm. Read `Manimal.QuickCancel.QuickCancelState.PendingControllers` before/after; normal completed Drop must leave no pending entry.
2. Repeat on a replacement body under standalone Redeploy and Arena. Record physical player/controller identities and `_player` ownership rather than matching profile ID.
3. Exercise the interrupted window: capture a pending Mark/throw, then death before queued inventory work reaches Drop. Read the foreign set; old pending controller retention must be observable before Core's actual unregister and absent afterward. The next body's pending cancellation must not be removed. This race can be difficult to hit; report unobserved if timing was not proven, rather than claiming interruption coverage from a later death.
4. Read pending set after two replacements, raid exit and next raid. No retired Core-owned controller may remain. Unowned control-player work must not be cleared. Confirm next-body cancellation and normal firing/reloading still work.
5. Check native logs for old-hand-controller callbacks and delayed operation errors. The adapter only retires Quick Cancel's pending membership; it does not cancel or complete unrelated native inventory transactions.

Root wiring: include `QuickCancelCompat.PluginGuid` in Core optional-mod presence; aggregate `GetRejectionReason()`; call `Prepare(game, oldBody)` after `BodyReplacementContext.Begin`; call `RetireWorld(world)` before context reset. There is no adapter-specific Tick/Abort/EndAttempt callback. Concrete runtime unregister overrides are patched during Prepare because an already-unregistered client override can return before calling the base method.

# Scav Recovery Native Probe

Development-only plugin, GUID `com.maschine.RedeployRecoveryProbe`, dependent on
`com.maschine.Redeploy`. Build with:

```powershell
dotnet build tools/ScavRecoveryNativeProbe/ScavRecoveryNativeProbe.csproj -c Release --nologo
```

`SPTModType=Probe` prevents automatic deployment. The probe does not belong in a
release package. Deployment, isolated profiles/backups and controlled process
termination remain the operator's responsibility.

All five public UiBridge actions are explicit controls. There is no automatic
fault injection, seeding or native movement. Actions execute on the Unity thread.
Creating test state requires the exact single-player `LocalGame` from Redeploy's current-game reference, its
current local body and no foreign human players. Redeploy first stores that
game reference at death interception: the ordinary PMC movement control must
therefore run after an ordinary PMC respawn. Prompt actions accept that body's
dead state. The BTR movement action requires a living body registered in the native
alive-player lookup. Status and exact-owned cleanup remain available after a raid.

| Action | Meaning |
| --- | --- |
| `Status()` | Reports current body instance ID, adoption, ownership, flags and counts, plus the read-only `preparation` diagnostics below. No tokens, journals or profile snapshots. |
| `HoldActivation(true)` | Arm on the original PMC. Suppress only the later activation acknowledgment for this exact game's adopted Scav. The native preparation/body switch still runs. |
| `HoldActivation(false)` | Disarm. Does not send or replay a suppressed acknowledgment. New-game detection also disarms. |
| `SetScavExits(false)` | Save the exact scene controller and exit array, then install a distinct empty array to test the prompt. |
| `SetScavExits(true)` | Restore only if that controller still holds the probe's exact array. Ownership drift returns an error. |
| `SeedPriorDelivery(true)` | Append a new, uniquely identified grid with one newly minted RUB to an existing native transit delivery stash. No player items move. |
| `SeedPriorDelivery(false)` | Remove only that minted item and exact appended grid. Restore before selecting PMC respawn. Foreign contents prevent removal and produce an error. |
| `BtrMoveProbe()` | Create a temporary native `BtrTransferItemsController`, initialize native server/player stashes, mint one RUB into its temporary grid and invoke the actual patched `MoveItemsFromTempStashToTransferStash`. Normal PMC expects `moved=1`, adopted Scav expects `moved=0`. |

The movement result includes before/after temporary and delivery item counts,
adoption, `HasCurrentBody`, body ID and item-owner counts. The `finally` cleanup
removes only newly registered owners whose root is one of this controller's
exact temporary stash references. It never removes an existing owner based on
profile ID or trader ID and never touches real player inventory.

Prior-delivery and exit state are cleaned defensively before native `GameEnd`
can serialize a report, on scene change, and on plugin destruction. This cleanup
does not start a test or mutate an unrelated scene. Unexpected ownership drift
fails visibly instead of overwriting foreign state. Inspect `Status()` and
require no active exit/delivery overrides before leaving the isolated test.

Native movement coverage is intentionally bounded: this runs the actual patched
native item-movement boundary with a synthetic controller and test item. It does
not prove a BTR ride, dialogue rendering, service pricing or a UI purchase.

## Read-only preparation diagnostics

`Status().preparation` reads existing state through reflection. It adds no hooks,
starts no timers or loading jobs, does not alter production state, and never reads
or serializes lease tokens, generation hashes, profile snapshots or task results.
Diagnostic lookup failures appear as `available=false` with an exception type;
the original status fields remain available.

- `frame` and `realtime` distinguish advancing Unity frames from a stopped main thread.
- `contextPresent`, `contextGameMatches`, `preparedScavPresent` and
  `preparedScavIsCurrentProfile` distinguish an absent preparation context, a
  parsed Scav reservation and an adopted profile. These are booleans only.
- `respawnInProgress`, `pendingChoice`, `ending` and `backendAcknowledged` are
  existing client flags; an absent reflected value is `null`, not `false`.
- `pipelineRunning` and `replacementCreating` describe existing Core state.
  `coreStateAvailable` and `replacementContextAvailable` report type lookup.
  `previousBodyId` and `lastRespawnedBodyId` are Unity instance IDs only; an older
  respawned body does not prove that the current attempt advanced.
- `timingActive`, `timingStartFrame` and `lastCompletedStage` read the optional
  existing Core timing collector. The stage is the last completed mark, not the
  currently executing method. With timing disabled or no active measurement,
  no stage is available; this probe does not enable timing.

A present reserved Scav with `pipelineRunning=false`, an unchanged dead PMC and
advancing frames points toward preparation/resource loading before Core. It does
not prove a particular bundle or job is stalled. The HTTP and preload tasks are
coroutine-local variables and are not measured by this snapshot. Compare repeated
explicit status calls and native logs; do not infer a missing task as completed.

Verified EFT 40743 source boundaries:

- `EFT/TransferItemsController.cs`: `InitItemControllerServer` (182),
  `InitPlayerStash` (133), `GetOrAddTransferContainer` (146), and
  `MoveItemsFromTempStashToTransferStash` (195).
- `EFT/BtrTransferItemsController.cs`: constructor (12) and native grid size (18).
- `EFT/BaseLocalGame.cs`: transfer serialization (1036), `GameEnd` (1054).
- `CommonAssets.Scripts.Game/ExfiltrationController.cs`: owned Scav exit array (187).

Offline compilation verifies binding only. The root operator must verify native
control/guard counts, cleanup, prepared acknowledgment loss, recovery and restored
test state before treating any acceptance gate as passed.

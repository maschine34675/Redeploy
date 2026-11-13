# Arena-denied mods in standalone Redeploy

Reviewed 13 November 2026 against Redeploy `5644cdb` / Core 1.0.23. This is a
source and released-DLL audit, **not native compatibility acceptance**. No mod
was installed for this review, no gameplay code was changed, and Arena's deny
list was not relaxed. An Arena exclusion can concern spawning, AI ownership or
profile isolation; it does not by itself imply a standalone respawn defect.

## Findings

| Mod | Reviewed release | Result and proposed priority |
| --- | --- | --- |
| ORBIT | 3.1.1 | Fix warranted: a copied human-player list omits replacement bodies from movement safety and convergence. First implementation priority. |
| Questing Bots | 1.2.0 | Fix warranted on Lighthouse: stale Lightkeeper-island membership can affect the replacement PMC's alliances, or insert a null ally after an own-Scav switch. |
| ISB Special Forces / SOF Notifier | 2.0.4 | Fix warranted: the notifier permanently closes its local presentation on the first death. This does not establish a broken bot-spawn system. |
| ISB Aishi | 2.0.7 | Investigate a delayed camera-controller continuation during replacement. The missing post-await identity check is visible in source; the interleaving is not reproduced. |
| Adaptive Bot Culling | 1.2.0 | No specific body-replacement adapter justified by the reviewed release. Test its camera/culling transition before claiming compatibility. |

All five are listed for SPT 4.1.6 by their authors. The current Arena deny identities
are `com.chazut.orbit`, `com.danw.questingbots`, `com.hysocs.adaptivebotculling`,
`com.samc137.isbspecialforces` and `com.samc137.aishi`. These remain unchanged.

### Evidence identities

| Release | Source revision | Examined binary SHA-256 |
| --- | --- | --- |
| [ORBIT 3.1.1](https://github.com/Chazut/ORBIT/releases/tag/3.1.1) | `702d3904a65c659ae9ceb3a0e6bd822077c12e1e` | `ORBIT.dll`: `008EFDB871A5696740E57A5CD5E32E6063591454E7EEDA78A16E0A4656735EDA` |
| [Questing Bots 1.2.0](https://github.com/dwesterwick/SPTQuestingBots/releases/tag/1.2.0) | `9f2c7dd7a2d73a4eb7a2e74d4cc1a4bfaf147cef` | client: `3FE5181F2EF50EE2D99EBE9A1202F9FFA4A6C5DAD411789773852D4BF77CC94C` |
| [ISB Special Forces 2.0.4](https://github.com/Sam-C-137/ISB-Special-Forces/releases/tag/v2.0.4) | `e108bd0083dd4528ecc6c2f4cd81907ba3ff47e9` | `ISBNotify.dll`: `1DBFD16EB0F498992A06D075766FA79EE5F5BE66526FCAA00A4E9F3814B9F3DA` |
| [Adaptive Bot Culling 1.2.0](https://github.com/Hysocs/adaptivebotculling-spt/releases/tag/1.2.0) | tag `66d0e50e39deba7191a45f09f11becb051d0e54d` is stale | client: `67AE69DFC1DDA3F7DFB41CDB424348489D2EADF3E9531EC82BD6AA5964778ACB` |
| [ISB Aishi 2.0.7](https://sp-mod.com/mod/2478/isb-aishi) | `7c1935cc3426080e781d60bad59646752c63d8c8` | Source only; release binary was not inspected. |

The affected ORBIT, Questing and ISB notifier methods were checked against the
actual released binaries, not just their tags. ORBIT's server carries an older
informational commit stamp, but no server files changed between that commit and
the release tag. Adaptive's 1.2.0 tag still contains 1.1.0 code, so the culling
assessment uses the released DLL instead. Downloads/decompiles are ignored audit
artifacts under `artifacts/blocked-mod-review-20261113/`, never distribution files.

## Core's existing ownership behavior

`RespawnEngine.RunRespawn` unregisters the old physical player, creates and
registers a new player, removes the old bot-controller information and adds the
new active player. It commits `MainPlayer`, the game profile and player owner,
then publishes `RespawnApi.Replaced`. It does not restart the `GameWorld` or
repeat `OnGameStarted`. A normal PMC respawn can reuse the same profile ID;
own-Scav continuation changes profile as well as physical body.

Verified against `Redeploy.Core/Engine/RespawnEngine.cs` (unregister, creation,
bot rewiring and publication), and the local EFT decompile's
`GameWorld.RegisterPlayer` / `UnregisterPlayer` (lines 2012-2086). Native world
registration updates `AllAlivePlayersList` and the current player reference.
A foreign one-time copy of that list does not receive those changes.

## ORBIT: copied human list

[OrbitManager](https://github.com/Chazut/ORBIT/blob/702d3904a65c659ae9ceb3a0e6bd822077c12e1e/Orbit/Core/OrbitManager.cs#L92-L113)
copies non-AI players at construction and passes the same list to MovementSystem
and WaypointSystem. No later refresh adds the replacement body. Once the old
body is dead, consumers skip it and do not see the living replacement. This
applies to both same-profile PMC respawn and own-Scav continuation.

The concrete losses are the human proximity/visibility checks around
[recovery teleports](https://github.com/Chazut/ORBIT/blob/702d3904a65c659ae9ceb3a0e6bd822077c12e1e/Orbit/Systems/MovementSystem.cs#L1541-L1566),
the human part of
[occupied-door checks](https://github.com/Chazut/ORBIT/blob/702d3904a65c659ae9ceb3a0e6bd822077c12e1e/Orbit/Systems/MovementSystem.DoorRecovery.cs#L69-L81),
and enabled
[convergence](https://github.com/Chazut/ORBIT/blob/702d3904a65c659ae9ceb3a0e6bd822077c12e1e/Orbit/Systems/WaypointSystem.cs#L277-L307).
Convergence is off by default; when enabled, its next calculation sees no living
human in the copied list and clears the attraction field. The missed conditions
are deterministic from the release code; visible teleports or door interference
still require a corresponding bot-recovery situation and have not been observed.

An upstream fix can consume `LocalBodyTracker.BodyChanged` or refresh the list
from the current world. For the unchanged release, a narrow Core adapter is
justified by inaccessible private state: validate the shared-list contract and
update the **original list in place** after an owned replacement. Merely assigning
a new list to one field misses the reference retained by `HardStuckRemediation`.
Do not restart ORBIT, rebuild its entire raid state or reset every ghost.

Its public
[bot-control API](https://github.com/Chazut/ORBIT/blob/702d3904a65c659ae9ceb3a0e6bd822077c12e1e/Orbit/Api/OrbitBotControl.cs#L10-L46)
controls bot exclusion and sleeping; it has no human-list refresh operation.
The separate dormancy system already scans current live players and reacquires
camera state. Its spectator suspension resumes when a living human returns.
That path may temporarily wake ghosts during a long death prompt, but no permanent
ghost/camera failure was established. Corpse looting uses `corpse.Item`; no redirection
to the replacement's shared profile inventory was found.

## Questing Bots: Lightkeeper-island membership

The general player lookup, AI limiter and spawn planner read current world
players. Sleeping decisions are refreshed every 250 ms, with no humans causing
bots to stay awake. Questing shutdown follows `StopGettingInfo`, which Core does
not invoke for an intercepted death. No general player-cache or generator reset
is warranted by these paths.

The exception is
[LightkeeperIslandMonitor](https://github.com/dwesterwick/SPTQuestingBots/blob/9f2c7dd7a2d73a4eb7a2e74d4cc1a4bfaf147cef/Client/Components/LightkeeperIslandMonitor.cs#L60-L70).
Its private `playersOnIsland` contains physical players. Updates only visit the
world's current live-player list; removal happens when that same object is later
seen outside the island. Death/unregistration therefore leaves a stale member.

Concrete case: die on the island, respawn outside it as the same PMC, then let an
ordinary bot enter. The bot's alliance setup still visits the old corpse.
[BotGroupHelpers](https://github.com/dwesterwick/SPTQuestingBots/blob/9f2c7dd7a2d73a4eb7a2e74d4cc1a4bfaf147cef/Client/Helpers/BotGroupHelpers.cs#L97-L167)
removes enemies by profile ID and resolves the old object to the currently alive
player with that ID. The replacement PMC outside the island can consequently be
added as an ally. The human/Zryachiy exclusion does not prevent the ordinary-bot case.

With own-Scav continuation the old PMC ID has no living match. `AddAlly(null)`
is possible instead. EFT's `BotsGroup.AddAlly` accepts that value without an immediate
exception. A later null dereference is conditional: `BotMemory.GetHitInner`
(local decompile lines 923-936) reads every ally's GroupId without a null guard
when `REVENGE_TO_GROUP` is enabled and the attacker's group ID is nonempty.
This is a code-demonstrated risk, not an observed in-game crash.

There is no public retirement API; the exposed island list is read-only. Calling
the ordinary island check on a corpse still geometrically on the island does not
remove it. A narrow Core adapter should retire the exact old physical body and
its scoped alliance/enemy snapshots before stale profile-ID resolution can reach
the replacement. Reassess the new body by its own location. The exact restoration
ordering needs implementation review: invoking the existing broad alliance revert
after the same-profile replacement is registered may already affect the new PMC.
Do not transplant island membership by profile ID or reset the quest generators.

## ISB Special Forces: notifier closes for the whole raid

The affected component has its own GUID **`com.samc137.ISBinfo`** (SOF Notifier
2.0.4), distinct from the main package's Arena-denied GUID.
[RefreshLocalRaidPresentationState](https://github.com/Sam-C-137/ISB-Special-Forces/blob/e108bd0083dd4528ecc6c2f4cd81907ba3ff47e9/ISBNotify/ISBNotify.cs#L557-L607)
sets private `LocalRaidPresentationEnded` when the local body is dead, absent or
unregistered. Its first guard then prevents detection of a later replacement.
The flag resets only in the full startup/teardown reset, not on a body change.
Announcements and the manual O query remain suppressed in the continuing raid.

This sequence is confirmed in the released notifier DLL. It warrants a targeted
reopening after a verified same-world replacement, respecting plugin settings and
real raid end. There is no public targeted reopen API; public
`MarkLocalRaidPresentationEnded` only closes it. Avoid the broad private `ResetState`,
which also clears unrelated counters and announcement history. An upstream interop
integration is preferable; an adapter for the unchanged private-state contract is
also possible without a public API expansion.

The hunting controllers already refresh live targets, so this finding does not
establish that ISB bots retain the old player. Whether PMC revenge/progression
should carry into an own-Scav continuation is a separate behavior question; do
not automatically transfer it as part of a notifier fix.

## Aishi and Adaptive Bot Culling

[AishiHammerheadVision.RefreshCamera](https://github.com/Sam-C-137/ISB-Aishi/blob/7c1935cc3426080e781d60bad59646752c63d8c8/AishiKeysV2/AishiKeys/AishiHammerheadVision.cs#L131-L156)
captures a PlayerCameraController, awaits, then can use that captured controller
without checking its owner again. Core replaces the controller while the shared
camera manager survives. The busy flag also delays the component's death cleanup.
Test a Hammerhead mode change overlapping death/replacement before adding a shim;
the continuation after one or more 16-ms delays spanning the actual replacement
has not been demonstrated. Scheduling can extend the delay.
Aishi's main anomaly runtime already polls the current player every half-second.
Its defibrillator preventing actual death is normal item behavior, not by itself
a conflict with Redeploy's handling of an already occurred death.

Adaptive's released 1.2.0 DLL reacquires CameraManager and scans current
RegisteredPlayers on each culling update. It does not retain the first local body
in those paths. Test reactivation near a distant respawn, ADS, and own-Scav
continuation. Do not add a speculative cache adapter or label the mod fully
compatible from this source review alone.

## Implementation and acceptance order

1. ORBIT's shared human-list handover; same-profile respawn twice and own-Scav,
   then bot recovery near the player/occupied doorway and enabled convergence.
2. Questing's island-state retirement; deaths on the island followed by respawn
   outside and inside, a later ordinary bot entering, same PMC and own Scav,
   no stale/null allies, subsequent exit and a fresh raid.
3. ISB notifier reopening; enable the notifier, wait more than half a second in
   the death prompt, respawn, request status and trigger a new announcement
   without replaying the initial announcement. Exercise PMC respawn and own-Scav
   separately; real give-up and extraction must still close presentation.
4. Native timing probe for Aishi and culling regression check for Adaptive.

Any Core adapter must remain optional, bind by actual plugin identity, retain
same-world/physical-body ownership and failure cleanup, and do no new per-frame
scene searches or repeated Harmony-wide unpatch walks when the mod is absent.
Use the existing API v1/interop; no engine/public API expansion is justified by
this audit. Test preparation refusal and same-profile aliasing offline as well
as native gameplay. Keep Arena's five exclusions until a separate Arena-specific
compatibility effort addresses their original reasons.

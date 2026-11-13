# AI-mod body replacement integrations

Core 1.0.24, 13 November 2026. Client/Server remain 1.1.2 and API v1 is unchanged.
This implements the three demonstrated private-state defects from the
[Arena-denied mod audit](ARENA-DENIED-MODS-20261113.md), which records the exact
released DLL hashes and source revisions. No Arena whitelist or exclusion is
changed. A standalone body fix is not certification of an Arena combination.

## Implemented scope

| Optional plugin | Reviewed version | Correction |
| --- | --- | --- |
| ORBIT (`com.chazut.orbit`) | 3.1.1 | Replace the exact outgoing entry in the original human-player list after a committed replacement; validate that Movement, Waypoint and HardStuck systems retain that same list. |
| Questing Bots (`com.danw.questingbots`) | 1.2.0 | Retire the outgoing human's island membership, scoped ally references and original relationship snapshots before the replacement registers; never transfer membership by profile ID. |
| ISB SOF Notifier (`com.samc137.ISBinfo`) | 2.0.4 | Clear only its private local-presentation-ended latch after a verified successful body change; preserve all raid counters and announcement history. |

Each adapter binds the actual loaded plugin and private member contract. Older
versions are refused for replacement; newer versions are allowed only when the
reviewed shape still matches, with a warning that gameplay remains unverified.
The ISB notifier GUID differs from the main Special Forces package GUID.

ORBIT keeps the original collection because multiple subsystems retain it. A
late native manager may initialize against the replacement; a changed existing
manager or list is never silently adopted. Failure recovery can rebind only a
living physical body already owned and committed by Core.

Questing cleanup runs at Core's exact old-body unregistration, after read-only
preparation and before successor construction. Same-profile PMC respawns cannot
redirect a corpse to the successor, and own-Scav continuation cannot resolve the
old PMC to a null ally. Delayed checks cannot readmit the retired body. A failed
live-body replacement can recover the old body with its scoped relationship
history intact, including recovery outside the island and bots that arrived
while it was absent. Quest generation, sleeping and spawning are untouched.
Both `GameWorld.UnregisterPlayer` and the declared `ClientGameWorld` override
are covered: EFT can unregister a corpse before the respawn request, causing
the later override to skip its base method. Cleanup is consumed once even when
both methods execute. Dead bodies retain no rollback snapshots; successful
publication also releases the live-body recovery snapshots.

ISB authorization is one-shot and requires publication of a successful
replacement in the same continuing world. A failed rewire alone does not reopen
presentation. Native explicit presentation end and native reset invalidate
pending authorization. The adapter never invokes the broad `ResetState` method
and does not transfer PMC revenge/progression behavior to a Scav.

No adapter performs per-frame scene searches. Missing plugins do not install
patches or cause repeated global Harmony unpatch walks. The shared engine's
statement order and public event/API contracts are unchanged.

## Offline verification

The adapter harnesses compile linked production code against the installed EFT
API and exercise private contracts and physical ownership using controlled
fixtures. They also check the actual reviewed release metadata/IL. Run:

```powershell
pwsh -NoProfile -File tools/OrbitChecks/Verify.ps1
pwsh -NoProfile -File tools/QuestingBotsChecks/Verify.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/IsbNotifierChecks/Verify.ps1
```

Coverage includes absent plugins, minimum/newer/drifted contracts, read-only
preparation and refusal, repeated same-ID replacements, own Scav, failure
recovery, world retirement and targeted mutation checks. Independent cross-review
found and corrected Questing's live-old-body recovery/exit case before acceptance.
The central harness covers adapter admission, preparation, cancellation and
cleanup; xunit includes the new adapters in the Harmony unpatch performance guard.
The central composition harness passed 23 scenarios and 973 assertions; all 17
deliberately broken variants failed their intended assertions. The ISB main-only
case uses the actual main plugin GUID and confirms it cannot activate notifier
repair by itself.

On the final production sources, `Redeploy.Tests` passed 636/636 and
`Redeploy.Server.Tests` passed 162/162, with no skips. The interop harness passed
47 checks. Release builds of Core, both drivers and the observation probe passed
without deploying; the Redeploy client reports eight existing `InGameStatus.InRaid`
deprecation warnings. The Questing harness additionally models the conditional
client-world override and an already-unregistered dead player. All three adapter
harnesses passed their released-binary checks and real-EFT compile checks.

## Native verification — SPT-GPU-2, 13 November 2026

Scoped standalone tests passed after the host was released by the other session.
Each case used the official mod package, a disposable profile and native Redeploy
prompt buttons. ArenaMode and Killcam were temporarily disabled. Synchronization
remained paused throughout the isolated tests. Deaths were triggered through
UiBridge; the developer-only [probe](../../tools/DeniedModsNativeProbe/README.md)
observed private state without repairing it.

All three processes loaded Core `1.0.24+0a4f242` with disk and loaded MVID
`265f2aa2-bfd8-4734-8a10-e4e2e3ae004d`. Candidate SHA-256:
`7545749aa7382dace92822d94f4fc9920499a3509c91c36bd5e1c83ff59923bb`.
Client remained `1.1.2+0a4f242`. These identities describe the tested candidate,
not a deployment to the shared installation.

| Package and map | Native observation |
| --- | --- |
| ISB Special Forces 2.0.4, Aishi 2.0.7, required MoreBotsAPI 2.1.2; Factory | PMC death/respawn followed by own-Scav continuation. The presentation-ended latch changed from true on death to false after each committed replacement. Two live ISB operators and existing counters/history remained. Holding O displayed the real ISB activity notification after both replacements. |
| ORBIT 3.1.1; Customs | Two PMC respawns and own-Scav continuation. Manager and shared list identities stayed constant; each old body was replaced in the original collection used by all three subsystems. Enabled convergence naturally changed from one live human to zero after death and back to one after each replacement. |
| Questing Bots 1.2.0; Lighthouse | PMC respawn outside the island, then own Scav. Old physical membership and live ally references were removed. Before the Scav switch, a positive original-snapshot reference was also present; it changed from one to zero. Both replacement bodies entered and left the actual island bounds normally. No null ally appeared. |

Questing's island scenario included a living ordinary scav, since Zryachiy and
his followers deliberately do not receive the human alliance. That ordinary bot
survived both replacements. Another ordinary scav arrived after the PMC respawn
(life-event #37), without reviving old membership or allies. For the subsequent
Scav switch, frame 34136 recorded one old ally and one old snapshot reference;
frame 36296 recorded neither, with a different live Savage body in the same world
and monitor. After Scav entry and exit both old-reference counters remained zero.
Additional spawn requests after the Scav switch did not produce a recorded bot
arrival, so the post-Scav newcomer case is not claimed as natively exercised.

ORBIT's five-second sample observed six of ten sampled bots moving and nine using
its brain layer. This does not establish every door-occupancy or stuck-teleport
recovery branch. The second PMC near-corpse request correctly fell back to a map
spawn because the corpse was in a sniper/mine exclusion zone; that was the sole
Redeploy warning in that run. No Redeploy warning/error was found in the complete
ISB or Questing client logs. All three tests returned through the normal raid-end
screens to the main menu.

### Separate findings and limits

- Aishi's anomaly-reveal feature reported `The active profile changed. Try again.`
  after own-Scav continuation. Its client reads the current player's profile,
  while its settings/reveal server routes return the PMC profile and consume PMC
  inventory. Source and released DLL review confirmed this mismatch. This is not
  a stale-body cache, and bypassing its identity check could charge the wrong
  inventory. No PMC progression or reveal permission is transferred to Scav.
  A normal Scav raid was not tested for comparison.
- The first ISB startup omitted required MoreBotsAPI and the server rejected mod
  loading. It is not acceptance evidence. The complete dependency was installed
  before the accepted run. Original profiles were isolated before further tests.
- Questing logged a missing NavMesh point for `Radio transmitter body` during
  quest preparation, before the first respawn. It also logged expected island
  boss-alliance exclusions, missing quests for custom maps, and bot heal/path
  warnings. These runs therefore do not establish an entirely clean mod stack.
  The server also logged one Fence armor-slot generation warning during Scav
  preparation. No observed stack trace linked these findings to the new adapters.
- Forced replacement failure/recovery remains covered offline, not by native
  fault injection. Arena combinations remain excluded and were not tested here.

### Evidence and restoration

Local, ignored evidence is in `artifacts/denied-native-20261113`: loaded-process
JSON for all three runs, complete client logs, the server log, ORBIT's 31-step
scenario and assertions, and `observations-complete.json`. Accepted client log
start times are 22:04:52 (ISB), 22:14:42 (ORBIT) and 22:23:38 (Questing), host local
time. The failed ISB startup at 22:01:14 is kept separately.

After normal raid exit, game and server were stopped. Test packages, generated
settings, ORBIT debug files and the observation DLL were moved out of the
synchronized installation. All nine temporarily disabled paths were restored.
The disposable profile tree was replaced with the initial backup, including
original files affected by server validation when mod loading failed. The
intermediate profile state remains preserved off-sync; it was not substituted
for the initial backup.

Restoration verified 310/310 original profile/configuration files, 84/84 additional
PFT/SAIN files and 73/73 original DLL/PDB files by SHA-256. These manifests overlap;
their counts must not be summed as unique files. All temporary package-manifest
paths and the probe were absent. The four original Core/client files were restored,
and both GPU-2 and `D:\SPT41` again matched baseline Core `1.0.23+f092f98` before
synchronization was resumed. `restoration-verification.json` and
`restore-sync-result.json` record the checks and `paused=false` confirmation.
Off-sync rollback/evidence remains in `C:\Temp\RedeployDenied-20261113` on GPU-2.
That isolated test cycle made no global deployment, push, release or Arena
admission change. The subsequent user-authorized regular deployment of the same
Core binary is recorded in [repository status](../REPOSITORY-STATUS.md).

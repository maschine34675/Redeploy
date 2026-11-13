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

## Native verification status

Pending. Source/build and offline checks are not in-game acceptance. A test
installation was prepared on SPT-GPU-2 with synchronization paused and backups of
profiles, configuration and temporarily disabled mods. Another session started
that host during preparation; no raid test or candidate deployment was performed
before coordinating host availability. Native observations and restoration
evidence will be recorded here after the tests.

The paused test setup still needs coordinated completion and cleanup. Its
off-sync backup is `C:\Temp\RedeployDenied-20261113` on SPT-GPU-2: `disabled.json`
records displaced mods, `temporary-payload.json` records the full ISB/Aishi
packages, and the original profile/configuration manifests are stored beside
them. No candidate Core was copied there. Preserve changes made by the other
session when restoring; resume synchronization only after the disposable profile,
probe and temporary packages have been removed and the original layout restored.

The developer-only [probe](../../tools/DeniedModsNativeProbe/README.md) reads the
actual body references, shared ORBIT list identity, island membership/snapshots,
bot allies and ISB latch/history. It never repairs foreign state and is excluded
from release packages. Target cases are repeated PMC respawn, own-Scav continuation,
Lightkeeper-island exit/re-entry, enabled convergence and renewed ISB O queries
and notifications. Visible bot teleport/door recovery requires its own trigger;
a correct human list alone does not prove every AI recovery scenario.

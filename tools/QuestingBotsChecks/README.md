# Questing Bots body replacement checks

Run `powershell -NoProfile -ExecutionPolicy Bypass -File tools/QuestingBotsChecks/Verify.ps1`
from the Redeploy root. No project references or deploying targets are used.
The default release DLL is the ignored Questing Bots 1.2.0 audit download; use
`-PublishedDll <path>` to supply that same release elsewhere.

The harness links the production adapter, contract and Core ownership context.
Real Harmony patches run against bounded EFT/Questing fixtures. The native
compile separately binds production code against the installed EFT assemblies;
the release check validates SHA-256 and private metadata using Cecil. These are
offline checks, not evidence of in-game AI behavior.

The fixtures first reproduce the released stale same-ID ally and own-Scav null
ally hazards without preparation. Checks then cover read-only preparation,
refusal/cancellation, exact body and world authority, cleanup before successor
registration, preserved same-ID peers, snapshots, delayed corpse callbacks,
inside/outside assessment, second replacement, own Scav, failed creation and
explicit live old-body recovery, fresh raid and absent monitor. Older, drifted
and spoofed contracts refuse; newer matching contracts warn once. Three mutation
controls must fail for their intended assertion.

Retirement also fences provisional preparation and actual raid shutdown. Historical
destroyed/detached bot snapshot keys are tolerated without adopting their groups;
live foreign-world entries refuse. Verified recovery of the exact old living body
restores only its removed references in still-unchanged snapshot rows, preserving
pre-island hostility for the next native exit. For direct outside recovery, prior
membership triggers native exit; inside recovery triggers native entry including
new arrivals. Changed rows are never overwritten. Dead bodies retain no rollback
collections, and successful publication releases living-body rollback collections.

Source review: Questing release `LightkeeperIslandMonitor` owns its list and
snapshot dictionaries on the world GameObject; `BotGroupHelpers.FormAlliance`
resolves targets by ProfileId. EFT `GameWorld.UnregisterPlayer` precedes Core
creation; `BotsGroup.AddAlly(Player)` only appends the human reference, while
`RemoveAlly(BotOwner)` removes a bot group member and must not be used here.
The declared `ClientGameWorld.UnregisterPlayer` override is patched as well as
the base method: it skips its base call for an already-unregistered dead player.
No general quest-generator, bot-runtime or scene reset is involved.

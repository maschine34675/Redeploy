# Optional body replacement fixes, 8 November 2026

Core 1.0.21 handles four optional mods in standalone Redeploy and in ArenaMode
when those mods are loaded. All four integrations use the physical player and
world that Core is replacing. Admission caches each installed plugin binding.
Core keeps public API version 1.

| Reviewed mod | Handoff |
| --- | --- |
| [P.L.A.T.E. 1.6.0](https://github.com/Anamelash/spt-plate/tree/v1.6.0) | The blood ledger stores both a profile ID and a player object. Retire the outgoing player's entry and PLATE-owned injury state when the replacement is confirmed. A respawn using the same ID gets fresh blood state. Other combatants keep their entries. |
| [Loot In Vicinity 3.0.6](https://github.com/FallegaHQ/SPT-LootInVicinity/tree/3.0.6) | Stop the old panel from attaching to a retired inventory. The new player uses the current inventory controller. Closing a display-only row leaves the actual loot on the ground. |
| [Skills Extended 3.1.4](https://github.com/CJ-SPT/Skills-Extended/tree/V3.1.4) | Move the mod's existing XP subscriptions and player reference to the confirmed body. Shared PMC mastery subscriptions keep their order. Separate Scav skills and the replacement health controller receive the applicable handlers. Only an in-raid minigame owned by the outgoing player is aborted. |
| [Climbable Ladders 1.0.4](https://github.com/bmpq/spt-ladders/tree/v1.0.4) | After preparation succeeds, immediately finish any climb attached to the outgoing player's own GameObject. The mod cancels its approach, unsubscribes its input callback and releases its ladder overrides before a successor can start another climb. |

### Skills Extended issue 136

Skills Extended stores the raid-start player in a static field. Its mastery
handlers subscribe to `Profile.Skills`, which can outlive that player. After a
same-profile respawn a hit can reach the old player's cleared hands and throw
inside EFT's damage processing. Core guards the owned handoff, including the
empty-hands interval, while preserving the mod's normal XP calculations on the
new body. The original medical callback was bound to the first health controller
and needs to move even on a same-profile PMC respawn. The report includes an
affected Arena run and a separate explanation of the standalone path.
[Upstream issue 136](https://github.com/CJ-SPT/Skills-Extended/issues/136)

### Inventory boundary

Loot In Vicinity's virtual grid serves two purposes. It can list an item that
still belongs to a world loot pile, or temporarily own a real item the player
moved into it. Core verifies item ownership before asking the mod to close the
old display. A staged real item, an unfinished take or an ambiguous transfer
causes a refusal before Core tears down the old player. Standalone Redeploy's
existing failure path can then end the raid. Complete or cancel the transfer
before a planned living kit swap. The adapter never forces a world drop, clears
the raid stash or moves a staged item into a successor's inventory.

### Verification

The focused runners in [tools/CaseBodyChecks](../../tools/CaseBodyChecks) exercise
the production adapters against published DLL metadata and model the affected
callbacks. The ladder check verifies that native `OnDestroy` in the reviewed
DLL cancels the transition, unregisters input and releases its movement lock.
The composition runner verifies admission, preparation order and cancellation.

The four focused runners passed offline. They cover same-profile replacement,
the own-Scav handoff where applicable, cancelled requests, recovery, and
callbacks from the old body. Skills Extended also covers the initial Arena kit
swap and empty hands. Loot In Vicinity covers a published replacement whose
input owner becomes ready later. P.L.A.T.E. covers a failed blood-record
initialization and restoration of the prior entries.

Core, Redeploy.Client and ArenaMode.Client compiled against the game assemblies.
All 613 Core/client tests, 47 interop checks and the 20 body-composition cases
passed. The composition runner detected all ten deliberate regressions.
These results cover the source and offline fixtures. In-game verification of
these four combinations is pending.

The reported loss of movement, HUD and weapon control is a separate incident.
The affected user's log and an in-game reproduction are still needed to establish
its cause. Relevant details are the first exception around the death, the loaded
Core version, the PMC or Scav choice and whether `Respawn complete.` was logged.

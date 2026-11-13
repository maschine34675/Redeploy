# Player Encumbrance Bar body binding

Reviewed release: Player Encumbrance Bar 1.3.0, plugin GUID
`com.mpstark.PlayerEncumbranceBar`, assembly `PlayerEncumbranceBar`.
The Forge page currently lists `com.mpstark.pec`; admission must use the actual DLL identity.
Release DLL SHA-256: `BCFC8ED1F5C7FE89F926402048728FC62BF8E3A32090A021CB47CD51A0A98C0B`.

The released component stores its displayed health controller privately and statically.
Its `Show` binds weight and strength events through `GameUtils.PlayerProfile`, which is
the backend PMC. Reading weight itself already follows `LocalGame.PlayerOwner.Player`.
A replacement carrying a different physical profile can therefore have a correct initial
bar but subscribe to another inventory. An own-Scav continuation is one such case.

Core 1.0.23 changes only the foreign getter while its displayed health belongs to the
exact current Core-owned local body, current game/world/profile and player owner. The
foreign `Show` still creates and owns all subscriptions. Once read-only preparation has
passed, Core closes only a bar bound to the outgoing health controller through its own
UI disposables, then clears that private health reference. If a later preparation step
fails, the same surviving physical player's display is restored through the foreign
`Show`, including its prior visibility and Core ownership. Rollback evidence is retained
before disposal starts, so a partial foreign disposal can also be recovered. A newer
display epoch, bar, health controller, owner or world prevents rollback from overwriting
that new state. Visibility and active native subscriptions are captured independently:
a hidden but subscribed bar regains its subscriptions and remains hidden; a closed,
unsubscribed bar stays closed. Successful replacement forgets
the rollback without reopening its old UI. World retirement closes only the still-owned binding. Weak UI
tombstones fence late callbacks without keeping worlds, players or health controllers
alive and are cleared by the next deliberate `Show`.

The adapter has no per-frame work, object searches or global profile changes. Its absent
path returns before reflection or Harmony cleanup. It caches successful and failed
contracts by loaded identity/version. Newer versions are advisory only when the private
API still matches. No foreign code or DLL is redistributed.

Arena separately substitutes the displayed limits with its exact local body's physical
limits before the bar lays out its ticks. Core does not know Arena's movement settings.

Offline checks: `dotnet run --project tools/PlayerEncumbranceBarChecks -c Release` uses
real Harmony detours on a fixture carrying the released private API. It checks native
pre-replacement behavior, rejected preparation, outgoing disposal, new-profile event
rebinding, same-profile unrelated owners, retirement, late callbacks and menu return.
The rollback cases also cover a downstream preparation failure, partial disposal,
foreign restoration failure, hidden panels, replaced displays/worlds/owners, cancellation
after an earlier successful swap, and a successful commit without an extra `Show`.
The final fixture passes 128 checks (including 100 repeated cached-admission calls).
`Verify.ps1` rejects eight compiled runtime mutants and then reruns that baseline.
Native acceptance and the tested loaded builds are recorded in ArenaMode's four-mod
verification report once completed; a fixture is not proof of native compatibility.

Sources: [Forge](https://sp-mod.com/mod/1374/player-encumbrance-bar),
[release](https://github.com/Lacyway/SPT-PlayerEncumbranceBar/releases/tag/v1.3.0).
The published DLL is `1.3.0+57e37dac-dirty`; its IL was reviewed because the tagged source
still contains older obfuscated health-type names.

# FOV Fix and Manimal body replacement — 2026-10-25

Core 1.0.11 adds optional integrations for Fontaine FOV Fix 4.1.0 and Manimal
Ammo Loading Animations 2.0.0. These are Core responsibilities shared by
standalone Redeploy and ArenaMode. No foreign assemblies are linked or bundled.
The public `RespawnApi` remains version 1.

## Reproduced problems

On SPT-GPU-1, Factory daytime, Arena 0.2.1 (`7119f1f`) with Core 1.0.10:

- FOV Fix retained its original physical player. At an ADS multiplier of 0.7,
  aiming before death produced FOV 35; aiming with the replacement produced 50.
  The replacement's `IsAiming` was true while the cached old body's watcher was
  false. Default multiplier 1 can conceal this fault.
- Manimal's static `_sessions` dictionary retained old players and magazines.
  Actual packing of detached STANAG magazines across two bodies left two entries
  after respawn and raid exit, even after both Unity objects were destroyed.
  Successful packing on the new body does not disprove this managed retention.

## Ownership and admission

The integrations resolve optional plugin identities and required members lazily
at Core admission. Unsupported contracts refuse a Core replacement before the old
body is torn down; they do not disable the foreign mod in ordinary gameplay.
Newer versions require the same structural contracts and produce a warning,
rather than passing an exact-version ceiling.

Preparation is provisional and can be cancelled without changing the foreign
state. Authority is tied to exact physical bodies and their game/world, never
just a profile ID. Ordinary players, bots, hideout activity and absent mods do
not grant replacement authority. Old-world teardown cannot clear newer state.

FOV handover refreshes the controller's body, weapon and aim watchers together;
retired-body PWA/camera callbacks cannot overwrite the replacement's camera.
Manimal cleanup removes retired owned sessions and fences delayed animation
work, including iterators that began on the old body. The existing Continuous
Load Ammo integration remains responsible for CLA's own loading controller.

Install the tiny iterator-factory hooks **before their callers**. The first native
candidate cleaned sessions correctly but suppressed animation on owned bodies:
packing still advanced, while the hands stayed empty. Reordering the hooks
restored the custom controller and factory stamps without admitting unknown
old-generation iterators. This is consistent with a Mono inlining window when
the caller is patched/JIT-compiled before the factory; the test proves the order
fix, not a disassembly of Mono's generated code. Bounded Debug counters make a
future missed factory hook inspectable.

## Verification boundaries

The shared lifecycle harness covers each optional family on its own, absent
mods, refused admission, cancelled preparation, candidate identity and old-world
teardown. It also runs deliberate negative mutations of the protection rules.
The new pure scope tests exercise ownership and retirement independently of
Unity. These checks establish code behavior, not visual gameplay acceptance.

Release compilation of Core and its Redeploy driver passed with no warnings or
errors; all **237** Redeploy tests passed. The shared lifecycle harness passed
**19 cases / 389 assertions**, and all **9** deliberate negative mutations were
detected. Arena's policy suite passed **2,861** tests with one known APBS skip.

The final runtime candidate is Core **1.0.11+`a061c78`**, SHA-256
`73016DBEC5CC1C45F7AFDDAE2FA2B95CAFE457D1D2F2F8C8BE99BDA40EEFFD73`.
GPU-1 keeps Arena client code at `7119f1f` during these checks, isolating it from
concurrent development in the main checkout. The first Core candidate
`859d694` is **not** the accepted Manimal build: its visual regression was caught
by comparing an ordinary body's sustained STANAG animation with a replacement
body in the same process.

On the final candidate, two actual Arena deaths produced distinct replacement
bodies. FOV remained 35 with ADS multiplier 0.7, and FOV Scale 0.85 survived the
second death. Supported STANAG packing visibly animated on both Core-owned
bodies. Death during an active custom animation retired its session; the next
body could aim and use its firearm. A second pack completed 30/30, restored the
firearm controller and allowed two real RD-704 shots. Five factory stamps and
zero unknown owned iterators were observed. Menu return left zero Manimal
sessions and no FOV cached player, with no warning/error during that raid.

The ordinary PMC/standalone Redeploy control established same-process ordinary
FOV rebinding and an ordinary-body animation positive control, then reproduced
the first candidate's replacement-body animation defect. Final visual acceptance
uses Arena's shared Core replacement path after the order correction; it does
not claim a second final standalone run.

PitFireTeam, every optic, all delayed-operation races, Fika and future foreign
versions are not covered by these bounded checks. A normal PMC/Redeploy control
has Arena installed but inactive; it is not a separate installation with the
Arena DLL physically removed.

The corresponding Arena report is `docs/FOUR-MOD-COMPAT-20261025.md` in ArenaMode.
It also records why Tarkova-Cola and Fast Grenade Throw were not admitted; their
profile-reward and grenade-selection conflicts do not belong in Core.

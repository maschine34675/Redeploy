# Redeploy 1.1.3

For **SPT 4.1.x**. Includes Client/Server **1.1.3** and shared **Core 1.0.24**;
no additional mod is required. The Client and Server code is unchanged since
1.1.1: this release carries the new Core and the drivers' Core 1.0.24 stamp.
Core 1.0.21, 1.0.22 and 1.0.23 were never released on their own; this is the
first release that carries their changes.

## Changed

- **Three AI-mod integrations (Core 1.0.24).** ORBIT 3.1.1 keeps its original
  shared human-player list and substitutes the exact committed replacement
  body, so movement safety and convergence can use the new PMC or own Scav.
  Questing Bots 1.2.0 removes the retiring body's Lightkeeper-island membership
  before its successor registers, which prevents stale profile-ID aliasing and
  null allies. ISB SOF Notifier 2.0.4 reopens local presentation after a
  committed respawn and keeps settings, counters and announcement history.
- **DERP and Player Encumbrance Bar (Core 1.0.23).** Dynamic External Resolution
  Patch 1.1.2 follows the replacement player instead of the retained outgoing
  body; retired optic callbacks cannot reset the new camera and empty
  replacement hands no longer reach its unsafe scope access. Player Encumbrance
  Bar 1.3.0 follows the current body's health, inventory and skills, including
  an own-Scav continuation, and detaches the old UI event subscriptions.
- **Amand's Sense Updated 3.1.0 (Core 1.0.22)** can keep an active loot marker
  through a body change; the new player keeps using the same raid's markers and
  corpse history, and a failed handoff returns Sense to the old player.
- **Four more body handoffs (Core 1.0.21).** P.L.A.T.E. 1.6.0 keeps blood,
  injury and movement state with the physical life that incurred it. Loot In
  Vicinity 3.0.6 rebinds to the current inventory; a real item staged in its
  virtual stash or an unfinished take can prevent the replacement before
  teardown. Skills Extended 3.1.4 rebinds its weapon mastery and medical XP
  handlers and cancels a hacking or lockpicking attempt of the outgoing player.
  Climbable Ladders 1.0.4 finishes a dismount on the outgoing character first.
- **Public API and documentation.** `RespawnApi` is unchanged (version 1). The
  interop file is now revision 1 and behaves as before; earlier copies keep
  working. New English consumer documentation (API reference, soft dependency,
  rules, recipes) with a compiled example plugin, and `docs/` was reorganised
  into subfolders.
- Drivers built from this source (Redeploy.Client 1.1.3, ArenaMode 0.6.0) stamp
  and require Core 1.0.24 and refuse an older Core at startup.

## Installation and updating

Finish any raid and pending Scav recovery or transfer. Close the game and server,
back up your profile, and extract `Redeploy-1.1.3.zip` into the SPT installation.
Update all three files, then restart both:

- `BepInEx/plugins/maschine-Redeploy.Client.dll`
- `BepInEx/plugins/maschine-Redeploy.Core.dll`
- `SPT_Runtime/user/mods/Redeploy/maschine-Redeploy.Server.dll`

Keep exactly one Core DLL across the plugins folder and its subfolders. Redeploy
1.1.3 requires **Core 1.0.24 or newer** with a compatible API. ArenaMode 0.6.0
ships the identical Core 1.0.24; with ArenaMode 0.5.0 or older, keep the Core
from this package. The `RespawnApi` contract remains version 1. Existing
settings are kept.

## Limits and validation

The integrations are optional and detected at runtime. The Core 1.0.24 adapters
were run in the game on the GPU test host SPT-GPU-2 on 13 November 2026, one mod
at a time in standalone Redeploy: a PMC respawn and the own-Scav continuation for
ORBIT, Questing Bots and ISB SOF Notifier. A forced replacement failure and its
recovery are covered by offline harnesses only, and Questing Bots' case of a bot
arriving after the switch to Scav was not exercised natively. Aishi's
anomaly-reveal feature reports "The active profile changed. Try again." after an
own-Scav continuation; it reads the current player's profile while its server
routes return the PMC profile.

DERP and Player Encumbrance Bar were run in the game on SPT-GPU-1 on 13 November
2026, in an Arena match with an actual death and in standalone Redeploy with the
own-Scav continuation. DERP's friendly-marker alignment, a renderer-menu reset and
retired-optic callback races, and the encumbrance bar's cancellation,
hidden-panel and late-callback cases are covered offline only. Amand's Sense ran
in the game once, in a Factory Arena Deathmatch with two deaths; standalone
Redeploy and continuing as Scav were not exercised with it. P.L.A.T.E., Loot In
Vicinity, Skills Extended and Climbable Ladders are validated offline against the
published mod builds; their in-game verification is recorded as pending.

Fika remains unsupported. All other limits of
[1.1.2](https://github.com/maschine34675/Redeploy/blob/v1.1.3/docs/releases/RELEASE-1.1.2.md#limits-and-validation)
still apply. The
[changelog](https://github.com/maschine34675/Redeploy/blob/v1.1.3/docs/CHANGELOG.md#113---2026-11-14)
records the complete changes.

## Native release check

Pending: filled in after packaging and the native run.

## Package checks

Pending: filled in after packaging and the native run.

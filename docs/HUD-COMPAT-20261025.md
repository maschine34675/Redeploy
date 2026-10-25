# Radar and crosshair body replacement — Core 1.0.13

Two optional HUD integrations now share Core's physical-body lifecycle. They
are available to standalone Redeploy and ArenaMode without either consumer
carrying its own foreign HUD patches. Public RespawnApi version 1 and the
replacement event sequence are unchanged.

## Reviewed contracts

- [Accurate Circular Radar 1.3.4](https://github.com/Leonana69/Tyrian-Radar-Standalone/releases/tag/1.3.4),
  GUID `com.leonana69.radar`, source `8b58dde7b681b37c7dad70153f7b212c64d7da0a`.
- [Simple Crosshair 1.4.0](https://github.com/DrakiaXYZ/SPT-SimpleCrosshair/releases/tag/1.4.0),
  GUID `com.mpstark.SimpleCrosshair`, source `4c5ee788d6b31cd3ac45dc44b589a576b7472382`.

The actual released DLLs were decompiled and compared with these source tags.
Core checks plugin/assembly identities, minimum versions, required member
shapes and the foreign hooks before teardown. Newer matching versions warn as
unreviewed. Missing/incompatible contracts refuse replacement before removing
the old player. Neither foreign mod is bundled or required.

## Radar

Radar places its HUD under the FPS camera or compass glass and caches the
player/camera in several trackers. Core normally destroys those presentation
objects when replacing the player. The adapter temporarily reparents the
existing HUD under its exact GameWorld, preserving its coroutines, price cache
and anonymous loot-container subscriptions. It then rebinds the local player,
loot/extraction trackers, layout and camera when the new native camera owns the
current physical body. It does not replay the mod's Awake or GameWorld startup.

Only an adopted manager/HUD gets the guard against Radar's missing-HUD branch
destroying its containing GameWorld. Late callbacks from retired owned HUDs
cannot operate on a newer world's static radar. Settings changed during the
handover are applied afterward without overwriting the configuration.

Within the adopted world, contact reconciliation compares physical bot objects
even when their profile IDs and total count stay unchanged. Destroyed contacts
are removed; same-ID reconstructed bots get fresh contacts. This is bookkeeping
repair, not team detection: Radar colors still represent faction/role. Hidden
corpses that remain physical objects retain upstream corpse-display behavior.

## Simple Crosshair

The upstream unregister hook removes the current crosshair for any old local
player, even when native UnregisterPlayer already returned without doing
anything. Its cleanup also uses the current player instead of the cached one
and adds an interaction callback instead of removing it.

Core keeps the adopted overlay during replacement, detaches subscriptions from
their exact old body/controllers, temporarily suspends its updates and binds
it after the new body and FPS camera agree. Late old-body unregister/look
callbacks cannot remove or modify the successor's crosshair. Static and dynamic
positioning share the same corrected camera ownership. A repeated battle-screen
show cannot double-subscribe the preserved component.

Preparation remains reversible. Ordinary bodies/HUDs pass through; merely
installing Core does not take ownership of a raid. Weak retired-object records
protect late callbacks without retaining every historical body indefinitely.

## Offline verification

Core and Redeploy.Client Release builds succeeded without deployment, warnings
or errors. All 274 unit tests passed, including 10 Radar scope cases and 14
Crosshair cases. They cover exact identities, same-ID impostors, cancellation,
failed construction/recovery, atomic adoption and old-world callbacks. These
pure tests do not execute Unity or foreign Harmony methods.

The production-linked coordinator harness passed 19 scenarios / 458 assertions
and rejected all nine compiled mutants. It checks absent-mod inactivity,
admission/preparation fan-out, cancellation and retirement for both new families
as well as the existing adapters. Native reflection and display behavior are
separate acceptance evidence.

## Native baseline

SPT-GPU-2 with Core 1.0.12 reproduced both defects on Arena Deathmatch's initial
living kit swap: after the new player was active, there were zero HaloRadar and
zero SimpleCrosshairComponent instances. A screenshot independently confirmed
both overlays were absent. The unsafe Radar enable toggle was not exercised.

Candidate native acceptance is recorded after the completed test run; this
baseline alone is not evidence that Core 1.0.13 fixes the released packages.

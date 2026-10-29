# Redeploy 1.1.0

For **SPT 4.1.x**. Includes Client/Server **1.1.0** and shared **Core 1.0.17**;
no additional mod is required.

## What's new

- **Continue as your own Scav after PMC death.** Choose the usual naked PMC
  respawn or one life as your regular Scav when its cooldown permits. Your PMC
  remains dead and the corpse stays lootable. Successful Scav extraction opens
  the normal loot transfer screen. The option is enabled by default and can be
  disabled in F12; existing preferences are preserved.
- **Recover interrupted Scav continuations.** The main menu can abandon an
  unfinished Scav life, complete a recorded save or resume remaining loot
  transfer. Items already moved to the stash stay there. Follow the recovery
  screen's restart instruction before starting a new raid.
- **More optional mods follow the replacement body.** Added integrations for
  ImpactCue, Enemy Markers, Quick Cancel, Picture in Picture Disabler, Amand's
  Graphics, AI Limit, Healing Autocancel, Quest Tracker and Audio Accessibility
  Indicators. These live in Core and work without ArenaMode. See the
  [compatibility list](https://github.com/maschine34675/Redeploy/blob/v1.1.0/README.md#requirements-and-compatibility) for versions
  and limits.
- **Clearer settings and quieter logs.** F12 labels are easier to read, detailed
  timing remains an advanced opt-in, and routine respawn messages use Debug.
  Safety decisions and real failures remain visible.
- Preserve extraction time across replacements and use native death-callback
  ordering for the replacement character.

## Installation and updating

Finish any raid and pending Scav recovery or transfer. Close the game and server,
back up your profile, and extract `Redeploy-1.1.0.zip` into the SPT installation.
Update all three files, then restart both:

- `BepInEx/plugins/maschine-Redeploy.Client.dll`
- `BepInEx/plugins/maschine-Redeploy.Core.dll`
- `SPT_Runtime/user/mods/Redeploy/maschine-Redeploy.Server.dll`

Keep exactly one Core DLL across the plugins folder and its subfolders. Redeploy
1.1.0 requires **Core 1.0.17 or newer** with a compatible API. If ArenaMode already
supplies a newer compatible Core, retain it. Core has its own version line;
the `RespawnApi` contract remains version 1. Existing Redeploy settings are kept.
See [updating](https://github.com/maschine34675/Redeploy/blob/v1.1.0/README.md#updating) for obsolete CorpseRun files.

## Limits and validation

Own-Scav continuation requires native Scav spawns/exits and an available Scav.
Incoming map transitions are unsupported; prior BTR/transit deliveries disable
the choice. After switching, BTR item delivery and the transfer screen's Sell All
are unavailable. Move wanted items into the stash and finish the transfer.
Ordinary PMC/Scav delivery and ordinary Scav Sell All retain their behavior.
Unproven partial saves remain blocked for diagnosis; keep their journals.

Fika remains unsupported. Amand's Graphics still has its initial Labyrinth scene
lookup limitation. Newer optional-mod versions require the expected APIs.

The [29 October native checks](https://github.com/maschine34675/Redeploy/blob/v1.1.0/docs/RELEASE-CHECK-20261029.md) cover ordinary respawn,
corpse-weapon recovery and firing, give-up, own-Scav extraction and partial loot
transfer. [Recovery tests](https://github.com/maschine34675/Redeploy/blob/v1.1.0/docs/PMC-SCAV-RECOVERY-20261028.md) also cover interrupted
continuations. These used the recorded **pre-1.1.0 binaries**, so they establish
those gameplay scenarios, not native acceptance of newly built 1.1.0 hashes or
every map, optic and optional-mod combination. The
[changelog](https://github.com/maschine34675/Redeploy/blob/v1.1.0/docs/CHANGELOG.md#110---2026-10-29) records the complete release changes.

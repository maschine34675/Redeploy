# ImpactCue and Enemy Markers body replacement

Core 1.0.14 adds optional adapters for ImpactCue 2.0.3 and Enemy Markers 1.4.0.
Standalone Redeploy and Arena share these fixes. Client/Server stay at 1.0.2;
this is development work after the published Core 1.0.13 package.

Both adapters validate the released plugin identity, version, reflection
members and required hooks before admitting replacement. Preparation alone
grants no mutation authority: the exact physical old body must reach Core's
native unregister path. The concrete ClientGameWorld override is covered even
when death has already unregistered the corpse. Inherited MethodInfo values
are normalized to their declaring type before Harmony installs a patch.

ImpactCue preserves raid history and settings while rebinding live telemetry.
Old-body callbacks cannot replace its current-player cache. Enemy Markers
preserves bot marker objects and their configuration, changes the billboard's
camera to the current FPS camera, and excludes the replacement local body.
Retired worlds/callbacks cannot affect the next owned world. Ordinary raids
outside Core ownership keep the foreign mods' existing behavior.

Native tests exposed another Enemy Markers issue: OnDestroy reads a destroyed
player's name both in its try and catch, and unsubscribes the wrong death event.
An owned-component-only prefix now removes its actual interface subscription
and marker rows first. The original world/canvas/coroutine/logger cleanup still
runs. Tests verify this tail and retention of unrelated subscribers; the
ordinary unowned failing fixture is deliberately unchanged.

## Verification

- Core/Client Release build: no warnings or errors.
- Redeploy unit suite: 283 passed.
- ImpactCue harness: 81 assertions against production adapter/contracts.
- Enemy Markers harness: 64 assertions and 5 scope tests, including destroyed
  player/marker teardown and a delayed old-world destroy.
- Shared coordinator harness: 19 scenarios, 504 assertions and 9 mutations.
- Native SPT-GPU-2: two standalone respawns with Arena absent and two Arena
  automatic respawns. Physical body/camera identities changed, retained old
  bodies did not steal HUD bindings, new-body health/ammo changed correctly and
  persistent enemy markers followed the new camera. Screenshots were inspected.
- Final Core additionally passed one more respawn and full raid exit per driver:
  no Enemy Markers teardown exception, and both returned to the main menu.
  Temporary mods/probe/configs were removed afterward; test profile and sync
  settings were restored, preserving pre-test settled rewards. GPU2 is stopped.

The temporary `tools/HudBodyNativeProbe` only reads identities and snapshots.
It does not implement the fix or refresh telemetry. It must never be shipped.
The accompanying Use Items Anywhere fix belongs to Arena and is not duplicated
in Core. See Arena's `docs/ITEMS-HUD-COMPAT-20261025.md` for inventory tests and
the complete final cleanup/exit retest record.

ImpactCue's existing startup warning for its optional EftBattleUIScreen.Show
hook remains; fallback HUD telemetry worked. Simultaneously enabled HUDs can
overlap. Compatibility does not promise every foreign option or game mode.

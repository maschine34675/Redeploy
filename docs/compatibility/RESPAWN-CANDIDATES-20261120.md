# Respawn compatibility review — 20 November 2026

Source and official-package audit against Redeploy.Core 1.0.25, repository
`32336f4`. This records recommendations, not implemented adapters or native
acceptance. No game was started, no mod was installed or updated, and neither
Arena's whitelist nor its ORBIT exclusion was changed.

## Reviewed versions and outcome

| Mod | Reviewed version/source | Result |
| --- | --- | --- |
| Satellite | Client v1.0.2, `e35abf1bbd1c452ae40eaf405f9379b1c1d136d9` | Current-body tracking exists; refresh side/infiltration-dependent extracts when continuing as the own Scav. |
| Gravity | v1.1.0, `8733d2f433e0f1bdc21287a59d5011c82f6df7dc` | No additional body-replacement fix identified. Bot-population ownership in Arena is a separate question. |
| GamePanelHUD | v4.0.1, `19658d6d883644994a72e0a3162ee8acf07dd62f` | Ordinary HUD reads follow the new body. Same-world profile changes need compass/extract/quest and profile-specific counter handling. |
| KmyTarkovApi | 2.0.0 bundled with that HUD release; source `be145d9dcdad4b2521c30e6c340e3e9f97266b9c` | Player rebinding already exists. A separate owner-hook gap merits correction/validation, but does not establish a HUD respawn failure. |
| Advanced Movement | 1.0.1, `4cba9415045195f919ceef10ebbd1b2e0d98b38c` | Add explicit ownership/reset handling for an active move during a live-body replacement; ordinary death already has cleanup. |
| ORBIT | 3.2.1, `c03acffb67f1640e52de45e06cbe6d89c57f8528` | Existing Core adapter still matches the reviewed private contract; no new respawn fix identified. |
| DynamicMaps | 1.3.0, `3592b71ea6341a0e0bfae5efc0456ffc7d165e22` | Existing arrow fix still matches; extend it to the new per-body quest and inventory subscriptions. |

Satellite's newer map-addon release is not a newer client. KmyTarkovApi has no
standalone GitHub release; its packaged DLL and current source were considered
separately. Gravity's release DLL stamps build commit
`f5a28c33828d9b893e3d23d34971a527121e7546`; the client/shared/server C# is unchanged
between that commit and its release tag. References and downloaded/decompiled
evidence remain in the ignored `artifacts/respawn-audit-20261120/` directory.

## DynamicMaps: extend the existing fix

[Release 1.3.0](https://github.com/acidphantasm/SPT-DynamicMaps/releases/tag/1.3.0)
adds live quest and key-dependent door updates. The existing
`DynamicMapsCompatHelper.RebindPlayerMarker` still finds `Plugin.Instance`,
`Plugin.Map`, `_dynamicMarkerProviders`, `_playerMarker` and the marker's `Player`
property. That property became private, but Core already uses non-public-aware
reflection. The new main/mini-map controllers receive the current player each
update, so they do not require a second arrow or transform adapter.

The new gap is in two providers:

- [QuestMarkerProvider](https://github.com/acidphantasm/SPT-DynamicMaps/blob/3592b71ea6341a0e0bfae5efc0456ffc7d165e22/Plugin/DynamicMarkers/QuestMarkerProvider.cs#L13)
  stores the player's quest controller and subscribes to its quest events.
- [LockedDoorMarkerMutator](https://github.com/acidphantasm/SPT-DynamicMaps/blob/3592b71ea6341a0e0bfae5efc0456ffc7d165e22/Plugin/DynamicMarkers/LockedDoorMarkerMutator.cs#L22)
  stores the inventory controller and equipment, and subscribes to item changes.

These bindings refresh on provider show/map-change and clear on hide/end/disable,
not on physical-body change. EFT creates new inventory and quest controllers for
each `LocalPlayer`. Core deliberately retires the old owner without closing
BattleUI; a continuously visible minimap therefore has no guaranteed hide/show
transition. The existing fix only changes the arrow's player. Quest updates and
key colours can remain bound to the retired body until the display is rebuilt.
Showing BattleUI again does not guarantee repair: `TryAddPeekComponent` returns
when its existing component survives, without hiding/showing the providers.

Recommended: at confirmed replacement, detach the old subscriptions and refresh
only those active providers against the successor. Unsubscribe before checking
the new side: the quest provider returns early for Scavs. Preserve same-world map
data; do not replay `GameWorld.OnGameStarted`, run a scene scan, or call the full
raid-end path merely to refresh subscriptions.

The official client DLL matches the currently installed DLL, SHA256
`87878B34A7BB4D50DCF05DAA6CD9A93A27543B0C26AA38FA213C40053A6DB19B`.
The decisive provider and marker members were also checked in the release IL.
This is a source-confirmed missing lifecycle path, not an observed native failure.

## Advanced Movement: retire active moves on body change

The [1.0.1 source](https://github.com/SageSPT/Advanced-Movement/tree/4cba9415045195f919ceef10ebbd1b2e0d98b38c)
already fetches `GameWorld.MainPlayer` for input. AirSlide checks physical identity,
and animation/audio helpers can bind lazily to another body. A blanket restart
of the mod after every death is not justified.

However, [`MoveController`](https://github.com/SageSPT/Advanced-Movement/blob/4cba9415045195f919ceef10ebbd1b2e0d98b38c/Client/Moves/MoveController.cs#L204-L228)
keeps a static player plus slide/dive state. Its `Update` cleanup checks whether
that old player is missing, has no health
controller, or is dead; it does not require that it still be the current player.
Global `Busy` can consequently describe an active move belonging to a different,
still-living body. This matters particularly for Arena wave/round replacements
of a surviving player. Normal lethal respawn usually takes the existing dead-body
cleanup path, so a universal inability to move after death is not established.
The duration of the live-swap risk depends on when the old body is disposed and
the old move finishes. This recommends bounded mitigation and a focused probe;
it is not an observed permanent movement lock.

Recommended: explicitly retire the old body's movement, gesture, animation and
weapon state at the committed handoff, using exact ownership. Do not transfer an
in-progress dive to the new controller. A native test must cover death and a
live-body swap during both moves, plus idle swaps. Conflicts between Arena's
movement features and this mod are a separate gameplay-integration review.

## Satellite: refresh extracts for own-Scav continuation

[World.Scan](https://github.com/MrFums/Satellite/blob/e35abf1bbd1c452ae40eaf405f9379b1c1d136d9/Satellite.Client/World.cs#L343)
reads the current MainPlayer and alive-player collection. Ordinary replacement
therefore does not require replacing a cached global player.

Extract discovery is different: `_exitsLooked` resets when the GameWorld changes,
while [`LookForExits`](https://github.com/MrFums/Satellite/blob/e35abf1bbd1c452ae40eaf405f9379b1c1d136d9/Satellite.Client/World.cs#L1001-L1022)
selects PMC/Scav points and infiltration eligibility from
the main player only once. Own-Scav continuation keeps the world but changes the
profile/side. The displayed exits can therefore stay on the PMC selection.

Recommended: invalidate and rebuild only the local player's extract/transit
selection on the relevant identity change, retaining existing world/player
history. Do not reset the complete live map. Native verification should compare
PMC and own-Scav exit lists in one raid.

## GamePanelHUD and KmyTarkovApi: distinguish body and profile state

The [HUD release](https://github.com/dustinmoon78/GamePanelHUD/releases/tag/v4.0.1)
declares SPT 4.1.6 and bundles KmyTarkovApi 2.0.0. Its seven actual HUD plugin
versions are still 4.0.0, under `com.dustinmoon78.gamepanelhud*`; the package's
4.0.1 label is not a plugin version. The current HUD branch is two commits ahead
of the release, but only build/documentation files differ. Kmy's
[PlayerHelper.OnInit](https://github.com/dustinmoon78/KmyTarkovApi/blob/be145d9dcdad4b2521c30e6c340e3e9f97266b9c/KmyTarkovApi/Helpers/PlayerHelper.cs#L117)
awaits `Player.Init` and updates its singleton for the local player. Core's normal
creation path runs that method. Health, inventory, firearm and quest getters
derive from that player. HUD damage/kill/direction hooks are global patches which
check the current local player, not old-body event subscriptions. No general
health/weapon HUD reinitialization is indicated by this audit.

The HUD's
[static compass initialization](https://github.com/dustinmoon78/GamePanelHUD/blob/19658d6d883644994a72e0a3162ee8acf07dd62f/GamePanelHUDCompass/Controllers/CompassStaticHUDController.cs#L176)
builds quest and extract markers at world start, with side-specific exit selection.
Same-world own-Scav continuation does not replay that event. Those markers need
a targeted profile-context refresh. The HUD kill counter clears when `HasPlayer`
becomes false; a handoff that never exposes that state may retain the previous
profile's display count. Define/reset that profile-specific display at the same
boundary, without clearing normal PMC kills on every physical respawn.
VisitAPI markers likewise lack deterministic profile-change invalidation; mark
only the relevant data dirty. Retire the old marker generation before rebuilding
its IDs, because deferred deletion must not remove newly created replacements.

There is also a separate Kmy owner-hook issue:
[GamePlayerOwnerPatchs](https://github.com/dustinmoon78/KmyTarkovApi/blob/be145d9dcdad4b2521c30e6c340e3e9f97266b9c/KmyTarkovApi/Patches/GamePlayerOwnerPatchs.cs#L13)
targets base `GamePlayerOwner.Init` and `HideoutPlayerOwner.Init`. The game's
`EftGamePlayerOwner.Init` override does not call the base method. Therefore a
current raid-owner cache cannot be inferred from those hooks. The reviewed HUD
does not consume that API property, so this is not evidence of a HUD movement or
input lock. Correct/validate the owner hook for other consumers before treating
it as a general guarantee; avoid an unnecessary Core player-cache adapter.

## ORBIT: existing adapter remains applicable

[3.2.1](https://github.com/Chazut/ORBIT/releases/tag/3.2.1) was compared with the
reviewed 3.1.1 source and release DLL. All eight fields required by `OrbitContract`
retain their exact types and ownership shape, including the shared human list
used by movement, waypoints and stuck recovery. The selected 23 human-cache
methods have identical normalized IL across the two release DLLs.

The assembly version now has a fixed revision, which the current contract
accepts: its metadata comparison already checks major/minor/build, and the
minimum version is 3.1.1. A newer-reviewed-version warning is expected; version
3.2.1 alone does not trigger rejection. Client SHA256:
`9750F678C90D115B900BA38A19766BFB50CD5BEE8A563531815E962D3B5D076E`.

No additional respawn implementation is justified by this update. Repeat the
standalone native convergence test with the exact package before updating its
reviewed-version claim. The existing Arena exclusion remains independent of
this body-replacement assessment.

## Gravity: no additional body fix identified

[v1.1.0](https://github.com/danyhappy564-cmyk/Gravity/releases/tag/v1.1.0) reads the
current main player/alive-player population in its runtime directors and spawn
decisions. The reviewed paths do not rely on a one-time local-body binding that
Core must replace. This is a negative source finding, not native acceptance.

Its role as a bot spawner/director may overlap with Arena's ownership of match
population. That separate concern must be resolved before any Arena whitelist
decision, even if ordinary Redeploy respawns need no special adapter.

## Suggested implementation and verification order

1. Extend DynamicMaps' existing Core integration for quest/inventory subscriptions.
2. Add bounded Advanced Movement active-state retirement for live-body swaps.
3. Refresh Satellite and HUD profile-specific map/compass state for own-Scav.
4. Check Kmy's actual raid-owner hook with a focused consumer probe; avoid
   presenting it as an established failure of the reviewed HUD.
5. Run focused native regression checks for ORBIT 3.2.1 and Gravity; update
   reviewed versions only with explicit evidence and preserve Arena admission rules.

Future integrations should use the public body-change events where possible.
Any necessary private-state repair belongs in Redeploy.Core, with no API-v1 or
engine-order change, no global searches or patch walks per frame, and exact
old/new body ownership. Do not rebuild entire mods or world caches per respawn.

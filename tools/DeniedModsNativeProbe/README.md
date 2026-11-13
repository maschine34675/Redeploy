# Denied-mod native observation probe

Developer-only; never part of a release. Build without deployment:

```powershell
dotnet build tools/DeniedModsNativeProbe/DeniedModsNativeProbe.csproj -c Release -p:SPTModType=Library
```

Install its DLL only in an isolated test setup with a disposable profile. The
`com.maschine.DeniedModsNativeProbe/observe` UiBridge action reads native private
state and exact physical-body identities. `remember=true` stores a probe reference
for comparison with a later body; it does not modify the mods or the game.

`islandPoints` reports grounded NavMesh samples within the native Lighthouse
island trigger without moving any actor. Use the normal UiBridge placement
checks before teleporting to one. Native monitor updates establish membership;
the probe never edits that membership or calls a repair method.

Observations distinguish the movement, waypoint and hard-stuck list identities,
the current and remembered bodies, enabled convergence player count, island
membership, old relationship snapshots, null/old/current allies, and the notifier
latch, initial-message state, operator counts, pending notices and manual-query
cooldown. These observations are scoped assertions, not proof of all AI behavior.

Use real Redeploy death choices. Record loaded Core MVID/hash and mod versions,
the life-event ledger, before/death/after observations and all three log streams.
Remove the probe and testmods and restore host configuration/synchronization
afterward. No profile identifier is intentionally emitted by this probe.

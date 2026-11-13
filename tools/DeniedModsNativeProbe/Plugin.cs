using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.AI;

namespace DeniedModsNativeProbe;

[AttributeUsage(AttributeTargets.Method)]
public sealed class UiBridgeActionAttribute : Attribute
{
    public UiBridgeActionAttribute(string name) { Name = name; }
    public string Name { get; }
    public string Description { get; set; }
    public bool Control { get; set; }
}

// Developer-only instrumentation. Never distributed; observations do not repair foreign state.
[BepInPlugin("com.maschine.DeniedModsNativeProbe", "Denied Mods Native Probe", "1.0.0")]
public sealed class Plugin : BaseUnityPlugin
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static Player _previous;
    private static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);
    private static object Read(object value, string member)
    {
        if (value == null) return null;
        var type = value as Type ?? value.GetType();
        var instance = value is Type ? null : value;
        var field = type.GetField(member, All);
        if (field != null) return field.GetValue(instance);
        return type.GetProperty(member, All)?.GetValue(instance);
    }
    private static object SingletonOf(Type type) => type == null ? null : Read(typeof(Singleton<>).MakeGenericType(type), "Instance");
    private static int Id(object value) => ReferenceEquals(value, null) ? 0 : value is UnityEngine.Object unity ? unity.GetInstanceID() : RuntimeHelpers.GetHashCode(value);
    private static object[] Entries(object value) => value is IEnumerable items ? items.Cast<object>().ToArray() : Array.Empty<object>();
    private static bool Has(object value, object target) => !ReferenceEquals(target, null) && Entries(value).Any(x => ReferenceEquals(x, target));
    private static object Position(Player body) => body == null ? null : new[] { body.Position.x, body.Position.y, body.Position.z };

    [UiBridgeAction("observe", Description = "Read the exact body references and private ORBIT, Questing island and ISB notifier state; remember saves probe comparison only.")]
    public static string Observe(bool remember = false)
    {
        var world = Singleton<GameWorld>.Instance;
        var current = world?.MainPlayer;
        var orbit = SingletonOf(Find("Orbit.Core.OrbitManager"));
        var movement = Read(orbit, "MovementSystem");
        var waypoint = Read(orbit, "WaypointSystem");
        var humans = Read(movement, "_humanPlayers");
        var waypointHumans = Read(waypoint, "_humanPlayers");
        var hard = Read(Read(movement, "_stuckRemediation"), "_hardRemediation");
        var hardHumans = Read(hard, "<humanPlayers>P");
        var monitorType = Find("QuestingBots.Components.LightkeeperIslandMonitor");
        var monitor = world != null && monitorType != null ? world.GetComponent(monitorType) : null;
        var island = Read(monitor, "playersOnIsland");
        var handler = Read(monitorType, "LightkeeperTraderZoneColliderHandler");
        var collider = Read(handler, "trigger") as Collider;
        var oldSnapshotReferences = 0;
        foreach (var field in new[] { "originalAllies", "originalEnemies" })
            if (Read(monitor, field) is IDictionary snapshots)
                foreach (DictionaryEntry entry in snapshots)
                    oldSnapshotReferences += (ReferenceEquals(entry.Key, _previous) ? 1 : 0) + Entries(entry.Value).Count(x => ReferenceEquals(x, _previous));
        var groups = world?.AllAlivePlayersList?.Where(p => p != null && p.IsAI).Select(p => p.AIData?.BotOwner?.BotsGroup).Where(g => g != null).Distinct().ToArray();
        var isb = Find("ISBInfo.ISBInfoPlugin");
        var result = new
        {
            frame = Time.frameCount, world = Id(world), body = Id(current), previous = Id(_previous),
            changed = _previous != null && !ReferenceEquals(current, _previous), side = current?.Side.ToString(),
            alive = current?.HealthController?.IsAlive, hands = current?.HandsController?.GetType().Name, position = Position(current),
            orbit = new { manager = Id(orbit), list = Id(humans), shared = ReferenceEquals(humans, waypointHumans), hardShared = ReferenceEquals(humans, hardHumans), current = Has(humans, current), previous = Has(humans, _previous), humanCount = Entries(humans).Length, convergenceActive = Read(waypoint, "ConvergenceActive"), convergencePlayers = Entries(Read(waypoint, "_convergencePlayerCoords")).Length },
            questing = new { monitor = Id(monitor), members = Entries(island).Select(x => new { body = Id(x), current = ReferenceEquals(x, current), previous = ReferenceEquals(x, _previous) }).ToArray(), oldSnapshotReferences, nullAllies = groups?.Sum(g => g.Allies.Count(a => ReferenceEquals(a, null))), oldAllies = groups?.Sum(g => g.Allies.Count(a => ReferenceEquals(a, _previous))), currentAllies = groups?.Count(g => g.Allies.Any(a => ReferenceEquals(a, current))), bounds = collider == null ? null : new { center = collider.bounds.center.ToString(), size = collider.bounds.size.ToString() } },
            isb = new { ended = Read(isb, "LocalRaidPresentationEnded"), observed = Read(isb, "LocalPlayerObservedInRaid"), initial = Read(isb, "InitialMessageShown"), spawned = Read(isb, "ISBSpawnedThisRaid"), counts = Read(isb, "ISBAliveCounts"), commanderShown = Read(isb, "CommanderKIAShown"), scheduled = Entries(Read(isb, "DeathNotificationsScheduled")).Length, cached = Entries(Read(isb, "CachedISBOperators")).Length, nextManual = Read(Read(isb, "Instance"), "_nextManualNotificationTime") }
        };
        if (remember) _previous = current;
        return JsonConvert.SerializeObject(result);
    }

    [UiBridgeAction("islandPoints", Description = "Find grounded navmesh samples inside the actual Lighthouse island trigger, without moving any actor.")]
    public static string IslandPoints()
    {
        var handler = Read(Find("QuestingBots.Components.LightkeeperIslandMonitor"), "LightkeeperTraderZoneColliderHandler");
        var collider = Read(handler, "trigger") as Collider ?? throw new InvalidOperationException("No native island collider");
        var bounds = collider.bounds;
        var points = new List<float[]>();
        for (int x = 1; x < 8 && points.Count < 12; x++)
            for (int z = 1; z < 8 && points.Count < 12; z++)
            {
                var top = new Vector3(Mathf.Lerp(bounds.min.x, bounds.max.x, x / 8f), bounds.max.y + 5, Mathf.Lerp(bounds.min.z, bounds.max.z, z / 8f));
                if (Physics.Raycast(top, Vector3.down, out var ground, bounds.size.y + 20, ~0, QueryTriggerInteraction.Ignore)
                    && NavMesh.SamplePosition(ground.point, out var nav, 3, NavMesh.AllAreas) && bounds.Contains(nav.position))
                    points.Add(new[] { nav.position.x, nav.position.y, nav.position.z });
            }
        return JsonConvert.SerializeObject(points);
    }
}

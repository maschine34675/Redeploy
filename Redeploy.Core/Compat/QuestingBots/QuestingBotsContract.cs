using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal sealed class QuestingBotsContract
{
    internal static readonly Version Reviewed = new(1, 2, 0, 0);
    internal readonly Type Monitor;
    internal readonly FieldInfo Location, Players, Allies, Enemies;
    internal readonly MethodInfo CheckPlayer, CheckPoint;

    internal QuestingBotsContract(Type plugin, string guid, Version version)
    {
        var assembly = StandaloneHudContract.Plugin(plugin, guid, QuestingBotsCompat.PluginGuid,
            version, Reviewed, "QuestingBots.QuestingBotsPlugin", "QuestingBots-Client", Reviewed);
        Monitor = assembly.GetType("QuestingBots.Components.LightkeeperIslandMonitor", true);
        var location = assembly.GetType("QuestingBots.Components.LocationData", true);
        if (!typeof(MonoBehaviour).IsAssignableFrom(Monitor) || !typeof(MonoBehaviour).IsAssignableFrom(location))
            throw new InvalidOperationException("island monitor and location data must be world components");
        Location = StandaloneHudContract.Field(Monitor, "locationData", location);
        Players = StandaloneHudContract.Field(Monitor, "playersOnIsland", typeof(List<Player>));
        Allies = StandaloneHudContract.Field(Monitor, "originalAllies", typeof(Dictionary<Player, IPlayer[]>));
        Enemies = StandaloneHudContract.Field(Monitor, "originalEnemies", typeof(Dictionary<Player, IPlayer[]>));
        CheckPlayer = StandaloneHudContract.Method(Monitor, "IsPlayerOnLightkeeperIsland", typeof(bool), false, typeof(Player));
        CheckPoint = StandaloneHudContract.Method(location, "IsPointOnLightkeeperIsland", typeof(bool), false, typeof(Vector3?));
    }

    internal QuestingIslandState Read(Component monitor, GameWorld world)
    {
        if (monitor == null || !ReferenceEquals(monitor.gameObject, world.gameObject)
            || Location.GetValue(monitor) is not Component location || location == null
            || !ReferenceEquals(location.gameObject, world.gameObject))
            throw new InvalidOperationException("Lightkeeper monitor does not belong to the current world");
        return new QuestingIslandState(world, (List<Player>)Players.GetValue(monitor),
            (Dictionary<Player, IPlayer[]>)Allies.GetValue(monitor),
            (Dictionary<Player, IPlayer[]>)Enemies.GetValue(monitor));
    }

    internal bool IsOnIsland(Component monitor, Player body)
        => (bool)CheckPoint.Invoke(Location.GetValue(monitor), new object[] { (Vector3?)body.Position });
}

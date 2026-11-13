using System;
using System.Collections.Generic;
using System.Linq;
using EFT;

namespace Redeploy.Core.Compat;

/// <summary>Only the monitor's physical-body references; never its profile-ID helpers.</summary>
internal sealed class QuestingIslandState
{
    internal readonly GameWorld World;
    internal readonly List<Player> Players;
    internal readonly Dictionary<Player, IPlayer[]> Allies, Enemies;

    internal QuestingIslandState(GameWorld world, List<Player> players,
        Dictionary<Player, IPlayer[]> allies, Dictionary<Player, IPlayer[]> enemies)
    {
        World = world;
        Players = players ?? throw new InvalidOperationException("island membership is unavailable");
        Allies = allies ?? throw new InvalidOperationException("island ally snapshots are unavailable");
        Enemies = enemies ?? throw new InvalidOperationException("island enemy snapshots are unavailable");
        Validate();
    }

    internal bool SameCollections(QuestingIslandState other)
        => ReferenceEquals(Players, other.Players) && ReferenceEquals(Allies, other.Allies)
           && ReferenceEquals(Enemies, other.Enemies) && ReferenceEquals(World, other.World);

    private void Validate()
    {
        foreach (var player in Players.Concat(Allies.Keys).Concat(Enemies.Keys))
        {
            // Native snapshots keep historical bots. A destroyed/detached bot is
            // not an authority to a current group, but must not veto a respawn.
            if (player == null || player.GameWorld == null) continue;
            if (!ReferenceEquals(player.GameWorld, World))
                throw new InvalidOperationException("island membership contains a foreign body");
        }
        if (Allies.Values.Any(row => row == null) || Enemies.Values.Any(row => row == null))
            throw new InvalidOperationException("island snapshots are unavailable");
    }

    internal Action<bool> Retire(Player body)
    {
        Validate(); // All validation precedes the first mutation.
        if (ReferenceEquals(body, null) || body.IsAI || !ReferenceEquals(body.GameWorld, World))
            throw new InvalidOperationException("only this world's outgoing human body may be retired");

        // Snapshots also retain bots which already left the island. Include those groups:
        // their later revert must neither retain nor resurrect the outgoing human.
        var groups = Players.Concat(Allies.Keys).Concat(Enemies.Keys)
            .Select(player => new { Player = player, Group = OwnedGroup(player) }).Where(row => row.Group != null)
            .GroupBy(row => row.Group).Select(rows => rows.First()).ToArray();
        var removedAllies = groups.Where(row => row.Group.Allies.Any(ally => ReferenceEquals(ally, body)))
            .Select(row => new { row.Player, row.Group, Allies = row.Group.Allies }).ToArray();
        var wasMember = Players.Any(player => ReferenceEquals(player, body));
        foreach (var row in groups)
        {
            // EFT AddAlly(Player) only appends Allies; RemoveAlly(BotOwner) instead
            // removes a GROUP MEMBER and invokes bot tactics. A human is not a member.
            row.Group.Allies.RemoveAll(ally => ReferenceEquals(ally, body));
        }
        Players.RemoveAll(player => ReferenceEquals(player, body));
        var allyRows = Strip(Allies, body);
        var enemyRows = Strip(Enemies, body);
        // Do not restore enemies here. That would restore a corpse, and profile-ID
        // restoration after registration can instead change the replacement's allies.
        // EFT's normal AddActivePLayer and the monitor's location check own the successor.
        if (body.HealthController?.IsAlive != true) return null;
        return outside =>
        {
            Restore(Allies, allyRows, body);
            Restore(Enemies, enemyRows, body);
            foreach (var row in removedAllies)
                if (ReferenceEquals(OwnedGroup(row.Player), row.Group) && ReferenceEquals(row.Group.Allies, row.Allies)
                    && !row.Allies.Any(ally => ReferenceEquals(ally, body))) row.Allies.Add(body);
            // Outside recovery needs the native exit transition to restore hostility.
            // Inside recovery must remain absent so native entry also allies newcomers
            // that arrived while the old body was unregistered.
            if (outside && wasMember && !Players.Any(player => ReferenceEquals(player, body))) Players.Add(body);
        };
    }

    private BotsGroup OwnedGroup(Player player)
    {
        if (player == null || !ReferenceEquals(player.GameWorld, World)) return null;
        // Questing's GetBotOwner resolves by profile ID, which can alias a pooled
        // historical bot. Only the physical owner's current player grants access.
        var owner = player.AIData?.BotOwner;
        return owner != null && ReferenceEquals(owner.GetPlayer, player) ? owner.BotsGroup : null;
    }

    private static List<KeyValuePair<Player, IPlayer[]>> Strip(Dictionary<Player, IPlayer[]> snapshots, Player body)
    {
        var removed = new List<KeyValuePair<Player, IPlayer[]>>();
        foreach (var key in snapshots.Keys.ToArray())
        {
            if (ReferenceEquals(key, body)) snapshots.Remove(key);
            else if (snapshots[key].Any(value => ReferenceEquals(value, body)))
            {
                var row = snapshots[key].Where(value => !ReferenceEquals(value, body)).ToArray();
                snapshots[key] = row;
                removed.Add(new KeyValuePair<Player, IPlayer[]>(key, row));
            }
        }
        return removed;
    }

    private static void Restore(Dictionary<Player, IPlayer[]> snapshots, List<KeyValuePair<Player, IPlayer[]>> rows, Player body)
    {
        foreach (var row in rows)
        {
            // A failed swap can recover this exact still-living body. Preserve its
            // pre-island relationships, but never overwrite a newer native snapshot.
            if (snapshots.TryGetValue(row.Key, out var current) && ReferenceEquals(current, row.Value)
                && !current.Any(player => ReferenceEquals(player, body)))
                snapshots[row.Key] = current.Concat(new IPlayer[] { body }).ToArray();
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using EFT;

namespace Redeploy.Core.Compat;

/// <summary>Detached shallow copies keep snapshot ownership intact without JSON roundtrips.</summary>
internal sealed class RaidReviewPacketSchema
{
    internal sealed class Shape
    {
        internal Type Type;
        internal PropertyInfo Session, Profile, Other, Name, Time, Status, Exit;
    }
    private static readonly Func<object, object> Clone = (Func<object, object>)Delegate.CreateDelegate(typeof(Func<object, object>),
        typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic));
    private readonly Dictionary<string, Shape> _shapes = new(StringComparer.Ordinal);
    private PropertyInfo _level, _team, _group, _spawnTime, _type, _brain, _difficulty, _sainName;
    internal static RaidReviewPacketSchema Resolve(Assembly assembly)
    {
        var schema = new RaidReviewPacketSchema();
        void Add(string action, string typeName, string other = null, bool name = false, bool status = false, bool exit = false)
        {
            var type = assembly.GetType("RAID_REVIEW." + typeName, true);
            var shape = new Shape { Type = type, Session = Property(type, "sessionId", typeof(string)), Profile = Property(type, "profileId", typeof(string)) };
            if (other != null) shape.Other = Property(type, other, typeof(string));
            if (name) shape.Name = Property(type, "name", typeof(string));
            if (status) { shape.Status = Property(type, "status", assembly.GetType("RAID_REVIEW.PlayerStatus", true)); shape.Time = Property(type, "time", typeof(long)); }
            if (exit) { shape.Exit = Property(type, "exitStatus", null); shape.Time = Property(type, "timeInRaid", typeof(long)); }
            schema._shapes.Add(action, shape);
        }
        Add("START", "TrackingRaid", exit: true); Add("END", "TrackingRaid", exit: true);
        Add("PLAYER", "TrackingPlayer", name: true); Add("PLAYER_UPDATE", "TrackingPlayer", name: true);
        Add("POSITION", "TrackingPlayerData"); Add("PLAYER_STATUS", "TrackingPlayerDeadOrUnspawned", status: true);
        Add("KILL", "TrackingRaidKill", "killedId"); Add("BALLISTIC", "TrackingBallistic", "hitPlayerId");
        Add("LOOT", "TrackingLootItem"); Add("PLAYER_INVENTORY", "TrackingPlayerInventory");
        var player = schema.Get("PLAYER").Type;
        schema._level = Property(player, "level", typeof(int)); schema._team = Property(player, "team", typeof(EPlayerSide));
        schema._group = Property(player, "group", typeof(int)); schema._spawnTime = Property(player, "spawnTime", typeof(long));
        schema._type = Property(player, "type", typeof(string)); schema._brain = Property(player, "mod_SAIN_brain", typeof(string));
        schema._difficulty = Property(player, "mod_SAIN_difficulty", typeof(string)); schema._sainName = Property(player, "mod_SAIN_name", typeof(string));
        return schema;
    }
    private static PropertyInfo Property(Type type, string name, Type value)
    {
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (property == null || !property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0
            || (value != null && property.PropertyType != value)) throw new MissingMemberException(type.FullName, name);
        return property;
    }
    internal Shape Get(string action) => _shapes.TryGetValue(action, out var value) ? value : null;
    internal object Project(string action, object payload, Func<string, bool, string> map, Func<string, string> name)
    {
        if (action == "POSITION_BATCH" || action == "PLAYER_CHECK")
        {
            if (payload is not IList list) throw new InvalidOperationException("Raid Review batch schema changed");
            var result = new object[list.Count];
            for (int i = 0; i < result.Length; i++) result[i] = Project(action == "POSITION_BATCH" ? "POSITION" : "PLAYER", list[i], map, name);
            return result;
        }
        var shape = Get(action);
        if (shape == null || action == "START" || action == "END") return payload;
        if (payload == null || payload.GetType() != shape.Type) throw new InvalidOperationException("Raid Review " + action + " snapshot schema changed");
        var real = (string)shape.Profile.GetValue(payload);
        var actor = map(real, false);
        var other = shape.Other == null ? null : (string)shape.Other.GetValue(payload);
        var mappedOther = shape.Other == null ? null : map(other, true);
        if (actor == real && mappedOther == other) return payload;
        var copy = Clone(payload);
        shape.Profile.SetValue(copy, actor);
        if (shape.Other != null) shape.Other.SetValue(copy, mappedOther);
        if (shape.Name != null && actor != real) shape.Name.SetValue(copy, (string)shape.Name.GetValue(payload) + name(actor));
        return copy;
    }
    internal object Status(string session, string alias, long time, int status)
    {
        var shape = Get("PLAYER_STATUS");
        var value = Activator.CreateInstance(shape.Type);
        shape.Session.SetValue(value, session); shape.Profile.SetValue(value, alias);
        shape.Time.SetValue(value, time); shape.Status.SetValue(value, Enum.ToObject(shape.Status.PropertyType, status));
        return value;
    }
    internal object Player(string session, EFT.Player body, long time)
    {
        var shape = Get("PLAYER"); var value = Activator.CreateInstance(shape.Type);
        shape.Session.SetValue(value, session); shape.Profile.SetValue(value, body.ProfileId);
        shape.Name.SetValue(value, string.IsNullOrWhiteSpace(body.Profile.Info.MainProfileNickname) ? body.Profile.Nickname : body.Profile.Info.MainProfileNickname);
        _level.SetValue(value, body.Profile.Info.Level); _team.SetValue(value, body.Side); _group.SetValue(value, 0);
        _spawnTime.SetValue(value, time); _type.SetValue(value, "HUMAN"); _brain.SetValue(value, "PLAYER");
        _difficulty.SetValue(value, ""); _sainName.SetValue(value, "");
        return value;
    }
}

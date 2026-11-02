using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;

namespace Redeploy.Core.Compat;

/// <summary>Checked once per installed plugin identity, never discovered on the recording hot path.</summary>
internal sealed class RaidReviewContract
{
    internal const string PluginGuid = "ekky.raidreview";
    internal static readonly System.Version ReviewedVersion = new(1, 10, 0, 0);
    internal MethodInfo Send, AbortSender, Add, Observe, Confirm, Depart, InventoryCapture, LootCapture, LootFlush, Kill, Ballistic, SessionEnd, Start;
    internal FieldInfo Sender, World, MyPlayer, Session, Watch, TrackingPlayers, Active, Confirmed, Statuses, Departed, Captured, PendingLoot;
    internal Type StatusType;
    internal RaidReviewPacketSchema Schema;

    internal static RaidReviewContract Resolve(Type plugin, string guid, System.Version version)
    {
        var assembly = StandaloneHudContract.Plugin(plugin, guid, PluginGuid, version, ReviewedVersion,
            "RAID_REVIEW.RAID_REVIEW", "RAID_REVIEW", ReviewedVersion);
        Type T(string name) => assembly.GetType("RAID_REVIEW." + name, true);
        var root = T("RAID_REVIEW"); var telemetry = T("Telemetry"); var roster = T("RecordingRoster");
        var inventory = T("RecordingInventory"); var loot = T("RAID_REVIEW_Player_OnItemAddedOrRemovedPatch");
        var status = T("PlayerStatus");
        foreach (var pair in new[] { ("Alive", 0), ("Dead", 1), ("Unspawned", 2), ("Unknown", 3), ("Extracted", 4), ("Downed", 5) })
            if (!status.IsEnum || !Enum.IsDefined(status, pair.Item1) || Convert.ToInt32(Enum.Parse(status, pair.Item1)) != pair.Item2)
                throw new MissingMemberException("Raid Review status schema changed");
        var c = new RaidReviewContract
        {
            Send = Method(telemetry, "SendSnapshot", typeof(bool), typeof(string), typeof(object)),
            Sender = Field(telemetry, "sender", T("OrderedPacketSender")),
            AbortSender = StandaloneHudContract.Method(T("OrderedPacketSender"), "Abort", typeof(void), false, typeof(string)),
            World = Field(root, "gameWorld", typeof(GameWorld)), MyPlayer = Field(root, "myPlayer", typeof(Player)), Session = Field(root, "sessionId", typeof(string)),
            Watch = Field(root, "stopwatch", typeof(Stopwatch)), StatusType = status,
            TrackingPlayers = Field(root, "trackingPlayers", typeof(Dictionary<,>).MakeGenericType(typeof(string), T("TrackingPlayer"))),
            Active = Field(roster, "active", typeof(Dictionary<string, Player>)),
            Confirmed = Field(roster, "confirmed", typeof(Dictionary<,>).MakeGenericType(typeof(string), status)),
            Statuses = Field(roster, "statuses", typeof(Dictionary<,>).MakeGenericType(typeof(string), status)),
            Departed = Field(roster, "departed", typeof(HashSet<string>)), Captured = Field(inventory, "captured", typeof(HashSet<string>)),
            PendingLoot = Field(loot, "_pendingRemoves", typeof(Dictionary<,>).MakeGenericType(typeof(string), T("TrackingLootItem"))),
            Add = Method(roster, "Add", typeof(void), typeof(Player)), Observe = Method(roster, "Observe", typeof(bool), typeof(Player)),
            Confirm = Method(roster, "Confirm", typeof(void), typeof(Player), status),
            Depart = Method(roster, "Depart", typeof(void), typeof(Player)),
            InventoryCapture = Method(inventory, "Capture", typeof(void), typeof(Player)),
            LootCapture = Method(loot, "Capture", typeof(void), typeof(Player), typeof(Item), typeof(ItemAddress), typeof(bool)),
            LootFlush = Method(loot, "FlushPending", typeof(void), typeof(bool)),
            Kill = Method(T("RAID_REVIEW_Player_OnBeenKilledByAggressorPatch"), "PatchPostFix", typeof(void), typeof(Player).MakeByRefType(), typeof(IPlayer), typeof(DamageInfo), typeof(EBodyPart), typeof(EDamageType)),
            Ballistic = Method(T("RAID_REVIEW_ClientGameWorld_ShotDelegatePatch"), "PatchPostFix", typeof(void), typeof(ClientGameWorld).MakeByRefType(), typeof(Shot)),
            SessionEnd = Method(T("RAID_REVIEW_Player_OnGameSessionEndPatch"), "PatchPostFix", typeof(void), typeof(Player).MakeByRefType(), typeof(ExitStatus), typeof(float), typeof(string), typeof(string)),
            Start = Method(T("RAID_REVIEW_Player_OnGameStartedPatch"), "PatchPostFix", typeof(void), typeof(GameWorld).MakeByRefType()),
            Schema = RaidReviewPacketSchema.Resolve(assembly)
        };
        return c;
    }

    private static MethodInfo Method(Type type, string name, Type result, params Type[] args)
        => StandaloneHudContract.Method(type, name, result, true, args);
    private static FieldInfo Field(Type type, string name, Type value)
    {
        var field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field == null || field.FieldType != value) throw new MissingFieldException(type.FullName, name);
        return field;
    }
    internal long Now => (Watch.GetValue(null) as Stopwatch)?.ElapsedMilliseconds ?? 0;
    internal void ForgetProfile(string id)
    {
        // Only the live recorder's cache for the replaced identity. Sent history is immutable.
        ((IDictionary)Active.GetValue(null)).Remove(id);
        ((IDictionary)Confirmed.GetValue(null)).Remove(id);
        ((IDictionary)Statuses.GetValue(null)).Remove(id);
        ((IDictionary)TrackingPlayers.GetValue(null)).Remove(id);
        ((HashSet<string>)Departed.GetValue(null)).Remove(id);
        ((HashSet<string>)Captured.GetValue(null)).Remove(id);
    }
    internal void Abort(string reason)
    {
        var sender = Sender.GetValue(null);
        if (sender != null) AbortSender.Invoke(sender, new object[] { reason });
    }
}

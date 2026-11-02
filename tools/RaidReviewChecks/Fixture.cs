using System.Diagnostics;
using System.Runtime.CompilerServices;
using BepInEx;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
namespace RAID_REVIEW;
[BepInPlugin("ekky.raidreview", "Raid Review", "1.10.0")]
public class RAID_REVIEW : BaseUnityPlugin
{
    public static GameWorld gameWorld;
    public static Player myPlayer;
    public static string sessionId;
    public static Stopwatch stopwatch = Stopwatch.StartNew();
    public static Dictionary<string, TrackingPlayer> trackingPlayers = new();
    public static TrackingPlayer Metadata(Player body)
    {
        var row = new TrackingPlayer { sessionId = sessionId, profileId = body.ProfileId, name = "Tester" };
        trackingPlayers[body.ProfileId] = row; Telemetry.SendSnapshot("PLAYER", row); return row;
    }
}
public static class Telemetry
{
    public static OrderedPacketSender sender = new();
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool SendSnapshot(string action, object snapshot) => sender.Enqueue(action, snapshot);
}
public class OrderedPacketSender
{
    public readonly List<(string Action, object Value)> Packets = new();
    public bool Faulted, Reject;
    public string Reason;
    public bool Enqueue(string action, object value) { if (Faulted || Reject) return false; Packets.Add((action, value)); return true; }
    public void Abort(string reason) { Faulted = true; Reason = reason; }
}
// Reduced source-faithful roster state machine: confirmed/profile-only death poisoning and
// active-body replacement behavior intentionally reproduce upstream 1.10.0 before patching.
public static class RecordingRoster
{
    public static readonly Dictionary<string, Player> active = new();
    public static readonly HashSet<string> departed = new();
    public static readonly Dictionary<string, PlayerStatus> statuses = new(), confirmed = new();
    public static bool ThrowObserve;
    public static Action Nested;
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Add(Player body) { active[body.ProfileId] = body; departed.Remove(body.ProfileId); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Confirm(Player body, PlayerStatus status) { if (status == PlayerStatus.Dead || status == PlayerStatus.Extracted) confirmed[body.ProfileId] = status; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Depart(Player body)
    {
        if (active.TryGetValue(body.ProfileId, out var current) && !ReferenceEquals(current, body)) return;
        Add(body); departed.Add(body.ProfileId);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public static bool Observe(Player body)
    {
        Nested?.Invoke(); if (ThrowObserve) throw new InvalidOperationException("fixture observe exception");
        var state = confirmed.TryGetValue(body.ProfileId, out var terminal) ? terminal : !body.HealthController.IsAlive ? PlayerStatus.Dead
            : departed.Contains(body.ProfileId) ? PlayerStatus.Unspawned : PlayerStatus.Alive;
        if (state == PlayerStatus.Dead) confirmed[body.ProfileId] = state;
        statuses[body.ProfileId] = state;
        Telemetry.SendSnapshot("PLAYER_STATUS", new TrackingPlayerDeadOrUnspawned { sessionId = RAID_REVIEW.sessionId, profileId = body.ProfileId, status = state, time = RAID_REVIEW.stopwatch.ElapsedMilliseconds });
        if (state == PlayerStatus.Alive) return true;
        active.Remove(body.ProfileId); departed.Remove(body.ProfileId); return false;
    }
    public static void Reset() { active.Clear(); departed.Clear(); confirmed.Clear(); statuses.Clear(); ThrowObserve = false; Nested = null; }
}
public static class RecordingInventory
{
    public static readonly HashSet<string> captured = new();
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Capture(Player body)
    {
        if (!captured.Add(body.ProfileId)) return;
        Telemetry.SendSnapshot("PLAYER_INVENTORY", new TrackingPlayerInventory { sessionId = RAID_REVIEW.sessionId, profileId = body.ProfileId });
    }
}
public static class RAID_REVIEW_Player_OnItemAddedOrRemovedPatch
{
    public static readonly Dictionary<string, TrackingLootItem> _pendingRemoves = new();
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Capture(Player body, Item item, ItemAddress location, bool added)
    {
        var row = new TrackingLootItem { sessionId = RAID_REVIEW.sessionId, profileId = body.ProfileId, time = RAID_REVIEW.stopwatch.ElapsedMilliseconds, itemId = item.Id, added = added };
        var key = body.ProfileId + "|" + item.Id;
        if (!added) { _pendingRemoves[key] = row; return; }
        if (_pendingRemoves.TryGetValue(key, out var previous)) { _pendingRemoves.Remove(key); if (row.time - previous.time <= 50) return; Telemetry.SendSnapshot("LOOT", previous); }
        Telemetry.SendSnapshot("LOOT", row);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void FlushPending(bool all)
    {
        foreach (var pair in _pendingRemoves.ToArray())
        {
            if (!all && RAID_REVIEW.stopwatch.ElapsedMilliseconds - pair.Value.time < 50) continue;
            Telemetry.SendSnapshot("LOOT", pair.Value); _pendingRemoves.Remove(pair.Key);
        }
    }
}
public static class RAID_REVIEW_Player_OnBeenKilledByAggressorPatch
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static void PatchPostFix(ref Player body, IPlayer killer, DamageInfo damage, EBodyPart part, EDamageType type)
        => Telemetry.SendSnapshot("KILL", new TrackingRaidKill { sessionId = RAID_REVIEW.sessionId, profileId = (killer as Player)?.ProfileId ?? "", killedId = body.ProfileId, time = RAID_REVIEW.stopwatch.ElapsedMilliseconds });
}
public static class RAID_REVIEW_ClientGameWorld_ShotDelegatePatch
{
    public static int OriginalCalls, EventFailures;
    public static bool CanCapture = true, BallisticsTracking = true;
    [MethodImpl(MethodImplOptions.NoInlining)] public static void PatchPostFix(ref ClientGameWorld world, Shot shot)
    {
        OriginalCalls++;
        if (!CanCapture || !BallisticsTracking) return;
        // Upstream owns an event-local catch. A passive adapter must not replace it
        // with a recording-wide Abort before this handler has even run its guards.
        try
        {
            Telemetry.SendSnapshot("BALLISTIC", new TrackingBallistic { sessionId = RAID_REVIEW.sessionId, profileId = (shot.Player?.iPlayer as Player)?.ProfileId ?? "", hitPlayerId = shot.HittedBallisticCollider?.gameObject.GetComponentInParent<Player>()?.ProfileId ?? "" });
        }
        catch { EventFailures++; }
    }
}
public static class RAID_REVIEW_Player_OnGameSessionEndPatch
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static void PatchPostFix(ref Player body, ExitStatus status, float time, string location, string name)
        => Telemetry.SendSnapshot("END", new TrackingRaid { sessionId = RAID_REVIEW.sessionId, profileId = RAID_REVIEW.sessionId, exitStatus = status });
}
public static class RAID_REVIEW_Player_OnGameStartedPatch
{
    [MethodImpl(MethodImplOptions.NoInlining)] public static void PatchPostFix(ref GameWorld world)
    {
        RAID_REVIEW.gameWorld = world; RAID_REVIEW.myPlayer = world.MainPlayer;
        Telemetry.SendSnapshot("START", new TrackingRaid { sessionId = RAID_REVIEW.sessionId, profileId = RAID_REVIEW.sessionId });
        RecordingRoster.Add(world.MainPlayer); RAID_REVIEW.Metadata(world.MainPlayer); RecordingInventory.Capture(world.MainPlayer);
    }
}

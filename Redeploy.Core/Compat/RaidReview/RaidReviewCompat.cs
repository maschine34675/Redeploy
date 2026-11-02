using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using EFT;
using EFT.Ballistics;
using HarmonyLib;
using Redeploy.Core.Api;
using Redeploy.Core.Engine;
using UnityEngine;

namespace Redeploy.Core.Compat;

/// <summary>One recording, detached identities for each confirmed local physical life.</summary>
internal static class RaidReviewCompat
{
    private const int StartupCapacity = 256;
    private const float StartupTimeout = 10f;
    private const float EntryReplacementTimeout = 35f; // Core's body creation timeout is 30s.
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.RaidReviewCompat");
    private static readonly ConditionalWeakTable<Player, object> KnownBodies = new();
    private static readonly ConditionalWeakTable<GameWorld, object> RetiredWorlds = new();
    private static readonly ConditionalWeakTable<object, ActorScope> LootOrigins = new();
    private static RaidReviewContract _contract;
    private static BaseUnityPlugin _plugin;
    private static System.Version _version;
    private static BaseUnityPlugin _rejectedPlugin;
    private static System.Version _rejectedVersion;
    private static bool _initialized, _patched, _fault, _warned;
    private static string _failure;
    private static GameWorld _world;
    private static LocalGame _game;
    private static LocalPlayer _initial, _prepared;
    private static RaidReviewLifeLedger _lives;
    private static string _session;
    private static int _startFrame;
    private static float _startTime;
    private static bool _pending, _active, _ended, _retired, _recover;
    private static bool _deferredStart;
    private static bool _committedLife;
    private static float _deferredAt;
    private static readonly ConditionalWeakTable<Player, object> RejectedCandidates = new();
    private static readonly List<WeakReference<Player>> AttemptCandidates = new();
    private static readonly List<Packet> Startup = new();
    [ThreadStatic] private static bool _sending;
    [ThreadStatic] private static ActorScope _actor;

    private sealed class Packet
    {
        internal string Action;
        internal object Raw, Projected;
    }
    private sealed class ActorScope
    {
        internal Player Actor, Other;
        internal ActorScope Previous;
        internal bool Suppressed;
    }

    internal static string GetRejectionReason()
    {
        if (!_initialized)
        {
            _initialized = true;
            RespawnApi.Replaced += Replaced;
            RespawnApi.ReplacementFailed += Failed;
        }
        if (_fault) return _failure;
        if (!Chainloader.PluginInfos.TryGetValue(RaidReviewContract.PluginGuid, out var info))
        {
            if (_active || _pending) return Reject(new InvalidOperationException("plugin disappeared during a recording"), true);
            Clear(); _rejectedPlugin = null; _rejectedVersion = null; _failure = null; return null;
        }
        try
        {
            var version = StandaloneHudContract.Normalize(info.Metadata?.Version);
            if (info.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            if (ReferenceEquals(_rejectedPlugin, info.Instance) && _rejectedVersion == version)
            {
                // A failed contract/partial install is stable for this plugin identity.
                // Retry only still-held cleanup; never repeat reflection/patch installation.
                if (_patched) Clear();
                return _failure;
            }
            if (_contract != null && ReferenceEquals(_plugin, info.Instance) && _version == version) return _failure;
            if (_active || _pending) throw new InvalidOperationException("plugin changed during a recording");
            Clear();
            _rejectedPlugin = null; _rejectedVersion = null;
            _contract = RaidReviewContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version);
            _plugin = info.Instance; _version = version;
            _patched = true;
            Patch(_contract.Send, nameof(BeforeSend), nameof(AfterSend));
            Patch(_contract.Start, nameof(BeforeRecorderStart));
            Patch(_contract.Add, nameof(BeforeRoster));
            Patch(_contract.Depart, nameof(BeforeRoster));
            Patch(_contract.Confirm, nameof(BeforeConfirm), finalizer: nameof(RestoreScope));
            Patch(_contract.Observe, nameof(BeforeObserve), finalizer: nameof(RestoreScope));
            Patch(_contract.InventoryCapture, nameof(BeforeCapture), finalizer: nameof(RestoreScope));
            Patch(_contract.LootCapture, nameof(BeforeLoot), nameof(AfterLoot), nameof(RestoreScope));
            Patch(_contract.Kill, nameof(BeforeKill), finalizer: nameof(RestoreScope));
            Patch(_contract.Ballistic, nameof(BeforeBallistic), finalizer: nameof(RestoreScope));
            Patch(_contract.SessionEnd, nameof(BeforeSessionEnd));
            Verify();
            if (version > RaidReviewContract.ReviewedVersion && !_warned)
            {
                _warned = true;
                CoreLog.LogWarning("Raid Review " + version + " is newer than reviewed 1.10.0; APIs match, native compatibility is unverified.");
            }
            _failure = null;
        }
        catch (Exception ex)
        {
            _rejectedPlugin = info.Instance; _rejectedVersion = StandaloneHudContract.Normalize(info.Metadata?.Version);
            Reject(ex, _active || _pending);
            if (!_fault) { try { Clear(); } catch (Exception cleanup) { Reject(cleanup, false); } }
        }
        return _failure;
    }
    private static void Patch(MethodInfo target, string prefix, string postfix = null, string finalizer = null)
        => Patches.Patch(target, new HarmonyMethod(typeof(RaidReviewCompat), prefix) { priority = Priority.First },
            postfix == null ? null : new HarmonyMethod(typeof(RaidReviewCompat), postfix),
            finalizer: finalizer == null ? null : new HarmonyMethod(typeof(RaidReviewCompat), finalizer));
    private static void Verify()
    {
        void Require(MethodInfo target, string prefix, string postfix = null, string finalizer = null)
        {
            var info = Harmony.GetPatchInfo(target);
            bool Present(IEnumerable<Patch> patches, string name) => name == null || patches?.Count(p => p.owner == Patches.Id
                && p.PatchMethod == AccessTools.Method(typeof(RaidReviewCompat), name)) == 1;
            if (!Present(info?.Prefixes, prefix) || !Present(info?.Postfixes, postfix) || !Present(info?.Finalizers, finalizer))
                throw new MissingMethodException("Raid Review recording fence is missing: " + target.DeclaringType.Name + "." + target.Name);
        }
        Require(_contract.Send, nameof(BeforeSend), nameof(AfterSend));
        Require(_contract.Start, nameof(BeforeRecorderStart)); Require(_contract.SessionEnd, nameof(BeforeSessionEnd));
        Require(_contract.Add, nameof(BeforeRoster)); Require(_contract.Depart, nameof(BeforeRoster));
        Require(_contract.Confirm, nameof(BeforeConfirm), finalizer: nameof(RestoreScope));
        Require(_contract.Observe, nameof(BeforeObserve), finalizer: nameof(RestoreScope));
        Require(_contract.InventoryCapture, nameof(BeforeCapture), finalizer: nameof(RestoreScope));
        Require(_contract.LootCapture, nameof(BeforeLoot), nameof(AfterLoot), nameof(RestoreScope));
        Require(_contract.Kill, nameof(BeforeKill), finalizer: nameof(RestoreScope));
        Require(_contract.Ballistic, nameof(BeforeBallistic), finalizer: nameof(RestoreScope));
    }
    internal static void BeforeRaidStart(GameWorld world)
    {
        if (world == null || world is HideoutGameWorld || !ReferenceEquals(world, StandaloneHudScope.CurrentWorld)) return;
        var game = StandaloneHudScope.CurrentGame;
        if (game == null || !ReferenceEquals(game.GameWorld, world) || game.LocalPlayer == null
            || !ReferenceEquals(world.MainPlayer, game.LocalPlayer)) return;
        if (ReferenceEquals(world, _world)) return;
        if (_pending) FaultRecording(new InvalidOperationException("world changed before startup packets were resolved"));
        // A failed recording is not allowed to switch identities halfway through it. A new
        // world can try its contract again; late callbacks remain physically tombstoned.
        _fault = false;
        if (ReferenceEquals(_rejectedPlugin, null)) _failure = null;
        _world = null; _game = null; _lives = null; _initial = null; _prepared = null;
        _active = _pending = _ended = _retired = _recover = _deferredStart = _committedLife = false; _session = null; Startup.Clear(); AttemptCandidates.Clear();
        if (GetRejectionReason() != null || _contract == null) return;
        RetiredWorlds.Remove(world);
        _world = world; _game = game; _initial = game.LocalPlayer;
        var pump = world.gameObject.AddComponent<RaidReviewWorldPump>();
        pump.World = world;
    }
    private static bool BeforeRecorderStart(GameWorld __0)
    {
        if (!ReferenceEquals(__0, _world) || !BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(_game, _world)) return true;
        if (!_deferredStart) _deferredAt = Time.realtimeSinceStartup;
        _deferredStart = true;
        return false;
    }
    internal static void Prepare(LocalGame game, LocalPlayer body)
    {
        var failure = GetRejectionReason();
        if (failure != null) throw new InvalidOperationException(failure);
        if (_contract == null) return;
        if (_session == null || _ended) return; // Recording disabled / disconnected / voluntarily stopped.
        try { Verify(); } catch (Exception ex) { FaultRecording(ex); throw; }
        if ((!_active && !_pending) || _ended || !ReferenceEquals(game, _game) || !ReferenceEquals(game.GameWorld, _world)
            || !ReferenceEquals(body, _lives?.Current?.Body) || !BodyReplacementContext.IsCreating
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, body) || !BodyReplacementContext.OwnsBody(body)
            || !BodyReplacementContext.OwnsRaid(game, _world))
            throw new InvalidOperationException("Raid Review did not arm this physical life before START; restart the raid before replacing it");
        _prepared = body;
        // Pending removals belong to the old life and must not coalesce with replacement items.
        _contract.LootFlush.Invoke(null, new object[] { true });
        if (_fault) throw new InvalidOperationException(_failure);
    }
    internal static void AbortPreparation() { RetireCandidates(null); _prepared = null; _recover = false; }
    private static void RetireCandidates(Player committed)
    {
        foreach (var weak in AttemptCandidates)
            if (weak.TryGetTarget(out var candidate) && !ReferenceEquals(candidate, committed))
                RejectedCandidates.GetValue(candidate, _ => new object());
        AttemptCandidates.Clear();
    }
    private static void Failed(Player old, string stage)
    { if (ReferenceEquals(old, _prepared)) _recover = true; }
    private static void Replaced(Player old, Player body, string reason)
    {
        if (!ReferenceEquals(old, _prepared)) return;
        try { Commit(body); } catch (Exception ex) { FaultRecording(ex); }
    }
    private static bool Exact(Player body)
        => body is LocalPlayer && body != null && body.IsYourPlayer && body.HealthController?.IsAlive == true
            && BodyReplacementContext.OwnsRaid(_game, _world) && BodyReplacementContext.OwnsBody(body)
            && ReferenceEquals(body.GameWorld, _world) && ReferenceEquals(_world.MainPlayer, body)
            && ReferenceEquals(_game.LocalPlayer, body) && ReferenceEquals(GamePlayerOwner.MyPlayer, body)
            && _world.RegisteredPlayers.Contains(body);
    private static void Commit(Player body)
    {
        if (!Exact(body) || ReferenceEquals(body, _prepared)) throw new InvalidOperationException("replacement is not Core's committed physical body");
        var old = _lives.Current;
        EmitStatus(old, old.Dead || old.Body.HealthController?.IsAlive == false ? 1 : 2);
        _contract.ForgetProfile(old.Real);
        // Own-Scav may have transiently registered under its own new ID; clear only that ID too.
        if (body.ProfileId != old.Real) _contract.ForgetProfile(body.ProfileId);
        _lives.Add(body); KnownBodies.GetValue(body, _ => new object());
        _committedLife = true;
        RetireCandidates(body);
        _prepared = null; _recover = false;
        _contract.Add.Invoke(null, new object[] { body });
        // A second swap can happen before RR's next 5 Hz sample. Publish each confirmed
        // life's metadata immediately so its terminal events never lack a participant row.
        var metadata = _contract.Schema.Player(_session, body, _contract.Now);
        ((IDictionary)_contract.TrackingPlayers.GetValue(null))[body.ProfileId] = metadata;
        if (!(bool)_contract.Send.Invoke(null, new[] { (object)"PLAYER", metadata })) throw new InvalidOperationException("replacement metadata enqueue failed");
        _contract.InventoryCapture.Invoke(null, new object[] { body });
    }
    internal static void Tick()
    {
        if (_contract == null || _fault || _world == null) return;
        try
        {
            if (_deferredStart)
            {
                if (Time.realtimeSinceStartup - _deferredAt > EntryReplacementTimeout)
                    throw new InvalidOperationException("entry replacement did not settle before recording startup");
                if (BodyReplacementContext.IsCreating) return;
                if (_retired || !ReferenceEquals(_world, StandaloneHudScope.CurrentWorld) || !ReferenceEquals(_game, StandaloneHudScope.CurrentGame))
                    throw new InvalidOperationException("raid ended before deferred recording startup");
                if (_game.LocalPlayer == null || !ReferenceEquals(_game.LocalPlayer, _world.MainPlayer))
                    throw new InvalidOperationException("entry replacement left no stable recording body");
                _deferredStart = false;
                _contract.Start.Invoke(null, new object[] { _world });
            }
            if (_pending && Time.frameCount > _startFrame)
            {
                var same = ReferenceEquals(_world, StandaloneHudScope.CurrentWorld) && ReferenceEquals(_game, StandaloneHudScope.CurrentGame);
                if (same && _game.Status != GameStatus.Started && !_ended && !_retired)
                {
                    if (Time.realtimeSinceStartup - _startTime > StartupTimeout) throw new InvalidOperationException("recording startup ownership timed out");
                    return;
                }
                // IsAvailable alone would incorrectly alias ordinary PMC raids with inactive Arena.
                var owned = _committedLife || (same && RespawnApi.IsAvailable && DeathInterception.FindClaimant(_game) != null);
                _pending = false; _active = owned;
                foreach (var packet in Startup)
                {
                    var value = owned ? packet.Projected : packet.Raw;
                    if (value != null && !Send(packet.Action, value)) throw new InvalidOperationException("startup packet enqueue failed");
                }
                Startup.Clear();
                if (_ended) ReleaseRecording();
            }
            if (_recover && !BodyReplacementContext.IsCreating && _prepared != null)
            {
                if (Exact(_prepared)) AbortPreparation();
                else if (Exact(_world.MainPlayer)) Commit(_world.MainPlayer);
            }
        }
        catch (Exception ex) { FaultRecording(ex); }
    }
    internal static void RetireWorld(GameWorld world)
    {
        if (!ReferenceEquals(world, _world)) return;
        if (!ReferenceEquals(world, null)) RetiredWorlds.GetValue(world, _ => new object());
        _retired = true; _prepared = null; _recover = false;
        // Keep identity and final health evidence until RR sends END, sometimes after Core's
        // GameEnd postfix. A stale world's teardown must never clear a newer recording.
        if (_pending || _deferredStart) { _startFrame--; Tick(); }
        if (_fault || (_session == null && !_deferredStart)) ReleaseRecording();
    }
    private static bool Candidate(Player body)
    {
        if (ReferenceEquals(body, null)) return false;
        if (RejectedCandidates.TryGetValue(body, out _)) return true;
        // A failed candidate may never have reached an RR callback during creation.
        // The Core lease still proves it is an abandoned wrapper after old-body recovery.
        if (_prepared == null && _lives != null && _lives.Find(body) == null && body is LocalPlayer
            && BodyReplacementContext.OwnsBody(body)) return true;
        if (_prepared == null || ReferenceEquals(body, _prepared) || _lives?.Find(body) != null
            || body is not LocalPlayer || !body.IsYourPlayer || !ReferenceEquals(body.GameWorld, _world)) return false;
        var owned = BodyReplacementContext.OwnsBody(body) || (BodyReplacementContext.IsCreating
            && ReferenceEquals(body.Profile, BodyReplacementContext.ExpectedProfile) && BodyReplacementContext.TryOwnCandidate(body));
        if (owned && !AttemptCandidates.Any(w => w.TryGetTarget(out var p) && ReferenceEquals(p, body))) AttemptCandidates.Add(new WeakReference<Player>(body));
        return owned;
    }
    private static bool Block(Player body)
    {
        if (ReferenceEquals(body, null)) return false;
        var life = _lives?.Find(body);
        return Candidate(body) || (life != null && (life.Retired || _ended || _fault))
            || (life == null && KnownBodies.TryGetValue(body, out _));
    }
    private static bool BeforeRoster(Player __0) => !Block(__0);
    private static bool BeforeSessionEnd(Player __0, ExitStatus __1)
    {
        if (Block(__0)) return false;
        // Redeploy persists the dead PMC's statistics before its own-Scav handoff.
        // That is not the end of the physical raid. A real Stop releases the claim
        // before invoking session-end, so the final recording still closes normally.
        return !((_active || _pending) && ReferenceEquals(__0, _lives?.Current?.Body)
            && __1 == ExitStatus.Killed && RespawnApi.IsRespawnPending(__0));
    }
    private static ActorScope Push(Player actor, Player other = null, bool suppress = false)
    {
        var state = new ActorScope { Actor = actor, Other = other, Previous = _actor, Suppressed = suppress };
        _actor = state; return state;
    }
    private static bool BeforeObserve(Player __0, ref bool __result, out ActorScope __state)
    { __state = Push(__0); if (!Block(__0)) return true; __result = false; return false; }
    private static bool BeforeConfirm(Player __0, object __1, out ActorScope __state)
    {
        __state = Push(__0);
        var life = _lives?.Find(__0);
        if (life != null && Convert.ToInt32(__1) == 1) life.Dead = true;
        return !Block(__0);
    }
    private static bool BeforeCapture(Player __0, out ActorScope __state)
    { __state = Push(__0); return !Block(__0); }
    private static bool BeforeLoot(Player __0, out ActorScope __state)
    {
        // Construction/transfer bookkeeping is not ground loot. The fence is lifted as
        // soon as Core commits the body; ordinary post-respawn looting stays observable.
        var suppress = Block(__0) || (_prepared != null && BodyReplacementContext.IsCreating && ReferenceEquals(__0, _prepared));
        __state = Push(__0, suppress: suppress); return !suppress;
    }
    private static void AfterLoot(Player __0, ActorScope __state)
    {
        if (__state?.Suppressed != false || _contract == null || (!_active && !_pending)) return;
        try
        {
            // Only pending entries for this physical actor; no mutation of RR-owned snapshots.
            var entries = (IDictionary)_contract.PendingLoot.GetValue(null);
            var shape = _contract.Schema.Get("LOOT");
            foreach (DictionaryEntry entry in entries)
                if ((string)shape.Profile.GetValue(entry.Value) == __0.ProfileId)
                    LootOrigins.GetValue(entry.Value, _ => new ActorScope { Actor = __0 });
        }
        catch (Exception ex) { FaultRecording(ex); }
    }
    private static bool BeforeKill(Player __0, IPlayer __1, out ActorScope __state)
    {
        __state = Push(__1 as Player, __0);
        var victim = _lives?.Find(__0); if (victim != null) victim.Dead = true;
        return !Candidate(__0) && !Candidate(__1 as Player) && !ForeignOld(__0) && !ForeignOld(__1 as Player);
    }
    private static bool BeforeBallistic(ClientGameWorld __0, Shot __1, out ActorScope __state)
    {
        __state = null;
        if (!ReferenceEquals(__0, null) && RetiredWorlds.TryGetValue(__0, out _)) return false;
        // This wrapper runs before RR's own CanCapture / BallisticsTracking guards.
        // Ordinary recordings retain RR's event-level exception handling; do not read
        // a Unity collider or bridge, or abort their recording, before those guards.
        if (!_active && !_pending) return true;
        __state = Push(null);
        try
        {
            var actor = __1?.Player?.iPlayer as Player;
            var collider = __1?.HittedBallisticCollider;
            var victim = collider == null ? null : collider.gameObject.GetComponentInParent<Player>();
            __state.Actor = actor; __state.Other = victim;
            return !Candidate(actor) && !Candidate(victim) && !ForeignOld(actor) && !ForeignOld(victim);
        }
        catch (Exception ex) { FaultRecording(ex); return false; }
    }
    private static bool ForeignOld(Player body)
        => !ReferenceEquals(body, null) && KnownBodies.TryGetValue(body, out _) && (_lives?.Find(body) == null || _ended || _fault);
    private static void RestoreScope(ActorScope __state) { if (__state != null) _actor = __state.Previous; }
    private static bool BeforeSend(string __0, ref object __1, ref bool __result)
    {
        if (_sending || _contract == null) return true;
        if (_fault) { __result = false; return false; }
        try
        {
            if (__0 == "START" && _world != null && _session == null)
            {
                if (!ReferenceEquals(_contract.World.GetValue(null), _world)) return true;
                var shape = _contract.Schema.Get("START");
                // An entry-kit replacement can run in an earlier OnGameStarted postfix.
                // RR's actual first captured body, not the prefix's body, begins life one.
                _initial = _contract.MyPlayer.GetValue(null) as LocalPlayer;
                if (_initial == null || !ReferenceEquals(_initial.GameWorld, _world)
                    || !ReferenceEquals(_initial, _world.MainPlayer) || !ReferenceEquals(_initial, _game.LocalPlayer))
                    throw new InvalidOperationException("START has no exact local recording body");
                if (string.IsNullOrEmpty((string)shape.Profile.GetValue(__1)))
                    throw new InvalidOperationException("START recording owner is empty");
                _session = (string)shape.Session.GetValue(__1);
                _lives = new RaidReviewLifeLedger(); _lives.Add(_initial); KnownBodies.GetValue(_initial, _ => new object());
                _initial = null;
                _pending = true; _startFrame = Time.frameCount; _startTime = Time.realtimeSinceStartup;
            }
            if (!_active && !_pending)
            {
                // An inactive Arena / ordinary PMC recording is byte-for-byte passthrough,
                // but its accepted END still releases our temporary startup references.
                if (__0 == "END" && _session != null && (string)_contract.Schema.Get("END").Session.GetValue(__1) == _session) _ended = true;
                return true;
            }
            var packetShape = _contract.Schema.Get(__0);
            if (packetShape != null && (string)packetShape.Session.GetValue(__1) != _session)
                throw new InvalidOperationException("packet session changed inside the recording");
            if (_ended) { __result = false; return false; }
            if (__0 == "END")
            {
                var exit = packetShape.Exit.GetValue(__1).ToString();
                var life = _lives.Current;
                var status = life.Dead || life.Body.HealthController?.IsAlive == false ? 1
                    : exit == "Survived" || exit == "Runner" ? 4 : 2;
                EmitStatus(life, status); _ended = true;
            }
            var actor = __0 == "LOOT" && LootOrigins.TryGetValue(__1, out var origin) ? origin : _actor;
            var exact = __0 == "KILL" || __0 == "BALLISTIC" || __0 == "LOOT" || __0 == "PLAYER_INVENTORY";
            var projected = _contract.Schema.Project(__0, __1,
                (id, other) => _lives.Map(id, other ? actor?.Other : actor?.Actor, exact), _lives.Name);
            if (_pending)
            {
                Queue(__0, __1, projected); __result = true; return false;
            }
            __1 = projected;
            return true;
        }
        catch (Exception ex) { FaultRecording(ex); __result = false; return false; }
    }
    private static void AfterSend(string __0, bool __result)
    {
        if (_sending || _fault) return;
        if (__0 == "END" && _ended && !_pending && __result) { ReleaseRecording(); return; }
        if (!_active && !_pending) return;
        if (!__result) { FaultRecording(new InvalidOperationException("recording packet enqueue failed")); return; }
    }
    private static void ReleaseRecording()
    {
        RetireCandidates(null);
        if (!ReferenceEquals(_world, null)) RetiredWorlds.GetValue(_world, _ => new object());
        _world = null; _game = null; _initial = null; _prepared = null; _lives = null;
        _active = false; _recover = false; _deferredStart = false;
    }
    private static void Queue(string action, object raw, object projected)
    {
        if (Startup.Count >= StartupCapacity) throw new InvalidOperationException("startup recording buffer overflow");
        Startup.Add(new Packet { Action = action, Raw = raw, Projected = projected });
    }
    private static void EmitStatus(RaidReviewLifeLedger.Life life, int status)
    {
        life.Dead |= status == 1;
        var value = _contract.Schema.Status(_session, life.Alias, _contract.Now, status);
        if (_pending) Queue("PLAYER_STATUS", null, value);
        else if (_active && !Send("PLAYER_STATUS", value)) throw new InvalidOperationException("final life status enqueue failed");
    }
    private static bool Send(string action, object value)
    {
        var previous = _sending; _sending = true;
        try { return (bool)_contract.Send.Invoke(null, new[] { (object)action, value }); }
        finally { _sending = previous; }
    }
    private static void FaultRecording(Exception error)
    {
        Reject(error, true); Startup.Clear(); _pending = false;
        try { _contract?.Abort("Redeploy body recording failed: " + error.GetBaseException().Message); }
        catch (Exception ex) { CoreLog.LogWarning("Raid Review could not mark its recording incomplete: " + ex.GetBaseException().Message); }
    }
    private static string Reject(Exception ex, bool fault)
    {
        _fault |= fault;
        var message = "Core cannot safely replace the player with Raid Review: " + ex.GetBaseException().Message
            + ". Update or remove Raid Review, then restart. Ordinary raids remain available.";
        if (message != _failure) CoreLog.LogWarning(message);
        return _failure = message;
    }
    private static void Clear()
    {
        // Never enumerate all process Harmony patches on absent-plugin admission polls.
        // Keep the flag raised if partial install or UnpatchSelf throws, so cleanup retries.
        if (_patched) { Patches.UnpatchSelf(); _patched = false; }
        _contract = null; _plugin = null; _version = null;
    }
}

internal sealed class RaidReviewWorldPump : MonoBehaviour
{
    internal GameWorld World;
    private void Update() { if (ReferenceEquals(World, StandaloneHudScope.CurrentWorld)) RaidReviewCompat.Tick(); }
    private void OnDestroy() => RaidReviewCompat.RetireWorld(World);
}

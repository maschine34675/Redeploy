using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using EFT;
using Redeploy.Core.Api;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Retain ORBIT's original shared list while moving its local-human entry to Core's committed body.</summary>
internal static class OrbitCompat
{
    internal const string PluginGuid = OrbitContract.PluginGuid;
    private static OrbitContract _contract;
    private static object _plugin;
    private static Version _version;
    private static string _guid, _failure, _warning;
    private static bool _subscribed, _recover;
    private static LocalGame _game;
    private static GameWorld _world;
    private static LocalPlayer _oldBody;
    private static object _manager;
    private static List<Player> _humans;

    internal static string GetRejectionReason()
    {
        if (!Chainloader.PluginInfos.TryGetValue(OrbitContract.PluginGuid, out var info))
        {
            AbortPreparation(); _contract = null; _plugin = null; _version = null; _guid = null; _failure = null;
            return null;
        }
        if (!_subscribed)
        {
            RespawnApi.Replaced += Replaced;
            RespawnApi.ReplacementFailed += Failed;
            _subscribed = true;
        }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            var version = OrbitContract.Normalize(info.Metadata?.Version);
            var guid = info.Metadata?.GUID;
            if (ReferenceEquals(info.Instance, _plugin) && version == _version && guid == _guid) return _failure;
            if (!ReferenceEquals(_world, null)) throw new InvalidOperationException("ORBIT changed during a prepared replacement");
            // Cache both accepted and rejected shapes for this plugin identity.
            _plugin = info.Instance; _version = version; _guid = guid; _contract = null;
            _contract = OrbitContract.Resolve(info.Instance.GetType(), guid, version);
            _failure = null;
            if (version > OrbitContract.ReviewedVersion)
                CoreLog.LogWarning("ORBIT " + version + " is newer than reviewed 3.1.1; APIs match, native compatibility is unverified.");
        }
        catch (Exception ex) { Fault(ex); }
        return _failure;
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        var rejection = GetRejectionReason();
        if (rejection != null) throw new InvalidOperationException(rejection);
        if (_contract == null) return;
        if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, oldBody) || !BodyReplacementContext.OwnsBody(oldBody)
            || !ReferenceEquals(game.LocalPlayer, oldBody) || !ReferenceEquals(game.GameWorld.MainPlayer, oldBody))
            throw new InvalidOperationException("ORBIT requires Core's exact old body");
        AbortPreparation();
        var manager = _contract.ManagerInstance.GetValue(null);
        var humans = ReadHumans(game, manager);
        if (humans != null && ExactIndex(humans, oldBody) < 0)
            throw new InvalidOperationException("ORBIT human cache does not contain the exact old body");
        // Preparation is read-only. An absent manager can still initialize natively later.
        _game = game; _world = game.GameWorld; _oldBody = oldBody; _manager = manager; _humans = humans;
    }

    private static List<Player> ReadHumans(LocalGame game, object manager)
    {
        if (manager == null) return null;
        var bots = ((IBotGame)game).BotsController;
        if (bots == null || !ReferenceEquals(_contract.Bots.GetValue(manager), bots))
            throw new InvalidOperationException("ORBIT manager belongs to another bot controller");
        var movement = _contract.Movement.GetValue(manager)
            ?? throw new InvalidOperationException("ORBIT movement system is unavailable");
        var waypoints = _contract.Waypoints.GetValue(manager)
            ?? throw new InvalidOperationException("ORBIT waypoint system is unavailable");
        var stuck = _contract.Stuck.GetValue(movement)
            ?? throw new InvalidOperationException("ORBIT stuck remediation is unavailable");
        var hard = _contract.HardStuck.GetValue(stuck)
            ?? throw new InvalidOperationException("ORBIT hard-stuck remediation is unavailable");
        var humans = _contract.MovementHumans.GetValue(movement) as List<Player>
            ?? throw new InvalidOperationException("ORBIT human cache is unavailable");
        if (!ReferenceEquals(humans, _contract.WaypointHumans.GetValue(waypoints))
            || !ReferenceEquals(humans, _contract.HardHumans.GetValue(hard)))
            throw new InvalidOperationException("ORBIT systems no longer share one human cache");
        return humans;
    }

    private static int ExactIndex(List<Player> humans, Player body)
    {
        int index = -1;
        for (int i = 0; i < humans.Count; i++)
            if (ReferenceEquals(humans[i], body))
            {
                if (index >= 0) throw new InvalidOperationException("ORBIT contains duplicate physical human entries");
                index = i;
            }
        return index;
    }

    private static bool ExactCurrent(Player body)
        => body != null && body.IsYourPlayer && body.HealthController?.IsAlive == true
            && BodyReplacementContext.OwnsRaid(_game, _world) && BodyReplacementContext.OwnsBody(body)
            && ReferenceEquals(body.GameWorld, _world) && ReferenceEquals(body, _world?.MainPlayer)
            && ReferenceEquals(body, _game?.LocalPlayer) && ReferenceEquals(body, GamePlayerOwner.MyPlayer)
            && _world.RegisteredPlayers.Contains(body);

    private static void Replaced(Player oldBody, Player body, string reason)
    {
        if (_contract == null || _failure != null || ReferenceEquals(_oldBody, null) || !ReferenceEquals(oldBody, _oldBody)) return;
        try { Synchronize(body); } catch (Exception ex) { Fault(ex); }
    }

    private static void Failed(Player oldBody, string stage)
    {
        if (!ReferenceEquals(oldBody, null) && ReferenceEquals(oldBody, _oldBody)) _recover = true;
    }

    internal static void Tick()
    {
        if (ReferenceEquals(_world, null) || _failure != null) return;
        if (!BodyReplacementContext.OwnsRaid(_game, _world)) { AbortPreparation(); return; }
        if (!_recover || BodyReplacementContext.IsCreating) return;
        try { Synchronize(_world.MainPlayer); } catch (Exception ex) { Fault(ex); }
    }

    private static void Synchronize(Player body)
    {
        if (ReferenceEquals(body, _oldBody) || !ExactCurrent(body)) return;
        // A plugin can disappear/change between admission and publication. Never use its old cache then.
        if (GetRejectionReason() != null || _contract == null || ReferenceEquals(_world, null)) return;
        var manager = _contract.ManagerInstance.GetValue(null);
        if (_manager != null && !ReferenceEquals(manager, _manager))
            throw new InvalidOperationException("ORBIT manager changed during replacement");
        var humans = ReadHumans(_game, manager);
        if (_humans != null && !ReferenceEquals(humans, _humans))
            throw new InvalidOperationException("ORBIT human cache changed during replacement");
        if (humans != null)
        {
            int oldIndex = ExactIndex(humans, _oldBody);
            int newIndex = ExactIndex(humans, body);
            if (oldIndex >= 0 && newIndex < 0)
                humans[oldIndex] = body;
            else if (oldIndex >= 0)
                throw new InvalidOperationException("ORBIT human cache contains both replacement bodies");
            else if (newIndex < 0)
            {
                if (_manager != null) throw new InvalidOperationException("ORBIT human cache ownership changed during replacement");
                // A manager initialized between teardown and publication may have captured no local human.
                humans.Add(body);
            }
        }
        // An absent manager will initialize from the current world, without any synthetic raid reset.
        AbortPreparation();
    }

    internal static void AbortPreparation()
    {
        _game = null; _world = null; _oldBody = null; _manager = null; _humans = null; _recover = false;
    }

    internal static void RetireWorld(GameWorld world)
    {
        if (ReferenceEquals(world, _world)) AbortPreparation();
    }

    private static void Fault(Exception ex)
    {
        _failure = "ORBIT body replacement compatibility failed: " + ex.GetBaseException().Message + ".";
        if (_warning != _failure) { _warning = _failure; CoreLog.LogWarning(_failure); }
    }
}

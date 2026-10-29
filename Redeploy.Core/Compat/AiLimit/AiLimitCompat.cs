using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using EFT;
using Redeploy.Core.Api;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Move AI Limit's distance origin to the committed body without resetting any bot.</summary>
internal static class AiLimitCompat
{
    private static AiLimitContract _contract;
    private static object _plugin;
    private static Version _version;
    private static bool _subscribed, _newerWarned, _recover;
    private static string _failure, _warning;
    private static LocalGame _game;
    private static GameWorld _world;
    private static LocalPlayer _oldBody;
    private static Component _component;
    private static List<Player> _players;

    internal static string GetRejectionReason()
    {
        if (!_subscribed)
        {
            RespawnApi.Replaced += Replaced;
            RespawnApi.ReplacementFailed += Failed;
            _subscribed = true;
        }
        if (!Chainloader.PluginInfos.TryGetValue(AiLimitContract.PluginGuid, out var info))
        {
            AbortPreparation(); _contract = null; _plugin = null; _version = null; _failure = null;
            return null;
        }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            var version = AiLimitContract.Normalize(info.Metadata?.Version);
            if (ReferenceEquals(info.Instance, _plugin) && version == _version) return _failure;
            if (_world != null) throw new InvalidOperationException("AI Limit changed during a prepared replacement");
            _contract = AiLimitContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, version, out version);
            _plugin = info.Instance; _version = version; _failure = null;
            if (version > AiLimitContract.ReviewedVersion && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("AI Limit " + version + " is newer than reviewed 1.9.2; APIs match, native compatibility is unverified.");
            }
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
            throw new InvalidOperationException("AI Limit requires Core's exact old body");
        AbortPreparation();
        var component = FindComponent(game.GameWorld);
        var players = ReadPlayers(game.GameWorld, component);
        if (players != null && players.Count != 0 && ExactIndex(players, oldBody) < 0)
            throw new InvalidOperationException("AI Limit human cache does not contain the exact old body");
        // Preparation is read-only. Native Start may still run before publication.
        _game = game; _world = game.GameWorld; _oldBody = oldBody;
        _component = component; _players = players;
    }

    private static Component FindComponent(GameWorld world)
    {
        var components = world.gameObject.GetComponents(_contract.ComponentType);
        if (components.Length > 1) throw new InvalidOperationException("multiple AI Limit components belong to this world");
        return components.Length == 0 ? null : components[0];
    }
    private static List<Player> ReadPlayers(GameWorld world, Component component)
    {
        if (component == null) return null;
        if (!ReferenceEquals(_contract.World.GetValue(null), world))
            throw new InvalidOperationException("AI Limit component belongs to another world");
        return _contract.Players.GetValue(component) as List<Player>
            ?? throw new InvalidOperationException("AI Limit human cache is unavailable");
    }
    private static int ExactIndex(List<Player> players, Player body)
    {
        int index = -1;
        for (int i = 0; i < players.Count; i++)
            if (ReferenceEquals(players[i], body))
            {
                if (index >= 0) throw new InvalidOperationException("AI Limit contains duplicate physical human entries");
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
        if (_contract == null || _failure != null || !ReferenceEquals(oldBody, _oldBody)) return;
        try { Synchronize(body); } catch (Exception ex) { Fault(ex); }
    }
    private static void Failed(Player oldBody, string stage)
    {
        if (!ReferenceEquals(oldBody, null) && ReferenceEquals(oldBody, _oldBody)) _recover = true;
    }
    internal static void Tick()
    {
        if (_world == null || _failure != null) return;
        if (!BodyReplacementContext.OwnsRaid(_game, _world)) { AbortPreparation(); return; }
        if (!_recover || BodyReplacementContext.IsCreating) return;
        try { Synchronize(_world.MainPlayer); } catch (Exception ex) { Fault(ex); }
    }
    private static void Synchronize(Player body)
    {
        if (ReferenceEquals(body, _oldBody) || !ExactCurrent(body)) return;
        var component = FindComponent(_world);
        if (!ReferenceEquals(_component, null) && !ReferenceEquals(component, _component))
            throw new InvalidOperationException("AI Limit component changed during replacement");
        var players = ReadPlayers(_world, component);
        if (_players != null && !ReferenceEquals(players, _players))
            throw new InvalidOperationException("AI Limit human cache changed during replacement");
        if (players != null && players.Count != 0)
        {
            int oldIndex = ExactIndex(players, _oldBody);
            int newIndex = ExactIndex(players, body);
            if (oldIndex >= 0 && newIndex < 0)
                players[oldIndex] = body;
            else if (oldIndex >= 0 || newIndex < 0)
                throw new InvalidOperationException("AI Limit human cache ownership changed during replacement");
        }
        // Empty/missing components have not run Start yet; let native initialization fill the cache.
        AbortPreparation();
    }
    internal static void AbortPreparation()
    {
        _game = null; _world = null; _oldBody = null; _component = null; _players = null; _recover = false;
    }
    internal static void RetireWorld(GameWorld world)
    {
        if (ReferenceEquals(world, _world)) AbortPreparation();
    }
    private static void Fault(Exception ex)
    {
        _failure = "AI Limit body replacement compatibility failed: " + ex.GetBaseException().Message + ".";
        if (_warning != _failure) { _warning = _failure; CoreLog.LogWarning(_failure); }
    }
}

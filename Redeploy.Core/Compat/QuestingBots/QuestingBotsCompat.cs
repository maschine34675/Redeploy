using System;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using EFT;
using HarmonyLib;
using Redeploy.Core.Api;
using Redeploy.Core.Engine;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal static class QuestingBotsCompat
{
    internal const string PluginGuid = "com.danw.questingbots";
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.QuestingBots");
    private static readonly ConditionalWeakTable<Player, Retirement> Retired = new();
    private sealed class Retirement
    {
        internal readonly Component Monitor;
        internal readonly GameWorld World;
        internal QuestingIslandState State;
        internal Action<bool> Restore;
        internal Retirement(Component monitor, GameWorld world, QuestingIslandState state, Action<bool> restore)
        { Monitor = monitor; World = world; State = state; Restore = restore; }
    }
    private static object _plugin;
    private static Version _version;
    private static string _failure, _guid;
    private static bool _patched, _subscribed;
    private static QuestingBotsContract _contract;
    private static LocalGame _game;
    private static GameWorld _world;
    private static LocalPlayer _body;
    private static Component _monitor;
    private static QuestingIslandState _state;

    internal static string GetRejectionReason()
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info)) return null;
        if (info?.Instance == null) return "Questing Bots has not finished loading.";
        var version = StandaloneHudContract.Normalize(info.Metadata?.Version);
        if (ReferenceEquals(_plugin, info.Instance) && Equals(_version, version) && _guid == info.Metadata?.GUID) return _failure;
        if (_plugin != null) return _failure = "Questing Bots changed identity during this process.";
        _plugin = info.Instance;
        _version = version;
        _guid = info.Metadata?.GUID;
        try
        {
            var contract = new QuestingBotsContract(info.Instance.GetType(), info.Metadata?.GUID, version);
            var unregister = StandaloneHudContract.Method(typeof(GameWorld), nameof(GameWorld.UnregisterPlayer),
                typeof(void), false, typeof(IPlayer));
            var clientUnregister = StandaloneHudContract.Method(typeof(ClientGameWorld), nameof(GameWorld.UnregisterPlayer),
                typeof(void), false, typeof(IPlayer));
            _patched = true;
            Patches.Patch(unregister, prefix: new HarmonyMethod(typeof(QuestingBotsCompat), nameof(BeforeUnregister)) { priority = Priority.First });
            // ClientLocalGameWorld inherits this override. It skips base entirely
            // when death already unregistered the old body before Core's request.
            Patches.Patch(clientUnregister, prefix: new HarmonyMethod(typeof(QuestingBotsCompat), nameof(BeforeUnregister)) { priority = Priority.First });
            Patches.Patch(contract.CheckPlayer, prefix: new HarmonyMethod(typeof(QuestingBotsCompat), nameof(BeforeIslandCheck)) { priority = Priority.First });
            _contract = contract;
            if (!_subscribed) { RespawnApi.Replaced += Replaced; _subscribed = true; }
            if (version > QuestingBotsContract.Reviewed)
                CoreLog.LogWarning("Questing Bots " + version + " is newer than reviewed 1.2.0; matching APIs, unverified gameplay.");
        }
        catch (Exception ex)
        {
            if (_patched) { Patches.UnpatchSelf(); _patched = false; }
            _contract = null;
            _failure = "Questing Bots body replacement compatibility failed: " + ex.GetBaseException().Message;
            CoreLog.LogWarning(_failure);
        }
        return _failure;
    }

    internal static void Prepare(LocalGame game, LocalPlayer body)
    {
        AbortPreparation();
        var rejection = GetRejectionReason();
        if (rejection != null) throw new InvalidOperationException(rejection);
        if (!Chainloader.PluginInfos.ContainsKey(PluginGuid)) return;
        if (_contract == null || _failure != null) return;
        RequireOutgoing(game, body);
        var monitors = game.GameWorld.gameObject.GetComponents(_contract.Monitor);
        if (monitors.Length == 0) return; // Non-Lighthouse or disabled monitor.
        if (monitors.Length != 1) throw new InvalidOperationException("Questing Bots has more than one island monitor.");
        var state = _contract.Read(monitors[0], game.GameWorld);
        // Preparation is read-only. A later adapter's refusal must leave even a
        // living outgoing player's island membership and alliances unchanged.
        _game = game; _world = game.GameWorld; _body = body; _monitor = monitors[0]; _state = state;
    }

    private static void RequireOutgoing(LocalGame game, LocalPlayer body)
    {
        if (!OwnsOutgoing(game, body))
            throw new InvalidOperationException("Questing Bots requires Core's exact outgoing local body.");
    }

    private static bool OwnsOutgoing(LocalGame game, LocalPlayer body)
        => body != null && !body.IsAI && BodyReplacementContext.IsCreating
           && BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
           && ReferenceEquals(BodyReplacementContext.PreviousBody, body) && BodyReplacementContext.OwnsBody(body)
           && ReferenceEquals(game.LocalPlayer, body) && ReferenceEquals(game.GameWorld.MainPlayer, body);

    private static void BeforeUnregister(GameWorld __instance, IPlayer __0)
    {
        if (_state == null || !ReferenceEquals(__instance, _world) || !ReferenceEquals(__0, _body)) return;
        if (!RespawnEngineState.IsPipelineRunning || !OwnsOutgoing(_game, _body)
            || !Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info) || !ReferenceEquals(info?.Instance, _plugin)
            || info.Metadata?.GUID != _guid || StandaloneHudContract.Normalize(info.Metadata?.Version) != _version)
        {
            AbortPreparation();
            return; // Real raid teardown or an unrelated unregister remains native.
        }
        if (!_state.SameCollections(_contract.Read(_monitor, _world)))
            throw new InvalidOperationException("Questing Bots island state changed during preparation.");
        // The engine has begun teardown, but no replacement has registered. This
        // prefix also works when death already unregistered the physical old body.
        var restoreSnapshots = _state.Retire(_body);
        Retired.Remove(_body);
        Retired.Add(_body, new Retirement(_monitor, _world, restoreSnapshots == null ? null : _state, restoreSnapshots));
        AbortPreparation();
    }

    private static void Replaced(Player oldBody, Player body, string reason)
    {
        if (ReferenceEquals(oldBody, null) || ReferenceEquals(body, null) || ReferenceEquals(oldBody, body)
            || !Retired.TryGetValue(oldBody, out var retired)
            || retired.State == null || !BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsBody(body)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, oldBody) || !ReferenceEquals(body.GameWorld, retired.World)
            || !BodyReplacementContext.OwnsRaid(BodyReplacementContext.CurrentGame, retired.State.World)
            || !ReferenceEquals(BodyReplacementContext.CurrentGame.LocalPlayer, body)
            || !ReferenceEquals(retired.State.World.MainPlayer, body)) return;
        // The committed successor can no longer need old-body rollback arrays.
        retired.State = null;
        retired.Restore = null;
    }

    private static bool BeforeIslandCheck(Component __instance, Player __0, ref bool __result)
    {
        if (ReferenceEquals(__0, null) || !Retired.TryGetValue(__0, out var retired)
            || !ReferenceEquals(retired.Monitor, __instance)) return true;
        var game = BodyReplacementContext.CurrentGame;
        var world = __0.GameWorld;
        // If a failed replacement explicitly recovers and registers the old live
        // physical body, native location assessment may adopt it afresh.
        if (!BodyReplacementContext.IsCreating && __0.HealthController?.IsAlive == true
            && ReferenceEquals(world, retired.World) && BodyReplacementContext.OwnsBody(__0)
            && BodyReplacementContext.OwnsRaid(game, world) && ReferenceEquals(game.LocalPlayer, __0)
            && ReferenceEquals(world.MainPlayer, __0) && world.RegisteredPlayers.Any(player => ReferenceEquals(player, __0)))
        {
            if (_contract != null && retired.State != null && retired.State.SameCollections(_contract.Read(__instance, world)))
                retired.Restore(!_contract.IsOnIsland(__instance, __0));
            Retired.Remove(__0);
            return true;
        }
        __result = false;
        return false; // A delayed scan cannot put the corpse back on the island.
    }

    internal static void AbortPreparation()
    {
        _game = null; _world = null; _body = null; _monitor = null; _state = null;
    }

    internal static void Tick()
    {
        if (_state != null && (!BodyReplacementContext.IsCreating
            || !BodyReplacementContext.OwnsRaid(_game, _world))) AbortPreparation();
    }

    internal static void RetireWorld(GameWorld world)
    {
        if (ReferenceEquals(world, _world)) AbortPreparation();
        // Weak retirement fences survive delayed callbacks; no world or bot reset.
    }
}

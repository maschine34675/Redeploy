using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Redeploy.Core.Api;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Retire Cola's physical-body state only at an admitted Core replacement.</summary>
internal static class TarkovaColaBodyCompatibility
{
    internal const string PluginGuid = "com.tarkovacola.server";
    private static readonly Version Reviewed = new(4, 0, 0, 0);
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.TarkovaColaCompat");
    private static readonly List<(MethodInfo Target, MethodInfo Hook)> Targets = new();
    private static TarkovaColaBodyContract _contract;
    private static object _plugin;
    private static Version _version;
    private static LocalGame _game, _previousGame;
    private static GameWorld _world, _previousWorld;
    private static LocalPlayer _outgoing, _active;
    private static string _failure, _warning;
    private static bool _subscribed, _newerWarned;

    internal static string GetRejectionReason()
    {
        if (!_subscribed) { _subscribed = true; RespawnApi.Replaced += Replaced; }
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info))
        { Clear(); return _failure = null; }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            var version = Normalize(info.Metadata?.Version);
            if (version == null) throw new InvalidOperationException("plugin version is unavailable");
            // The new downed/third-weapon state does not exist in the previously supported 3.x family.
            if (version < Reviewed) { Clear(); return _failure = null; }
            if (_contract != null && ReferenceEquals(_plugin, info.Instance) && version == _version)
            { VerifyHooks(); return _failure; }
            if (_outgoing != null || _active != null)
                throw new InvalidOperationException("plugin changed during an owned raid");
            Clear();
            var type = info.Instance.GetType();
            var metadata = type.GetCustomAttribute<BepInPlugin>(false);
            if (type.FullName != "TarkovaCola.Client.Plugin" || type.Assembly.GetName().Name != "TarkovaCola.Client"
                || !typeof(BaseUnityPlugin).IsAssignableFrom(type) || info.Metadata.GUID != PluginGuid
                || metadata?.GUID != PluginGuid || Normalize(metadata.Version) != version)
                throw new InvalidOperationException("plugin identity or metadata changed");
            var contract = new TarkovaColaBodyContract(type.Assembly);
            Patch(contract.TrackerTick, nameof(BeforeTracker));
            Patch(contract.IsMe, nameof(BeforeIsMe));
            Patch(contract.DownedProne, nameof(BeforeDownedProne));
            _contract = contract; _plugin = info.Instance; _version = version;
            VerifyHooks(); _failure = null;
            CoreLog.LogDebug("Tarkova-Cola: physical-body transient replacement adapter ready.");
            if (version > Reviewed && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("Tarkova-Cola " + version + " is newer than reviewed 4.0.0; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            // If a hook is removed after a successful replacement, keep the other guards
            // installed. Their failure latch still fences the owned body's stale state.
            if (ReferenceEquals(_active, null) && ReferenceEquals(_outgoing, null)) Clear();
            Reject(ex);
        }
        return _failure;
    }

    internal static void Prepare(LocalGame game, LocalPlayer body)
    {
        if (_contract == null) return;
        if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, body) || !BodyReplacementContext.OwnsBody(body)
            || !ReferenceEquals(game.LocalPlayer, body) || !ReferenceEquals(game.GameWorld.MainPlayer, body))
            throw new InvalidOperationException("Tarkova-Cola requires Core's exact outgoing body");
        _previousGame = _game; _previousWorld = _world;
        _game = game; _world = game.GameWorld; _outgoing = body;
        // Admission is read-only; a later adapter may still cancel before teardown.
    }

    internal static void AbortPreparation()
    {
        if (ReferenceEquals(_outgoing, null)) return;
        _game = _previousGame; _world = _previousWorld;
        _outgoing = null; _previousGame = null; _previousWorld = null;
    }

    internal static void EndAttempt()
    {
        try
        {
            // A failed rewire may already have committed the successor without a Replaced event.
            if (!ReferenceEquals(_outgoing, null)) Commit(_outgoing, _game?.LocalPlayer as LocalPlayer);
        }
        catch (Exception ex) { Reject(ex); }
        finally
        {
            if (_active == null) { _active = null; _game = null; _world = null; }
            _outgoing = null; _previousGame = null; _previousWorld = null;
        }
    }

    private static void Replaced(Player oldBody, Player newBody, string reason)
    {
        if (!ReferenceEquals(oldBody, _outgoing)) return;
        try { Commit(oldBody as LocalPlayer, newBody as LocalPlayer); }
        catch (Exception ex) { Reject(ex); }
    }

    private static void Commit(LocalPlayer oldBody, LocalPlayer newBody)
    {
        if (_contract == null || ReferenceEquals(oldBody, null) || newBody == null || ReferenceEquals(oldBody, newBody)
            || ReferenceEquals(_active, newBody) || !ReferenceEquals(oldBody, _outgoing)
            || !BodyReplacementContext.OwnsRaid(_game, _world) || !BodyReplacementContext.OwnsBody(newBody)
            || !ReferenceEquals(newBody.GameWorld, _world) || !newBody.IsYourPlayer
            || !ReferenceEquals(_game.LocalPlayer, newBody) || !ReferenceEquals(_world.MainPlayer, newBody)) return;
        _contract.RetireBodyState(newBody);
        _active = newBody;
        CoreLog.LogDebug("Tarkova-Cola: retired outgoing downed, hands and transient perk state; raid progress retained.");
    }

    internal static void RetireWorld(GameWorld world)
    {
        if (ReferenceEquals(world, null) || !ReferenceEquals(world, _world)) return;
        try
        {
            // Do not clear a subsequent ordinary raid's process-global Cola state on a late old-world event.
            if (_contract != null && Singleton<GameWorld>.Instantiated
                && ReferenceEquals(Singleton<GameWorld>.Instance, _world)
                && ReferenceEquals(BodyReplacementContext.CurrentWorld, _world)
                && (ReferenceEquals(_world.MainPlayer, _active) || ReferenceEquals(_world.MainPlayer, _outgoing)))
                _contract.RetireBodyState(null);
        }
        catch (Exception ex) { Reject(ex); }
        finally
        {
            _game = null; _world = null; _outgoing = null; _active = null;
            _previousGame = null; _previousWorld = null;
        }
    }

    // Called by the foreign mod, not a new Update pump. No reflection, registry/scene scan or
    // allocation on the hot path; an unowned ordinary body retains the foreign implementation.
    private static bool BeforeTracker()
    {
        if (_contract == null) return true;
        var game = BodyReplacementContext.CurrentGame;
        var world = BodyReplacementContext.CurrentWorld;
        if (world == null || !Singleton<GameWorld>.Instantiated || !ReferenceEquals(Singleton<GameWorld>.Instance, world)) return true;
        var body = world?.MainPlayer;
        if (!BodyReplacementContext.OwnsBody(body) && !BodyReplacementContext.TryOwnCandidate(body)) return true;
        return _failure == null && !BodyReplacementContext.IsCreating && BodyReplacementContext.OwnsRaid(game, world)
            && ReferenceEquals(game.LocalPlayer, body) && ReferenceEquals(world.MainPlayer, body);
    }

    private static bool BeforeIsMe(Player __0, ref bool __result)
    {
        if (_contract == null || (!BodyReplacementContext.OwnsBody(__0) && !BodyReplacementContext.TryOwnCandidate(__0))) return true;
        var game = BodyReplacementContext.CurrentGame;
        var world = BodyReplacementContext.CurrentWorld;
        __result = _failure == null && !BodyReplacementContext.IsCreating && BodyReplacementContext.OwnsRaid(game, world)
            && ReferenceEquals(game.LocalPlayer, __0) && ReferenceEquals(world.MainPlayer, __0);
        return false;
    }

    private static bool BeforeDownedProne(ref bool __result)
    {
        // Cola's prone guard reads Plugin.Me, which changes before the engine commits LocalPlayer.
        // It must not force the new body's construction into the outgoing body's downed pose.
        if (_contract == null || !BodyReplacementContext.IsCreating
            || !BodyReplacementContext.OwnsRaid(BodyReplacementContext.CurrentGame, BodyReplacementContext.CurrentWorld)) return true;
        var body = BodyReplacementContext.CurrentWorld?.MainPlayer;
        if (!BodyReplacementContext.OwnsBody(body) && !BodyReplacementContext.TryOwnCandidate(body)) return true;
        __result = true;
        return false;
    }

    private static void Patch(MethodInfo target, string hookName)
    {
        var hook = typeof(TarkovaColaBodyCompatibility).GetMethod(hookName, BindingFlags.Static | BindingFlags.NonPublic);
        Targets.Add((target, hook));
        Patches.Patch(target, prefix: new HarmonyMethod(hook) { priority = Priority.First });
    }
    private static void VerifyHooks()
    {
        foreach (var entry in Targets)
            if (Harmony.GetPatchInfo(entry.Target)?.Prefixes.Count(p => p.owner == Patches.Id && p.PatchMethod == entry.Hook) != 1)
                throw new MissingMethodException("Tarkova-Cola body-owner hook is missing");
    }
    private static void Clear()
    {
        foreach (var entry in Targets) Patches.Unpatch(entry.Target, entry.Hook);
        Targets.Clear(); _contract = null; _plugin = null; _version = null;
        _game = null; _world = null; _outgoing = null; _active = null;
        _previousGame = null; _previousWorld = null;
    }
    private static void Reject(Exception ex)
    {
        _failure = "Tarkova-Cola body replacement compatibility failed: " + ex.GetBaseException().Message + ".";
        if (_warning != _failure) { _warning = _failure; CoreLog.LogWarning(_failure); }
    }
    private static Version Normalize(Version v) => v == null || v.Build < 0 ? null
        : new Version(v.Major, v.Minor, v.Build, Math.Max(0, v.Revision));
}

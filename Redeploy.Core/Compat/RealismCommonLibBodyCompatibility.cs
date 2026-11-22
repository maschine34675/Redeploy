using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using EFT;
using HarmonyLib;
using Redeploy.Core.Api;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Retire only CommonLib controllers belonging to Core's outgoing physical body.</summary>
internal static class RealismCommonLibBodyCompatibility
{
    private sealed class Lease
    {
        internal Player Body;
        internal Behaviour[] Controllers;
        internal bool[] Enabled;
        internal bool Active = true, Detached;
    }
    private static readonly ConditionalWeakTable<object, Lease> Controllers = new();
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.RealismCommonLibCompat");
    private static readonly List<(MethodInfo Target, MethodInfo Hook, int Kind)> Targets = new();
    private static RealismCommonLibContract _contract;
    private static object _plugin;
    private static Version _version;
    private static LocalGame _game, _previousGame;
    private static GameWorld _world, _previousWorld;
    private static Lease _active, _outgoing, _candidate;
    private static Player _initializing;
    private static bool _subscribed;
    private static string _failure, _warning;

    internal static string GetRejectionReason()
    {
        if (!_subscribed) { _subscribed = true; RespawnApi.Replaced += Replaced; }
        if (!Chainloader.PluginInfos.TryGetValue(RealismCommonLibContract.PluginGuid, out var info))
        { if (_contract != null) Clear(); return _failure = null; }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            var version = RealismCommonLibContract.Normalize(info.Metadata?.Version);
            if (_contract != null && ReferenceEquals(_plugin, info.Instance) && version == _version)
            { VerifyHooks(); return _failure; }
            if (_outgoing != null || _active != null) throw new InvalidOperationException("plugin changed during an owned raid");
            Clear();
            _contract = new RealismCommonLibContract(info.Instance.GetType(), info.Metadata.GUID, version);
            Add(_contract.Init, nameof(BeforeInit)); Add(_contract.Init, nameof(AfterInit), 2);
            foreach (var target in _contract.InstanceCallbacks) Add(target, nameof(BeforeInstance));
            foreach (var target in _contract.DestroyCallbacks) Add(target, nameof(BeforeDestroy));
            foreach (var target in _contract.BodyCallbacks)
                Add(target, target.DeclaringType.Name == "PlayerInitPatch" ? nameof(BeforeInitEvent)
                    : target.ReturnType == typeof(bool) ? nameof(BeforeBooleanCallback) : nameof(BeforeBodyCallback));
            _plugin = info.Instance; _version = version; VerifyHooks(); _failure = null;
            if (version > RealismCommonLibContract.Reviewed)
                CoreLog.LogWarning("Realism CommonLib " + version + " is newer than reviewed 0.1.0; APIs match, native compatibility is unverified.");
            CoreLog.LogDebug("Realism CommonLib: physical controller replacement adapter ready.");
        }
        catch (Exception ex) { if (_active == null && _outgoing == null) Clear(); Reject(ex); }
        return _failure;
    }

    internal static void Prepare(LocalGame game, LocalPlayer body)
    {
        if (_contract == null) return;
        if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, body)
            || !ReferenceEquals(game.LocalPlayer, body) || !ReferenceEquals(game.GameWorld.MainPlayer, body))
            throw new InvalidOperationException("CommonLib requires Core's exact outgoing body");
        var components = _contract.Capture(body);
        _previousGame = _game; _previousWorld = _world;
        _outgoing = Capture(body, components); _candidate = null;
        _outgoing.Enabled = components.Select(c => c.enabled).ToArray();
        _game = game; _world = game.GameWorld;
        // Read-only until the native factory actually creates replacement controllers.
    }

    private static Lease Capture(Player body, Behaviour[] components)
    {
        if (components[0] != null && Controllers.TryGetValue(components[0], out var prior))
        {
            if (!prior.Controllers.SequenceEqual(components))
                throw new InvalidOperationException("CommonLib changed members of an already owned controller bundle");
            return prior;
        }
        var lease = new Lease { Body = body, Controllers = components, Enabled = components.Select(c => c != null && c.enabled).ToArray() };
        foreach (var component in components) if (!ReferenceEquals(component, null)) Controllers.Add(component, lease);
        return lease;
    }

    private static bool BeforeInit(Player __0)
    {
        if (_contract == null || _outgoing == null) return !BodyReplacementContext.OwnsBody(__0);
        if (ReferenceEquals(__0, _outgoing.Body)) return false; // Duplicate init must not replace the saved bundle.
        if (!BodyReplacementContext.TryOwnCandidate(__0)) return AllowsUnownedInit(__0);
        if (_candidate != null) return ReferenceEquals(_candidate.Body, __0) ? false : !BodyReplacementContext.OwnsBody(__0);
        _initializing = __0;
        Suspend(_outgoing);
        return true;
    }

    private static Exception AfterInit(Player __0, Exception __exception)
    {
        if (!ReferenceEquals(_initializing, __0)) return __exception;
        try
        {
            if (__exception != null) throw new InvalidOperationException("native controller initialization failed", __exception);
            _candidate = Capture(__0, _contract.Capture(__0));
        }
        catch (Exception ex)
        {
            // A foreign Awake/init exception may leave only part of the bundle allocated.
            // Capture only exact candidate components so rollback also detaches their handlers.
            _candidate = Capture(__0, _contract.Capture(__0, partial: true));
            Suspend(_candidate); Reject(ex);
        }
        finally { _initializing = null; }
        return __exception;
    }

    private static bool BeforeInstance(object __instance)
        => !Controllers.TryGetValue(__instance, out var lease) || lease.Active;
    private static bool BeforeDestroy(object __instance)
    {
        if (!Controllers.TryGetValue(__instance, out var lease)) return true;
        // Original Gear.OnDestroy uses mutable Plugin.WeaponStateInstance. Remove its exact
        // subscriptions from the captured publisher instead; a delayed corpse cannot affect new state.
        // A cancelled Prepare only observed the bundle. If a mod subsequently destroys one
        // still-current component, preserve its ordinary cleanup without retiring its siblings.
        try
        {
            if (lease.Active && ReferenceEquals(_contract.Globals[1].GetValue(null), lease.Controllers[1])) return true;
            Retire(lease);
        }
        catch (Exception ex) { Reject(ex); }
        return false;
    }
    private static bool BeforeBodyCallback(object __0) => Allows(_contract?.Owner(__0));
    private static bool BeforeInitEvent(Player __0)
        => _outgoing != null && BodyReplacementContext.TryOwnCandidate(__0)
            ? _candidate == null : AllowsUnownedInit(__0);
    private static bool AllowsUnownedInit(Player body)
        => !BodyReplacementContext.OwnsBody(body)
            && !(_outgoing != null && body != null && body.IsYourPlayer && ReferenceEquals(body.GameWorld, _world));
    private static bool BeforeBooleanCallback(object __0, ref bool __result)
    {
        if (Allows(_contract?.Owner(__0))) return true;
        __result = true; // Suppress the foreign veto; allow the original native EFT operation.
        return false;
    }
    private static bool Allows(Player body)
    {
        if (ReferenceEquals(body, null)) return true;
        if (!BodyReplacementContext.OwnsBody(body))
        {
            if (!BodyReplacementContext.TryOwnCandidate(body))
            {
                // Init's early weight/weapon callbacks precede world registration. They must not
                // write into the old globals, but do not grant ownership to a same-profile stranger.
                return !(BodyReplacementContext.IsCreating && body is LocalPlayer && body.IsYourPlayer
                    && ReferenceEquals(body.GameWorld, BodyReplacementContext.CurrentWorld)
                    && ReferenceEquals(body.Profile, BodyReplacementContext.ExpectedProfile));
            }
        }
        var current = _candidate ?? _outgoing ?? _active;
        return current != null && current.Active && ReferenceEquals(current.Body, body)
            && BodyReplacementContext.OwnsRaid(_game, _world) && ReferenceEquals(_world.MainPlayer, body)
            && (ReferenceEquals(_game.LocalPlayer, body) || BodyReplacementContext.IsCreating);
    }

    private static void Replaced(Player oldBody, Player newBody, string reason)
    {
        if (_outgoing == null || !ReferenceEquals(_outgoing.Body, oldBody)) return;
        try { Commit(newBody); } catch (Exception ex) { Reject(ex); }
    }
    private static bool Commit(Player body)
    {
        if (_candidate == null || !ReferenceEquals(body, _candidate.Body)
            || !BodyReplacementContext.OwnsRaid(_game, _world) || !ReferenceEquals(_game.LocalPlayer, body)
            || !ReferenceEquals(_world.MainPlayer, body)) return false;
        if (!ReferenceEquals(_active, _candidate))
        {
            _contract.Capture(body); // Refuse foreign ownership drift before retiring the saved bundle.
            Retire(_outgoing); _active = _candidate;
            CoreLog.LogDebug("Realism CommonLib: retired outgoing controllers and retained fresh physical-player state.");
        }
        return true;
    }
    internal static void EndAttempt()
    {
        try
        {
            if (_outgoing != null && !Commit(_game?.LocalPlayer))
            {
                Rollback();
                if (_outgoing.Body != null && !_outgoing.Detached) _active = _outgoing;
            }
        }
        catch (Exception ex) { Reject(ex); }
        finally { _outgoing = null; _candidate = null; _initializing = null; _previousGame = null; _previousWorld = null; }
    }
    internal static void AbortPreparation()
    {
        if (_outgoing == null) return;
        try { Rollback(); }
        catch (Exception ex) { Reject(ex); }
        finally
        {
            _game = _previousGame; _world = _previousWorld;
            _outgoing = null; _candidate = null; _initializing = null; _previousGame = null; _previousWorld = null;
        }
    }
    private static void Rollback()
    {
        if (_candidate != null) Retire(_candidate);
        if (_outgoing == null || _outgoing.Detached) return;
        _contract.Restore(_outgoing.Controllers);
        _outgoing.Active = true;
        for (int i = 0; i < _outgoing.Controllers.Length; i++)
            if (_outgoing.Controllers[i] != null) _outgoing.Controllers[i].enabled = _outgoing.Enabled[i];
    }
    private static void Suspend(Lease lease)
    {
        lease.Active = false;
        foreach (var controller in lease.Controllers) if (controller != null) controller.enabled = false;
    }
    private static void Retire(Lease lease)
    {
        if (lease == null || lease.Detached) return;
        Suspend(lease); _contract.Detach(lease.Controllers); lease.Detached = true;
    }
    internal static void RetireWorld(GameWorld world)
    {
        if (ReferenceEquals(world, null) || !ReferenceEquals(world, _world)) return;
        try
        {
            foreach (var lease in new[] { _active, _outgoing, _candidate }.Where(l => l != null).Distinct())
            { Retire(lease); _contract.ClearOwnedGlobals(lease.Controllers); }
        }
        catch (Exception ex) { Reject(ex); }
        finally
        {
            _active = null; _outgoing = null; _candidate = null; _initializing = null;
            _game = null; _world = null; _previousGame = null; _previousWorld = null;
        }
    }
    private static void Add(MethodInfo target, string name, int kind = 0)
    {
        var hook = typeof(RealismCommonLibBodyCompatibility).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        Targets.Add((target, hook, kind));
        var patch = new HarmonyMethod(hook) { priority = kind == 0 ? Priority.First : Priority.Last };
        Patches.Patch(target, prefix: kind == 0 ? patch : null, finalizer: kind == 2 ? patch : null);
    }
    private static void VerifyHooks()
    {
        foreach (var entry in Targets)
        {
            var info = Harmony.GetPatchInfo(entry.Target);
            if ((entry.Kind == 2 ? info?.Finalizers : info?.Prefixes)?.Count(p => p.owner == Patches.Id && p.PatchMethod == entry.Hook) != 1)
                throw new MissingMethodException("CommonLib body-owner hook is missing");
        }
    }
    private static void Clear()
    {
        foreach (var entry in Targets) Patches.Unpatch(entry.Target, entry.Hook);
        Targets.Clear(); _contract = null; _plugin = null; _version = null;
    }
    private static void Reject(Exception ex)
    {
        _failure = "Realism CommonLib body replacement compatibility failed: " + ex.GetBaseException().Message + ".";
        if (_warning != _failure) { _warning = _failure; CoreLog.LogWarning(_failure); }
    }
}

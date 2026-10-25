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
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Keep ImpactCue's per-raid history while its telemetry follows Core's physical body.</summary>
internal static class ImpactCueCompat
{
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.ImpactCueCompat");
    private static readonly ImpactCueScope Scope = new();
    private static readonly MethodInfo Unregister = typeof(GameWorld).GetMethod(nameof(GameWorld.UnregisterPlayer), new[] { typeof(IPlayer) });
    private static readonly List<MethodInfo> UnregisterTargets = new();
    private static ImpactCueContract _contract;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static bool _initialized, _patched, _fault, _newerWarned, _synchronizing;
    private static string _failure, _lastWarning;
    private static LocalGame Game => Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
    private static GameWorld World => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;

    internal static string GetRejectionReason()
    {
        if (!_initialized) { _initialized = true; RespawnApi.Replaced += Replaced; }
        if (_fault) return _failure;
        if (!Chainloader.PluginInfos.TryGetValue(ImpactCueContract.PluginGuid, out var info))
        {
            if (Scope.Enabled) { Fault(new InvalidOperationException("ImpactCue disappeared during an owned raid")); return _failure; }
            ClearBinding(); return _failure = null;
        }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("ImpactCue has not finished loading");
            if (Scope.Enabled && (!ReferenceEquals(_plugin, info.Instance)
                || _version != ImpactCueContract.Normalize(info.Metadata?.Version)))
                throw new InvalidOperationException("ImpactCue changed during an owned raid");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance)
                && _version == ImpactCueContract.Normalize(info.Metadata?.Version))
            { VerifyPatches(); return _failure; }
            ClearBinding();
            _contract = ImpactCueContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version, out _version);
            _plugin = info.Instance; _patched = true;
            Patch(Unregister, nameof(BeforeUnregister));
            UnregisterTargets.Add(Unregister);
            Patch(_contract.Ready, nameof(BeforeReady));
            Patch(_contract.RenderGate, nameof(BeforeRender));
            Patch(_contract.CacheLocal, nameof(BeforeBodyCallback));
            Patch(_contract.GetLocal, nameof(BeforeGetLocal), nameof(AfterGetLocal));
            Patch(_contract.IsLocalActor, null, nameof(AfterIsLocalActor));
            Patch(_contract.BtrAttached, nameof(BeforeBodyCallback));
            Patch(_contract.BtrDetached, nameof(BeforeBodyCallback));
            VerifyPatches(); _failure = null;
            CoreLog.LogDebug("ImpactCue: physical-body telemetry handover ready.");
            if (_version > ImpactCueContract.ReviewedVersion && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("ImpactCue " + _version + " is newer than reviewed 2.0.3; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            if (Scope.Enabled) Fault(ex);
            else { ClearBinding(); Reject(ex); }
        }
        return _failure;
    }
    private static void Patch(MethodInfo target, string prefix, string postfix = null)
        => Patches.Patch(target, prefix == null ? null : Hook(prefix), postfix == null ? null : Hook(postfix));
    private static HarmonyMethod Hook(string name) => new(typeof(ImpactCueCompat), name) { priority = Priority.First };
    private static void VerifyPatches()
    {
        void Check(MethodInfo target, string name, bool prefix = true)
        {
            var entries = prefix ? Harmony.GetPatchInfo(target)?.Prefixes : Harmony.GetPatchInfo(target)?.Postfixes;
            var hook = typeof(ImpactCueCompat).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            if (entries?.Count(p => p.owner == Patches.Id && p.PatchMethod == hook) != 1)
                throw new MissingMethodException("ImpactCue compatibility hook missing: " + name);
        }
        foreach (var target in UnregisterTargets) Check(target, nameof(BeforeUnregister));
        Check(_contract.Ready, nameof(BeforeReady));
        Check(_contract.RenderGate, nameof(BeforeRender)); Check(_contract.CacheLocal, nameof(BeforeBodyCallback));
        Check(_contract.GetLocal, nameof(BeforeGetLocal)); Check(_contract.GetLocal, nameof(AfterGetLocal), false);
        Check(_contract.IsLocalActor, nameof(AfterIsLocalActor), false);
        Check(_contract.BtrAttached, nameof(BeforeBodyCallback)); Check(_contract.BtrDetached, nameof(BeforeBodyCallback));
    }
    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        var refusal = GetRejectionReason();
        if (refusal != null) throw new InvalidOperationException(refusal);
        if (_contract == null) return;
        if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, oldBody) || !BodyReplacementContext.OwnsBody(oldBody)
            || !ReferenceEquals(game.LocalPlayer, oldBody) || !ReferenceEquals(game.GameWorld.MainPlayer, oldBody))
            throw new InvalidOperationException("ImpactCue requires Core's exact old body");
        if ((bool)_contract.RaidActive.GetValue(null)
            && (!ReferenceEquals(_contract.RaidWorld.GetValue(null), game.GameWorld)
                || !ReferenceEquals(_contract.RaidPlayer.GetValue(null), oldBody)))
            throw new InvalidOperationException("ImpactCue telemetry belongs to another world or physical player");
        // ClientGameWorld can return before calling base when a dead player was
        // already unregistered. Intercept the exact virtual dispatch entry too.
        var unregister = game.GameWorld.GetType().GetMethod(nameof(GameWorld.UnregisterPlayer), new[] { typeof(IPlayer) });
        // Reflection through a derived world returns a different ReflectedType.
        // Harmony expects the declaration, not that inherited MethodInfo wrapper.
        unregister = unregister?.DeclaringType?.GetMethod(nameof(GameWorld.UnregisterPlayer),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new[] { typeof(IPlayer) }, null);
        if (unregister == null || unregister.IsStatic || unregister.ReturnType != typeof(void)
            || unregister.GetMethodBody() == null || unregister.GetBaseDefinition() != Unregister)
            throw new MissingMethodException("ImpactCue cannot verify this world's player unregister entry");
        if (!UnregisterTargets.Contains(unregister))
        {
            Patch(unregister, nameof(BeforeUnregister));
            UnregisterTargets.Add(unregister);
            VerifyPatches();
        }
        Scope.Prepare(game, game.GameWorld, oldBody);
    }
    internal static void AbortPreparation() => Scope.AbortPreparation();
    private static void BeforeUnregister(GameWorld __instance, IPlayer __0)
    {
        if (__0 is not Player body || !BodyReplacementContext.IsCreating
            || !ReferenceEquals(__instance, World) || !BodyReplacementContext.OwnsRaid(Game, __instance)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, body) || !BodyReplacementContext.OwnsBody(body)) return;
        try
        {
            if (Scope.Start(Game, __instance, body))
                _contract.HudChanged.Invoke(null, null); // Invalidate the same-frame render cache only.
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static bool ExactCurrent(Player body)
        => body != null && body.IsYourPlayer && body.HealthController?.IsAlive == true
            && BodyReplacementContext.OwnsRaid(Game, World) && BodyReplacementContext.OwnsBody(body)
            && ReferenceEquals(body.GameWorld, World) && ReferenceEquals(body, World?.MainPlayer)
            && ReferenceEquals(body, Game?.LocalPlayer) && ReferenceEquals(body, GamePlayerOwner.MyPlayer)
            && World.RegisteredPlayers.Contains(body);
    private static bool Allowed(Player body)
        => !_fault && Scope.Allows(body, Game, World, World?.MainPlayer, Game?.LocalPlayer, ExactCurrent(body));
    private static bool BeforeBodyCallback(Player __0)
    {
        if (Scope.OwnsBody(__0)) return Allowed(__0);
        // A candidate is not authoritative until Replaced or a completed recovery.
        return !BodyReplacementContext.OwnsBody(__0) || (!BodyReplacementContext.IsCreating && ExactCurrent(__0));
    }
    private static bool BeforeReady(ref bool __result)
    {
        Tick();
        if (!Scope.Enabled || !ReferenceEquals(Scope.World, World)) return true;
        if (!_fault && !Scope.Frozen && ExactCurrent(Scope.Body as Player)) return true;
        __result = false; return false; // Prevent transient body loss from resetting per-raid combat history.
    }
    private static bool BeforeRender(ref bool __result)
    {
        if (!Scope.Enabled || !ReferenceEquals(Scope.World, World)) return true;
        if (!_fault && !Scope.Frozen && ExactCurrent(Scope.Body as Player)) return true;
        __result = false; return false;
    }
    private static bool BeforeGetLocal(ref Player __result)
    {
        if (!Scope.Enabled || !ReferenceEquals(Scope.World, World)) return true;
        __result = !_fault && !Scope.Frozen && ExactCurrent(Scope.Body as Player) ? Scope.Body as Player : null;
        return false;
    }
    private static void AfterGetLocal(ref Player __result)
    {
        if (Scope.OwnsBody(__result) && !Allowed(__result)) __result = null;
    }
    private static void AfterIsLocalActor()
    {
        // IsLocalActor writes the cache directly. Keep its result (including a
        // grenade kill credited to the old body), but never let that steal HUD ownership.
        try
        {
            var cached = _contract?.CombatPlayer.GetValue(null) as Player;
            if (Scope.OwnsBody(cached) && !Allowed(cached))
                _contract.CombatPlayer.SetValue(null, Scope.Enabled && !Scope.Frozen && !_fault && ExactCurrent(Scope.Body as Player) ? Scope.Body : null);
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static void Replaced(Player oldBody, Player body, string reason)
    {
        if (!Scope.Enabled || !ReferenceEquals(Scope.Body, oldBody)) return;
        try { Synchronize(body, true); } catch (Exception ex) { Fault(ex); }
    }
    internal static void Tick()
    {
        if (!BodyReplacementContext.IsCreating && Scope.PreparedBody != null) Scope.AbortPreparation();
        if (_contract == null || !Scope.Enabled || _fault || _synchronizing) return;
        try
        {
            if (!ReferenceEquals(Scope.World, World)) { RetireWorld(Scope.World as GameWorld); return; }
            Synchronize(World?.MainPlayer, false);
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static void Synchronize(Player body, bool published)
    {
        if (!Scope.Enabled || (BodyReplacementContext.IsCreating && !published) || !ExactCurrent(body)) return;
        bool active = (bool)_contract.RaidActive.GetValue(null);
        if (active && !ReferenceEquals(_contract.RaidWorld.GetValue(null), World))
            throw new InvalidOperationException("ImpactCue active telemetry belongs to another world");
        bool changed = Scope.Frozen || !ReferenceEquals(Scope.Body, body)
            || (active && !ReferenceEquals(_contract.RaidPlayer.GetValue(null), body));
        if (!changed) return;
        _synchronizing = true;
        try
        {
            // Only an already-active SAME raid uses Restore: its context-loss
            // branch resets raid state. Initial Arena swaps retain normal activation.
            if (active)
            {
                if (!(bool)_contract.Restore.Invoke(null, new object[] { body, Time.realtimeSinceStartup, "Core physical-body replacement" })
                    || !ReferenceEquals(_contract.RaidPlayer.GetValue(null), body))
                    throw new InvalidOperationException("ImpactCue did not bind the confirmed replacement");
                foreach (var cache in _contract.BodyCaches) cache.SetValue(null, null);
                _contract.ClearWeapon.Invoke(null, new object[] { true });
                _contract.DetachQuest.Invoke(null, null);
                _contract.AttachQuest.Invoke(null, new object[] { body, World, true });
                _contract.QuestChanged.Invoke(null, null);
                _contract.ForceRefresh.Invoke(null, null);
                _contract.NextBreath.SetValue(null, 0f);
            }
            _contract.CombatPlayer.SetValue(null, body);
            _contract.ResetTarget.Invoke(null, null);
            _contract.ResetLowAmmo.Invoke(null, null);
            _contract.HudChanged.Invoke(null, null);
            Scope.Bind(Game, World, body, true);
            CoreLog.LogDebug("ImpactCue: telemetry follows the confirmed current physical body.");
        }
        finally { _synchronizing = false; }
    }
    internal static void RetireWorld(GameWorld world)
    {
        // No foreign global reset: the mod owns normal raid teardown. Weak body
        // tombstones still fence delayed calls after this world has disappeared.
        Scope.RetireWorld(world);
    }
    private static void Fault(Exception ex) { _fault = true; Reject(ex); }
    private static void Reject(Exception ex)
    {
        _failure = "Core cannot safely replace the player with ImpactCue: " + ex.GetBaseException().Message
            + ". Update or remove ImpactCue, then restart. Ordinary raids remain available.";
        if (_failure == _lastWarning) return;
        _lastWarning = _failure; CoreLog.LogWarning(_failure);
    }
    private static void ClearBinding()
    {
        if (_patched) { _patched = false; Patches.UnpatchSelf(); }
        UnregisterTargets.Clear();
        _contract = null; _plugin = null; _version = null;
    }
}

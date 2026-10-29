using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using EFT;
using HarmonyLib;
using Redeploy.Core.Api;
using UnityEngine;

namespace Redeploy.Core.Compat;

/// <summary>Keep the existing compass alive only for an exact Core body handover.</summary>
internal static class AudioIndicatorsCompat
{
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.AudioIndicatorsCompat");
    private static readonly StandaloneHudScope Scope = new();
    private static readonly ConditionalWeakTable<object, object> RetiredRotations = new();
    private static AudioIndicatorsContract _contract;
    private static BaseUnityPlugin _plugin;
    private static System.Version _version;
    private static Component _rotation;
    private static GameObject _hud;
    private static bool _initialized, _fault, _warned, _resumeRotation;
    private static string _failure;

    internal static string GetRejectionReason()
    {
        if (!_initialized) { _initialized = true; RespawnApi.Replaced += Replaced; }
        if (_fault) return _failure;
        if (!Chainloader.PluginInfos.TryGetValue(AudioIndicatorsContract.PluginGuid, out var info))
        {
            if (Scope.Owned) return Reject(new InvalidOperationException("plugin disappeared during an owned raid"), true);
            Clear(); return null;
        }
        try
        {
            var version = StandaloneHudContract.Normalize(info.Metadata?.Version);
            if (info.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance) && _version == version)
            { Verify(); return _failure; }
            if (Scope.Owned) throw new InvalidOperationException("plugin changed during an owned raid");
            Clear(); _contract = AudioIndicatorsContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version);
            _plugin = info.Instance; _version = version;
            Patch(_contract.Unregister, nameof(BeforeUnregister)); Patch(_contract.Update, nameof(BeforeUpdate));
            Verify();
            if (version > AudioIndicatorsContract.ReviewedVersion && !_warned)
            {
                _warned = true; CoreLog.LogWarning("Audio Accessibility Indicators " + version + " is newer than reviewed 2.1.0; APIs match, native compatibility is unverified.");
            }
            _failure = null;
        }
        catch (Exception ex) { Reject(ex, Scope.Owned); if (!_fault) Clear(); }
        return _failure;
    }
    private static void Patch(MethodInfo method, string name)
        => Patches.Patch(method, new HarmonyMethod(typeof(AudioIndicatorsCompat), name) { priority = Priority.First });
    private static void Verify()
    {
        foreach (var pair in new[] { (_contract.Unregister, nameof(BeforeUnregister)), (_contract.Update, nameof(BeforeUpdate)) })
        {
            var hook = typeof(AudioIndicatorsCompat).GetMethod(pair.Item2, BindingFlags.Static | BindingFlags.NonPublic);
            if (Harmony.GetPatchInfo(pair.Item1)?.Prefixes.Count(p => p.owner == Patches.Id && p.PatchMethod == hook) != 1)
                throw new MissingMethodException("Audio Indicators callback fence is missing: " + pair.Item2);
        }
    }
    internal static void Prepare(LocalGame game, LocalPlayer body)
    {
        var failure = GetRejectionReason();
        if (failure != null) throw new InvalidOperationException(failure);
        if (_contract == null) return;
        var hud = _contract.Hud.GetValue(null) as GameObject;
        if (hud == null) return; // Native initial HUD creation remains the mod's responsibility.
        var rotation = _contract.Rotation.GetValue(null) as Component;
        if (rotation == null || !ReferenceEquals(rotation.gameObject, hud))
            throw new InvalidOperationException("Audio Indicators HUD rotation component changed");
        Scope.Prepare(game, body); _hud = hud; _rotation = rotation;
        _resumeRotation = (bool)_contract.Active.GetValue(rotation)
            || body.HealthController?.IsAlive == false || !game.GameWorld.RegisteredPlayers.Contains(body);
    }
    internal static void AbortPreparation()
    {
        Scope.Abort(); if (!Scope.Active) { _rotation = null; _hud = null; _resumeRotation = false; }
    }
    private static bool BeforeUnregister(IPlayer __0)
    {
        if (__0 is not Player body) return true;
        // This fences only the foreign disposal postfix, never GameWorld's real unregister.
        return !Scope.SuppressUnregister(body);
    }
    private static bool BeforeUpdate(object __instance)
    {
        if (RetiredRotations.TryGetValue(__instance, out _)) return false;
        if (!ReferenceEquals(_rotation, __instance)) return true;
        Tick(); return !_fault && !Scope.BlockCallbacks;
    }
    private static void Replaced(Player oldBody, Player body, string reason)
    {
        if (!ReferenceEquals(oldBody, Scope.Prepared ?? Scope.Body)) return;
        try { Bind(body, true); } catch (Exception ex) { Reject(ex, true); }
    }
    internal static void Tick()
    {
        if (_contract == null || !Scope.Owned || _fault) return;
        try
        {
            if (!ReferenceEquals(Scope.World, StandaloneHudScope.CurrentWorld)) { RetireWorld(Scope.World); return; }
            Bind(Scope.World.MainPlayer, false);
            if (!BodyReplacementContext.IsCreating && Scope.Prepared != null && Scope.Exact(Scope.Prepared))
            {
                RestoreCompass();
                Scope.RecoverPrepared();
                CoreLog.LogDebug("Audio Accessibility Indicators: original living body recovered after replacement failure.");
            }
        }
        catch (Exception ex) { Reject(ex, true); }
    }
    private static void Bind(Player player, bool published)
    {
        if (!Scope.CanBind(player, published)) return;
        RestoreCompass();
        Scope.Bind((LocalPlayer)player);
        CoreLog.LogDebug("Audio Accessibility Indicators: existing compass retained across physical-body replacement.");
    }
    private static void RestoreCompass()
    {
        if (_hud == null || _rotation == null || !ReferenceEquals(_contract.Hud.GetValue(null), _hud)
            || !ReferenceEquals(_contract.Rotation.GetValue(null), _rotation))
            throw new InvalidOperationException("Audio Indicators replaced its HUD during body handover");
        // Normal replacement skips Dispose. If another legitimate old-body callback already
        // stopped it, resume only the component that was active when this transaction began.
        if (_resumeRotation) _contract.Active.SetValue(_rotation, true);
    }
    internal static void RetireWorld(GameWorld world)
    {
        if (!ReferenceEquals(Scope.World, world)) return;
        if (!ReferenceEquals(_rotation, null)) RetiredRotations.GetValue(_rotation, _ => new object());
        _rotation = null; _hud = null; _resumeRotation = false; Scope.Retire(world);
        // Final raid cleanup calls Panel.Dispose normally; no global cleanup is intercepted.
    }
    private static string Reject(Exception ex, bool fault)
    {
        _fault |= fault;
        var message = "Core cannot safely replace the player with Audio Accessibility Indicators: " + ex.GetBaseException().Message
            + ". Update or remove Audio Accessibility Indicators, then restart. Ordinary raids remain available.";
        if (message != _failure) CoreLog.LogWarning(message);
        return _failure = message;
    }
    private static void Clear() { Patches.UnpatchSelf(); _contract = null; _plugin = null; _version = null; }
}

using System;
using System.Linq;
using BepInEx.Bootstrap;
using EFT;
using HarmonyLib;
using Redeploy.Core.Api;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Retire Core's old HANB hotkey component, while leaving the new native component intact.</summary>
internal static class HandsAreNotBusyCompat
{
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.HandsAreNotBusyCompat");
    private static readonly StandaloneHudScope Scope = new();
    private static HandsAreNotBusyContract _contract;
    private static object _plugin;
    private static Version _version;
    private static bool _patched, _subscribed, _newerWarned, _recover;
    private static string _failure, _warning;
    private static Component[] _oldComponents;
    private static GameObject _oldGameObject;

    internal static string GetRejectionReason()
    {
        if (!Chainloader.PluginInfos.TryGetValue(HandsAreNotBusyContract.PluginGuid, out var info))
        {
            ClearBinding(); Scope.Retire(Scope.World); _recover = false; _oldComponents = null; _oldGameObject = null;
            return _failure = null;
        }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            var version = StandaloneHudContract.Normalize(info.Metadata?.Version);
            if (ReferenceEquals(_plugin, info.Instance) && version == _version) return _failure;
            if (Scope.Owned) throw new InvalidOperationException("Hands Are Not Busy changed during an owned replacement");
            ClearBinding();
            _plugin = info.Instance; _version = version;
            _contract = HandsAreNotBusyContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, version);
            // Raised only on the actual patch path, after every absent-plugin return.
            // A failure during Patch still leaves cleanup responsible for the partial hook.
            _patched = true;
            Patches.Patch(_contract.FixHands, prefix: new HarmonyMethod(typeof(HandsAreNotBusyCompat), nameof(BeforeFix)) { priority = Priority.First });
            VerifyPatch();
            if (!_subscribed)
            {
                RespawnApi.Replaced += Replaced;
                RespawnApi.ReplacementFailed += Failed;
                _subscribed = true;
            }
            _failure = null;
            if (version > HandsAreNotBusyContract.ReviewedVersion && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("Hands Are Not Busy " + version + " is newer than reviewed 1.7.1; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            // Preserve fences for already-owned bodies if the installed mod drifts.
            if (!Scope.Owned)
            {
                ClearBinding();
                _plugin = info?.Instance; _version = StandaloneHudContract.Normalize(info?.Metadata?.Version);
            }
            Fault(ex);
        }
        return _failure;
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        if (_contract == null) return;
        if (_failure != null) throw new InvalidOperationException(_failure);
        VerifyPatch();
        var oldGameObject = oldBody.gameObject;
        var components = oldGameObject.GetComponents(_contract.Component);
        foreach (var component in components)
            if (component is not Behaviour || !ReferenceEquals(_contract.Player.GetValue(component), oldBody)
                || !ReferenceEquals(component.gameObject, oldGameObject))
                throw new InvalidOperationException("Hands Are Not Busy component does not own Core's exact old body");
        Scope.Prepare(game, oldBody);
        // No component state is changed before the replacement commits. A refusal or
        // recovered old body therefore keeps the user's previous enabled/disabled state.
        _oldComponents = components; _oldGameObject = oldGameObject; _recover = false;
    }

    private static bool BeforeFix(Player __0)
    {
        if (ReferenceEquals(__0, null)) return true;
        if (Scope.Retired(__0)) return false;
        // A failed candidate can still receive a delayed callback after Core restores
        // the original body. The weak physical lease survives that attempt/raid; it
        // never matches an unrelated bot or another instance sharing the profile ID.
        if (BodyReplacementContext.OwnsBody(__0) && !ReferenceEquals(__0.GameWorld?.MainPlayer, __0)) return false;
        if (!ReferenceEquals(Scope.Prepared, null) && ReferenceEquals(__0, Scope.Prepared)) return false;
        // Native RegisterPlayer already adds HANB to the candidate while creation can
        // still yield. Do not let its hotkey rebuild hands before Core commits them.
        // The shared lease requires the exact world, expected profile and MainPlayer;
        // a same-profile bot/foreign body is not sufficient.
        // Keep the lease fenced between EndAttempt and the recovery pump as well.
        // Abort/recovered-old clears Prepared, so a surviving body immediately resumes.
        if (!ReferenceEquals(Scope.Prepared, null) && !ReferenceEquals(__0, Scope.Prepared)
            && BodyReplacementContext.OwnsRaid(Scope.Game, Scope.World)
            && (BodyReplacementContext.TryOwnCandidate(__0)
                || (BodyReplacementContext.OwnsBody(__0) && ReferenceEquals(__0.GameWorld, Scope.World)))) return false;
        return true;
    }

    private static void Replaced(Player oldBody, Player newBody, string reason)
    {
        if (_contract == null || _failure != null || !ReferenceEquals(oldBody, Scope.Prepared)) return;
        try { Bind(newBody, true); } catch (Exception ex) { Fault(ex); }
    }
    private static void Failed(Player oldBody, string stage)
    {
        if (!ReferenceEquals(oldBody, null) && ReferenceEquals(oldBody, Scope.Prepared)) _recover = true;
    }
    internal static void Tick()
    {
        if (!_recover || _contract == null || _failure != null || BodyReplacementContext.IsCreating) return;
        if (!BodyReplacementContext.OwnsRaid(Scope.Game, Scope.World)) { RetireWorld(Scope.World); return; }
        try
        {
            if (Scope.Prepared != null && Scope.Exact(Scope.Prepared))
            {
                Scope.RecoverPrepared(); _oldComponents = null; _oldGameObject = null; _recover = false;
            }
            else Bind(Scope.World.MainPlayer, false);
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static void Bind(Player body, bool published)
    {
        if (!Scope.CanBind(body, published)) return;
        var oldBody = Scope.Prepared ?? Scope.Body;
        // Commit the physical tombstone before disabling components: even if a Unity
        // callback throws, direct/reset calls cannot mutate the retired hands anymore.
        Scope.Bind((LocalPlayer)body);
        foreach (var component in _oldComponents ?? Array.Empty<Component>())
        {
            if (component == null) continue;
            if (!ReferenceEquals(_contract.Player.GetValue(component), oldBody)
                || !ReferenceEquals(component.gameObject, _oldGameObject))
                throw new InvalidOperationException("Hands Are Not Busy component ownership changed during replacement");
            ((Behaviour)component).enabled = false;
        }
        _oldComponents = null; _oldGameObject = null; _recover = false;
    }
    internal static void AbortPreparation() { Scope.Abort(); _oldComponents = null; _oldGameObject = null; _recover = false; }
    internal static void RetireWorld(GameWorld world)
    {
        if (!ReferenceEquals(world, Scope.World)) return;
        Scope.Retire(world); _oldComponents = null; _oldGameObject = null; _recover = false;
    }
    private static void VerifyPatch()
    {
        var hook = typeof(HandsAreNotBusyCompat).GetMethod(nameof(BeforeFix), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (_contract == null || Harmony.GetPatchInfo(_contract.FixHands)?.Prefixes.Count(p => p.owner == Patches.Id && p.PatchMethod == hook) != 1)
            throw new MissingMethodException("Hands Are Not Busy compatibility fence is missing");
    }
    private static void ClearBinding()
    {
        // UnpatchSelf scans all process patches. Never perform it on the absent-mod
        // polling path unless this adapter really attempted to install a hook.
        // Invalidate the cached success even when UnpatchSelf throws. Otherwise a
        // subsequent poll could accept a partially installed, unsubscribed adapter.
        _plugin = null; _version = null;
        if (_patched) { Patches.UnpatchSelf(); _patched = false; }
        _contract = null;
    }
    private static void Fault(Exception ex)
    {
        _failure = "Hands Are Not Busy body replacement compatibility failed: " + ex.GetBaseException().Message + ".";
        if (_warning != _failure) { _warning = _failure; CoreLog.LogWarning(_failure); }
    }
}

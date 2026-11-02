using System;
using BepInEx.Bootstrap;
using EFT;
using Redeploy.Core.Api;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Update only HollywoodFX's local impact/optic owner after a committed Core replacement.</summary>
internal static class HollywoodFxCompat
{
    private static readonly StandaloneHudScope Scope = new();
    private static HollywoodFxContract _contract;
    private static object _plugin;
    private static Version _version;
    private static bool _subscribed, _newerWarned, _recover;
    private static string _failure, _warning;

    internal static string GetRejectionReason()
    {
        // An absent integration is a dictionary lookup and field clears only: no reflection,
        // component enumeration or Harmony work on repeatedly polled menu admission paths.
        if (!Chainloader.PluginInfos.TryGetValue(HollywoodFxContract.PluginGuid, out var info))
        {
            Scope.Retire(Scope.World);
            _contract = null; _plugin = null; _version = null; _failure = null; _recover = false;
            return null;
        }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            var version = StandaloneHudContract.Normalize(info.Metadata?.Version);
            if (ReferenceEquals(_plugin, info.Instance) && version == _version) return _failure;
            if (Scope.Owned) throw new InvalidOperationException("HollywoodFX changed during an owned replacement");
            _plugin = info.Instance; _version = version;
            _contract = HollywoodFxContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, version);
            if (!_subscribed)
            {
                RespawnApi.Replaced += Replaced;
                RespawnApi.ReplacementFailed += Failed;
                _subscribed = true;
            }
            _failure = null;
            if (version > HollywoodFxContract.ReviewedVersion && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("HollywoodFX " + version + " is newer than reviewed 3.0.0; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex) { Fault(ex); }
        return _failure;
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        if (_contract == null) return;
        if (_failure != null) throw new InvalidOperationException(_failure);
        var cached = _contract.LocalPlayer.GetValue(null) as Player;
        // Arena can replace its entry body before HollywoodFX's raid-start postfix ran.
        // A null cache belongs to upstream initialization; do not initialize its scene here.
        if (!ReferenceEquals(cached, null) && !ReferenceEquals(cached, oldBody))
            throw new InvalidOperationException("HollywoodFX local-player cache does not belong to Core's old body");
        Scope.Prepare(game, oldBody);
        _recover = false;
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
                Scope.RecoverPrepared(); _recover = false;
            }
            else Bind(Scope.World.MainPlayer, false);
        }
        catch (Exception ex) { Fault(ex); }
    }

    private static void Bind(Player body, bool published)
    {
        if (!Scope.CanBind(body, published)) return;
        var oldBody = Scope.Prepared ?? Scope.Body;
        var cached = _contract.LocalPlayer.GetValue(null) as Player;
        if (ReferenceEquals(cached, oldBody)) _contract.LocalPlayer.SetValue(null, body);
        else if (!ReferenceEquals(cached, null) && !ReferenceEquals(cached, body))
            throw new InvalidOperationException("HollywoodFX local-player ownership changed during replacement");
        Scope.Bind((LocalPlayer)body);
        _recover = false;
    }

    internal static void AbortPreparation() { Scope.Abort(); _recover = false; }
    internal static void RetireWorld(GameWorld world)
    {
        if (!ReferenceEquals(world, Scope.World)) return;
        // Upstream owns world disposal. A delayed old-world callback must not clear its
        // next world's cache, so Core deliberately does not write the foreign field here.
        Scope.Retire(world); _recover = false;
    }
    private static void Fault(Exception ex)
    {
        _failure = "HollywoodFX body replacement compatibility failed: " + ex.GetBaseException().Message + ".";
        if (_warning != _failure) { _warning = _failure; CoreLog.LogWarning(_failure); }
    }
}

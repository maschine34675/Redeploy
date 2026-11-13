using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Redeploy.Core.Api;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Reopen only the presentation latch after a committed same-world physical body replacement.</summary>
internal static class IsbNotifierCompat
{
    internal const string PluginGuid = IsbNotifierContract.PluginGuid;
    private sealed class Lease
    {
        internal LocalGame Game;
        internal GameWorld World;
        internal Player OldBody, Body;
    }

    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.IsbNotifierCompat");
    private static readonly ConditionalWeakTable<GameWorld, object> ExplicitlyEnded = new();
    private static IsbNotifierContract _contract;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static Lease _prepared, _pending;
    private static bool _patched, _subscribed, _newerWarned;
    private static string _failure;
    private static GameWorld World => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;

    internal static string GetRejectionReason()
    {
        if (_failure != null) return _failure;
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info))
        {
            if (_prepared != null || _pending != null) Fault(new InvalidOperationException("notifier disappeared during an owned replacement"));
            else if (_contract != null)
            {
                try { ClearBinding(); } catch (Exception ex) { Fault(ex); }
            }
            return _failure;
        }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("notifier has not finished loading");
            if (info.Metadata?.GUID != PluginGuid) throw new InvalidOperationException("notifier registration identity changed");
            var version = IsbNotifierContract.Normalize(info.Metadata?.Version);
            if (_contract != null && ReferenceEquals(info.Instance, _plugin) && version == _version)
            { _contract.VerifyInstance(_plugin); VerifyPatches(); return null; }
            if (_prepared != null || _pending != null) throw new InvalidOperationException("notifier changed during an owned replacement");
            ClearBinding();
            var contract = new IsbNotifierContract(info.Instance.GetType(), info.Metadata?.GUID, version);
            contract.VerifyInstance(info.Instance);
            _contract = contract; _plugin = info.Instance; _version = version;
            _patched = true;
            Patches.Patch(contract.MarkEnded, prefix: new HarmonyMethod(typeof(IsbNotifierCompat), nameof(BeforeExplicitEnd)) { priority = Priority.First });
            Patches.Patch(contract.Reset, prefix: new HarmonyMethod(typeof(IsbNotifierCompat), nameof(BeforeReset)) { priority = Priority.First });
            VerifyPatches();
            if (!_subscribed)
            {
                RespawnApi.Replaced += Replaced;
                RespawnApi.ReplacementFailed += Failed;
                _subscribed = true;
            }
            if (version > IsbNotifierContract.Reviewed && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("ISB SOF Notifier " + version + " is newer than reviewed 2.0.4; private APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            Fault(ex);
            if (_prepared == null && _pending == null)
            {
                try { ClearBinding(); } catch (Exception cleanup) { Fault(cleanup); }
            }
        }
        return _failure;
    }

    private static void VerifyPatches()
    {
        foreach (var pair in new[] { (_contract.MarkEnded, nameof(BeforeExplicitEnd)), (_contract.Reset, nameof(BeforeReset)) })
        {
            var hook = typeof(IsbNotifierCompat).GetMethod(pair.Item2, BindingFlags.Static | BindingFlags.NonPublic);
            if (Harmony.GetPatchInfo(pair.Item1)?.Prefixes.Count(p => p.owner == Patches.Id && p.PatchMethod == hook) != 1)
                throw new MissingMethodException("ISB notifier lifecycle observer is missing: " + pair.Item2);
        }
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        if (_contract == null) return;
        if (_failure != null) throw new InvalidOperationException(_failure);
        var world = game?.GameWorld;
        if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, world)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, oldBody) || !BodyReplacementContext.OwnsBody(oldBody)
            || !ReferenceEquals(game.LocalPlayer, oldBody) || !ReferenceEquals(world.MainPlayer, oldBody)
            || ExplicitlyEnded.TryGetValue(world, out _))
            throw new InvalidOperationException("ISB notifier requires Core's exact old body in a continuing local raid");
        _contract.VerifyInstance(_plugin); VerifyPatches();
        // Read-only: even an already latched dead body remains closed if a later adapter refuses.
        _prepared = new Lease { Game = game, World = world, OldBody = oldBody };
    }

    internal static void AbortPreparation() => _prepared = null;

    private static void Replaced(Player oldBody, Player newBody, string reason)
    {
        var lease = _prepared;
        if (lease == null || !ReferenceEquals(oldBody, lease.OldBody) || ReferenceEquals(oldBody, newBody)
            || !BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsBody(newBody)
            || !Current(lease, newBody)) return;
        _prepared = null;
        lease.Body = newBody;
        _pending = lease;
        TryReopen();
    }

    private static bool Current(Lease lease, Player body)
        => body != null && body.IsYourPlayer && BodyReplacementContext.OwnsRaid(lease.Game, lease.World)
           && ReferenceEquals(body.GameWorld, lease.World) && ReferenceEquals(lease.Game.LocalPlayer, body)
           && ReferenceEquals(lease.World.MainPlayer, body)
           // Publication has already committed the replacement's profile to the game.
           && ReplacementProfileRule.CarriesRaidProfile(body.Profile, lease.Game.Profile, null)
           && !ExplicitlyEnded.TryGetValue(lease.World, out _);

    private static void Failed(Player oldBody, string stage)
    {
        if (_prepared != null && ReferenceEquals(_prepared.OldBody, oldBody)) _prepared = null;
        // A failed rewire can leave a living candidate. Only Replaced can authorize reopening.
    }

    internal static void Tick()
    {
        if (!BodyReplacementContext.IsCreating) _prepared = null;
        if (_pending == null || BodyReplacementContext.IsCreating) return;
        TryReopen();
    }

    private static void TryReopen()
    {
        var lease = _pending;
        if (lease == null || _failure != null || _contract == null) return;
        try
        {
            if (!Current(lease, lease.Body) || !BodyReplacementContext.OwnsBody(lease.Body)
                || lease.Body.HealthController?.IsAlive != true)
            { _pending = null; return; }
            // Use the notifier's own physical membership criterion, never ProfileId.
            var players = lease.World.AllAlivePlayersList;
            if (players == null || !players.Any(player => ReferenceEquals(player, lease.Body))) return;
            if (_plugin == null) throw new InvalidOperationException("notifier was destroyed before presentation reopening");
            _contract.VerifyInstance(_plugin);
            if (!_contract.PresentationEnabled) return;
            _contract.Ended.SetValue(null, false);
            _pending = null; // one write per published replacement; a later death stays closed.
            CoreLog.LogDebug("ISB notifier: presentation reopened for the committed local body; raid history preserved.");
        }
        catch (Exception ex) { Fault(ex); _prepared = _pending = null; }
    }

    private static void BeforeExplicitEnd()
    {
        var world = World;
        if (!ReferenceEquals(world, null) && !ExplicitlyEnded.TryGetValue(world, out _)) ExplicitlyEnded.Add(world, new object());
        _prepared = _pending = null;
    }

    private static void BeforeReset()
    {
        // Observe native startup/teardown only. Never call its broad history/counter reset.
        _prepared = _pending = null;
    }

    internal static void RetireWorld(GameWorld world)
    {
        if (_prepared != null && ReferenceEquals(_prepared.World, world)) _prepared = null;
        if (_pending != null && ReferenceEquals(_pending.World, world)) _pending = null;
    }

    private static void Fault(Exception ex)
    {
        var failure = "Core cannot safely replace the player with ISB SOF Notifier: " + ex.GetBaseException().Message
            + ". Update or remove the notifier, then restart. Ordinary raids remain available.";
        if (_failure != failure) CoreLog.LogWarning(failure);
        _failure = failure;
    }

    private static void ClearBinding()
    {
        if (_patched) { Patches.UnpatchSelf(); _patched = false; }
        _contract = null; _plugin = null; _version = null;
    }
}

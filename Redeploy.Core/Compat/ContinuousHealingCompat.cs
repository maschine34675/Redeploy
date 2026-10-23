using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Fence retired Core bodies; leave CH on the current local body and unrelated raids.</summary>
internal static class ContinuousHealingCompat
{
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.ContinuousHealingCompat");
    private static readonly ContinuousHealingScope Scope = new();
    private static MethodInfo[] _foreign;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static bool _initialized, _patchAttempted, _runtimeFault, _reportedNewer;
    private static string _failure, _lastWarning;
    private static int _reportedGuards, _reportedLocal;

    internal static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        Refresh();
    }

    internal static string GetRejectionReason()
    {
        if (!_initialized) Initialize();
        else Refresh();
        return _failure;
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        if (GetRejectionReason() is { } reason) throw new InvalidOperationException(reason);
        if (_foreign == null) return;
        if (!BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            || !BodyReplacementContext.OwnsBody(oldBody))
            throw new InvalidOperationException("Continuous Healing could not verify the outgoing Core body.");
        // Core holds the provisional weak lease. Do not duplicate it here: a
        // later adapter may reject preparation before any teardown occurred.
    }

    // No polling is needed: the three foreign hooks carry the physical owner.
    internal static void Tick() { }

    private static void Refresh()
    {
        if (!Chainloader.PluginInfos.TryGetValue(ContinuousHealingContract.PluginGuid, out var info))
        {
            ClearBinding();
            _failure = null;
            _runtimeFault = false;
            return;
        }
        if (_runtimeFault) return;
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Continuous Healing has not finished loading");
            if (_foreign != null && ReferenceEquals(_plugin, info.Instance)
                && _version == ContinuousHealingContract.Normalize(info.Metadata?.Version))
            {
                ContinuousHealingContract.VerifyNativeBindings(_foreign);
                if (!OwnsPatches()) throw new InvalidOperationException("Continuous Healing compatibility hook was removed");
                return;
            }
            ClearBinding();
            var methods = ContinuousHealingContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID,
                info.Metadata?.Version, out var version);
            _patchAttempted = true;
            for (int i = 0; i < methods.Length; ++i)
                Patches.Patch(methods[i], prefix: new HarmonyMethod(typeof(ContinuousHealingCompat),
                    i == 0 ? nameof(BeforeEndHeal) : nameof(BeforeLocalPlayerAction)) { priority = Priority.First });
            _foreign = methods;
            _plugin = info.Instance;
            _version = version;
            if (!OwnsPatches()) throw new InvalidOperationException("Continuous Healing compatibility hooks did not install");
            _failure = null;
            CoreLog.LogDebug("Continuous Healing: exact-body medical guards ready.");
            if (version > ContinuousHealingContract.ReviewedVersion && !_reportedNewer)
            {
                _reportedNewer = true;
                CoreLog.LogWarning("Continuous Healing " + version
                    + " is newer than reviewed 1.6.3; its medical hook APIs were checked, but runtime compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            ClearBinding();
            Reject(ex);
        }
    }

    private static bool OwnsPatches()
        => _foreign != null && _foreign.Select((method, index) => new { method, index }).All(entry =>
            Harmony.GetPatchInfo(entry.method)?.Prefixes.Any(p => p.owner == Patches.Id
                && p.PatchMethod == typeof(ContinuousHealingCompat).GetMethod(
                    entry.index == 0 ? nameof(BeforeEndHeal) : nameof(BeforeLocalPlayerAction),
                    BindingFlags.NonPublic | BindingFlags.Static)) == true);

    // This prefixes CH's bool Prefix, not the native void OnEffectRemoved. A
    // true result from the skipped foreign prefix explicitly permits native work.
    private static bool BeforeEndHeal(Player.MedsController.MedsInHandsOperation __0, ref bool __result)
    {
        if (_foreign == null || !ShouldBypass(__0?._controller?._player, "effect removed")) return true;
        __result = true;
        return false;
    }

    // A retired local body's start/cancel event must not reset/cancel the new
    // body's continuous heal. Neither hook suppresses the underlying EFT action.
    private static bool BeforeLocalPlayerAction(Player __0, MethodBase __originalMethod)
        => _foreign == null || !ShouldBypass(__0, __originalMethod.DeclaringType.Name);

    private static bool ShouldBypass(Player body, string action)
    {
        bool owned = false;
        try
        {
            if (ReferenceEquals(body, null)) return false;
            var bodyWorld = body.GameWorld;
            var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            bool coreOwned = BodyReplacementContext.OwnsBody(body)
                || BodyReplacementContext.TryOwnCandidate(body);
            // Core's weak lease is itself sufficient after a raid transition,
            // including bodies that never healed while they were current.
            owned = coreOwned || Scope.Owns(body, bodyWorld, world, false);
            if (!owned)
                owned = Scope.Owns(body, bodyWorld, world,
                    BodyReplacementCompatibility.OwnsAdditionalHealingBody(body));
            if (!owned) return false;
            bool local = body.IsYourPlayer, alive = body.HealthController?.IsAlive == true;
            bool bypass = ContinuousHealingScope.Bypass(body, bodyWorld, world, world?.MainPlayer, local, alive);
            // Debug only, bounded separately for current, retired, and opt-in bot bodies.
            {
                // One line per seam and body category keeps an early start/cancel
                // burst from consuming the later effect-removal evidence budget.
                int seam = action == "effect removed" ? 0 : action == "CH_StartHeal_Patch" ? 1 : 2;
                int guardBit = 1 << (seam + (local ? 3 : 0));
                int localBit = 1 << seam;
                if (bypass && (_reportedGuards & guardBit) == 0)
                {
                    _reportedGuards |= guardBit;
                    CoreLog.LogDebug("Continuous Healing " + action + ": native fallback for "
                        + (local ? "retired local body" : "consumer-owned bot") + "; alive=" + alive + ".");
                }
                else if (!bypass && (_reportedLocal & localBit) == 0)
                {
                    _reportedLocal |= localBit;
                    CoreLog.LogDebug("Continuous Healing " + action + ": current local body keeps foreign behavior.");
                }
            }
            return bypass;
        }
        catch (Exception ex)
        {
            if (owned)
            {
                _runtimeFault = true;
                Reject(ex);
            }
            // Only an already proved body lease grants fallback authority.
            return owned;
        }
    }

    private static void Reject(Exception ex)
    {
        _failure = "Core cannot safely replace the player with Continuous Healing: " + ex.GetBaseException().Message
            + ". Update or remove Continuous Healing, then restart. Ordinary raids remain available.";
        if (_lastWarning == _failure) return;
        _lastWarning = _failure;
        CoreLog.LogWarning(_failure);
    }

    private static void ClearBinding()
    {
        _foreign = null;
        _plugin = null;
        _version = null;
        Scope.Clear();
        _reportedGuards = _reportedLocal = 0;
        if (!_patchAttempted) return;
        _patchAttempted = false;
        try { Patches.UnpatchSelf(); }
        catch (Exception ex) { Reject(ex); }
    }

    internal static void Shutdown()
    {
        ClearBinding();
        _initialized = _runtimeFault = false;
        _failure = _lastWarning = null;
    }
}

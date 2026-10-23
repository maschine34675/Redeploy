using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using EFT;
using Redeploy.Core.Api;
using Redeploy.Core.Engine;
using UnityEngine;

namespace Redeploy.Core.Compat;

/// <summary>
/// Optional-mod protection shared by Core consumers. It owns only Core replacements;
/// a normal raid or the initial player construction does not grant replacement authority.
/// </summary>
public static class BodyReplacementCompatibility
{
    private sealed class MedicalScope : IDisposable
    {
        internal Func<Player, bool> Owns;
        internal string Failure;
        public void Dispose() { MedicalScopes.Remove(this); Owns = null; }
    }

    private static readonly List<MedicalScope> MedicalScopes = new();
    private static bool _initialized, _prepared;
    private static string _tickFailure;

    /// <summary>Last admission result, for UI predicates that run repeatedly. Refresh with
    /// GetRejectionReason at raid/death admission and before starting a replacement.</summary>
    public static string CachedRejectionReason { get; private set; }

    /// <summary>A consumer may explicitly own additional medical bodies (for example Arena bots).
    /// This does not extend recoil/ammo ownership or grant authority to replace those bodies.</summary>
    public static IDisposable RegisterHealingScope(Func<Player, bool> ownsBody)
    {
        if (ownsBody == null) throw new ArgumentNullException(nameof(ownsBody));
        var scope = new MedicalScope { Owns = ownsBody };
        MedicalScopes.Add(scope);
        return scope;
    }

    internal static bool OwnsAdditionalHealingBody(Player body)
    {
        foreach (var scope in MedicalScopes.ToArray())
        {
            if (scope.Owns == null || scope.Failure != null) continue;
            try { if (scope.Owns(body)) return true; }
            catch (Exception ex)
            {
                scope.Failure = "A registered medical-body scope failed: " + ex.GetBaseException().Message;
                CoreLog.LogWarning(scope.Failure);
            }
        }
        return false;
    }

    /// <summary>Null when the installed optional-mod contracts permit a Core replacement.
    /// Call at an admission boundary, not every frame. This never changes another mod's settings.</summary>
    public static string GetRejectionReason()
    {
        Initialize();
        // An older Arena build still owns these hooks and permits newer Core libraries.
        // Refuse that mixed pair before either Core adapter can install a second owner.
        var legacyArena = LegacyArenaConflict();
        if (legacyArena != null) return CachedRejectionReason = legacyArena;
        // Inspect each family even if an earlier one refuses: retryable load-order failures
        // must not prevent the other integrations from becoming ready.
        var recoil = RecoilLegacyCompat.GetRejectionReason();
        var ammo = ContinuousLoadAmmoCompat.GetRejectionReason();
        var healing = ContinuousHealingCompat.GetRejectionReason();
        foreach (var scope in MedicalScopes)
            if (scope.Failure != null) return CachedRejectionReason = scope.Failure;
        return CachedRejectionReason = recoil ?? ammo ?? healing ?? _tickFailure;
    }

    private static string LegacyArenaConflict()
    {
        if (!HasOptionalMods() || !Chainloader.PluginInfos.TryGetValue("com.maschine.ArenaMode", out var arena))
            return null;
        if (arena?.Instance == null) return "ArenaMode has not finished loading; body replacement cannot be checked yet.";
        try
        {
            var assembly = arena.Instance.GetType().Assembly;
            bool OldAdapter(string guid, string typeName) => Chainloader.PluginInfos.ContainsKey(guid)
                && assembly.GetType("ArenaMode.Compat." + typeName, throwOnError: false) != null;
            if (OldAdapter(RecoilLegacyContract.PluginGuid, "ArenaRecoilLegacyCompat")
                || OldAdapter(ContinuousLoadAmmoContract.PluginGuid, "ArenaContinuousLoadAmmoCompat")
                || OldAdapter(ContinuousHealingContract.PluginGuid, "ArenaContinuousHealingCompat"))
                return "ArenaMode still contains its older body-replacement adapters. Update ArenaMode and its matching Redeploy.Core together, then restart the game.";
        }
        catch (Exception ex)
        {
            return "The installed ArenaMode body-replacement adapters could not be checked: " + ex.GetBaseException().Message;
        }
        return null;
    }

    internal static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        // Adapters subscribe on their first admission check, after the chainloader is ready.
        // Resetting the construction context needs no foreign plugin reflection.
        RespawnApi.RaidEnded += Ended;
        RaidLifecycle.OnRaidStarted += Started;
    }

    private static bool HasOptionalMods()
        => Chainloader.PluginInfos.ContainsKey(RecoilLegacyContract.PluginGuid)
           || Chainloader.PluginInfos.ContainsKey(ContinuousLoadAmmoContract.PluginGuid)
           || Chainloader.PluginInfos.ContainsKey(ContinuousHealingContract.PluginGuid);

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        var refusal = GetRejectionReason();
        if (refusal != null) throw new InvalidOperationException(refusal);
        if (!HasOptionalMods()) return; // Keep the original Core path unchanged when none is installed.
        BodyReplacementContext.Begin(game, oldBody);
        _prepared = true;
        RecoilLegacyCompat.Prepare(game, oldBody);
        ContinuousLoadAmmoCompat.Prepare(game, oldBody);
        ContinuousHealingCompat.Prepare(game, oldBody);
        var pump = game.GameWorld.gameObject.GetComponent<BodyReplacementCompatPump>();
        if (pump == null) pump = game.GameWorld.gameObject.AddComponent<BodyReplacementCompatPump>();
        pump.World = game.GameWorld;
        RecoilLegacyCompat.CompletePreparation();
    }

    internal static void ValidateCreated(LocalPlayer body)
    {
        if (!_prepared) return;
        if (!BodyReplacementContext.TryOwnCandidate(body))
            throw new InvalidOperationException("The replacement body no longer belongs to the prepared Core operation.");
        var refusal = GetRejectionReason();
        if (refusal != null) throw new InvalidOperationException(refusal);
    }

    internal static void EndAttempt()
    {
        _prepared = false;
        BodyReplacementContext.EndAttempt();
    }

    internal static void CancelPreparation()
    {
        // No teardown or candidate construction has happened. Undo only provisional
        // adoption; prior successful-body tombstones must survive a refused retry.
        RecoilLegacyCompat.AbortPreparation();
        ContinuousLoadAmmoCompat.AbortPreparation();
        _prepared = false;
        BodyReplacementContext.CancelPreparation();
    }

    internal static void Tick()
    {
        if (_tickFailure != null) return;
        try
        {
            RecoilLegacyCompat.Tick();
            ContinuousLoadAmmoCompat.Tick();
            ContinuousHealingCompat.Tick();
        }
        catch (Exception ex)
        {
            _tickFailure = "Body replacement compatibility update failed: " + ex.GetBaseException().Message;
            CachedRejectionReason = _tickFailure;
            CoreLog.LogWarning(_tickFailure);
        }
    }

    private static void Ended()
    {
        WorldDisposed(BodyReplacementContext.CurrentWorld);
    }

    private static void Started(GameWorld world)
    {
        // RaidLifecycle deliberately emits no synthetic end when the old end was missed.
        var oldWorld = BodyReplacementContext.CurrentWorld;
        if (!ReferenceEquals(oldWorld, world)) WorldDisposed(oldWorld);
    }

    internal static void WorldDisposed(GameWorld world)
    {
        if (ReferenceEquals(world, null)) return;
        RecoilLegacyCompat.RetireWorld(world);
        // A late OnDestroy from an older pump must never clear a newer replacement.
        if (!ReferenceEquals(world, BodyReplacementContext.CurrentWorld)) return;
        EndAttempt();
        BodyReplacementContext.Reset();
        // The world-owned pump may already be destroyed. Retire controller leases now,
        // while leaving the optional hooks installed for delayed old-body callbacks.
        Tick();
    }
}

internal sealed class BodyReplacementCompatPump : MonoBehaviour
{
    internal GameWorld World;
    private void Update()
    {
        if (ReferenceEquals(World, BodyReplacementContext.CurrentWorld)) BodyReplacementCompatibility.Tick();
    }
    private void OnDestroy() => BodyReplacementCompatibility.WorldDisposed(World);
}

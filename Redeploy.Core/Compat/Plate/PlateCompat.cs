using System;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using Redeploy.Core.Api;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Retire a single PLATE blood record only after Core grants the next physical life.</summary>
internal static class PlateCompat
{
    internal const string PluginGuid = "com.anamelash.plate";
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.PlateCompat");
    private static readonly StandaloneHudScope Scope = new();
    private static PlateContract _contract;
    private static object _plugin, _preparedState;
    private static Version _version;
    private static string _failure, _warning, _oldId;
    private static MovementContext _movement;
    private static SkillManager _skills;
    [ThreadStatic] private static Player _initializingBlood;
    private static bool _patched, _cleanupPending, _subscribed, _warnedNewer, _recover;

    internal static string GetRejectionReason()
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info) || FikaCompatCheck.IsFikaLoaded)
        {
            try { Unbind(); RetireWorld(Scope.World); }
            catch (Exception ex) { return Fault(ex); }
            return null;
        }
        var version = StandaloneHudContract.Normalize(info?.Metadata?.Version);
        try
        {
            if (_cleanupPending) RemovePatches();
            if (info?.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            if (ReferenceEquals(info.Instance, _plugin) && version == _version) return _failure;
            if (Scope.Owned) throw new InvalidOperationException("plugin changed during an owned replacement");
            Unbind();
            // Cache rejected bindings as well as successful ones. Admission is polled in the menu.
            _plugin = info.Instance;
            _version = version;
            _contract = new PlateContract(info.Instance.GetType(), info.Metadata.GUID, version);
            if (!_subscribed)
            {
                RespawnApi.Replaced += Replaced;
                RespawnApi.ReplacementFailed += Failed;
                _subscribed = true;
            }
            if (version > PlateContract.Reviewed && !_warnedNewer)
            {
                _warnedNewer = true;
                CoreLog.LogWarning("P.L.A.T.E. " + version + " is newer than reviewed 1.6.0; its body-swap APIs match.");
            }
        }
        catch (Exception ex) { return Fault(ex); }
        return _failure;
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        if (_failure != null) throw new InvalidOperationException(_failure);
        if (_contract == null) return;
        Scope.Prepare(game, oldBody);
        try { EnsurePatches(); }
        catch (Exception ex) { throw new InvalidOperationException(Fault(ex), ex); }
        _oldId = oldBody.ProfileId;
        var destination = BodyReplacementContext.ExpectedProfile.Id;
        var states = _contract.Registry;
        _preparedState = _contract.CheckOwner(states, _oldId, oldBody);
        if (destination != _oldId) _contract.CheckOwner(states, destination, null);
        // Native Unity wrappers need not remain readable after the old body is destroyed.
        _movement = oldBody.MovementContext;
        _skills = oldBody.Skills;
        _contract.PlanMovementCleanup(_oldId, _movement, _skills);
    }

    private static void EnsurePatches()
    {
        if (!_patched)
        {
            _patched = true;
            try
            {
                foreach (var (target, prefix) in _contract.Hooks)
                    Patches.Patch(target, prefix: new HarmonyMethod(typeof(PlateCompat), prefix) { priority = Priority.First });
            }
            catch
            {
                _cleanupPending = true;
                RemovePatches();
                throw;
            }
        }
        foreach (var (target, prefix) in _contract.Hooks)
        {
            var method = typeof(PlateCompat).GetMethod(prefix, BindingFlags.Static | BindingFlags.NonPublic);
            if (Harmony.GetPatchInfo(target)?.Prefixes.Count(p => p.owner == Patches.Id && p.PatchMethod == method) != 1)
                throw new InvalidOperationException("missing P.L.A.T.E. hook on " + target.Name);
        }
    }

    private static bool AllowPlayer(Player __0)
    {
        if (ReferenceEquals(__0, null)) return true;
        if (Scope.Retired(__0)) return false;
        // A failed candidate can first call us only after Core recovered onto the old
        // body. Its weak lease still identifies it even after the attempt has ended.
        if (BodyReplacementContext.OwnsBody(__0) && !ReferenceEquals(__0.GameWorld?.MainPlayer, __0)) return false;
        if (ReferenceEquals(Scope.Prepared, null) || !BodyReplacementContext.OwnsRaid(Scope.Game, Scope.World)) return true;
        return !ReferenceEquals(__0, Scope.Prepared) && !BodyReplacementContext.OwnsBody(__0)
            && !BodyReplacementContext.TryOwnCandidate(__0);
    }

    private static bool AllowKill(ActiveHealthController __0) => AllowPlayer(__0?.Player);
    private static bool AllowCreate(Player __0) => ReferenceEquals(__0, _initializingBlood) || AllowPlayer(__0);
    private static bool AllowTick(object __0) => _preparedState == null || !ReferenceEquals(__0, _preparedState);
    private static bool AllowUpdate()
    {
        // PLATE interprets a missing MainPlayer as raid end: its ticker would upload
        // blood and clear *every* combatant. Suppress only that transient Core gap.
        return ReferenceEquals(Scope.Prepared, null) || !BodyReplacementContext.OwnsRaid(Scope.Game, Scope.World)
            || Scope.World.MainPlayer != null;
    }

    private static void Replaced(Player oldBody, Player newBody, string reason)
    {
        if (ReferenceEquals(oldBody, null) || !ReferenceEquals(oldBody, Scope.Prepared)) return;
        try { Bind(newBody, true); } catch (Exception ex) { Fault(ex); }
    }

    private static void Failed(Player oldBody, string stage)
    {
        if (!ReferenceEquals(oldBody, null) && ReferenceEquals(oldBody, Scope.Prepared)) _recover = true;
    }

    internal static void Tick()
    {
        if (!_recover || _failure != null || BodyReplacementContext.IsCreating) return;
        if (!BodyReplacementContext.OwnsRaid(Scope.Game, Scope.World)) { RetireWorld(Scope.World); return; }
        try
        {
            if (Scope.Exact(Scope.Prepared))
            {
                Scope.RecoverPrepared();
                ForgetPreparation();
            }
            else Bind(Scope.World.MainPlayer, false);
        }
        catch (Exception ex) { Fault(ex); }
    }

    private static void Bind(Player replacement, bool published)
    {
        if (!Scope.CanBind(replacement, published))
        {
            // If the owner's final binding is delayed, the world pump can finish once
            // the physical survivor is known. Never substitute a matching profile ID.
            _recover = true;
            return;
        }
        var states = _contract.Registry;
        var old = _contract.CheckOwner(states, _oldId, Scope.Prepared);
        var id = replacement.ProfileId;
        var priorDestination = id == _oldId ? old : _contract.CheckOwner(states, id, replacement);
        var releaseMovement = _contract.PlanMovementCleanup(_oldId, _movement, _skills);
        if (old != null)
        {
            states.Remove(_oldId);
            // Use PLATE's native initial blood volume and wound defaults. Only this
            // explicit call may initialize a candidate while both lives are fenced.
            _initializingBlood = replacement;
            try
            {
                var fresh = _contract.CreateState.Invoke(null, new object[] { replacement });
                if (fresh == null || !ReferenceEquals(fresh, _contract.CheckOwner(states, id, replacement)))
                    throw new InvalidOperationException("P.L.A.T.E. failed to initialize the new blood record");
            }
            catch
            {
                if (priorDestination == null) states.Remove(id);
                else states[id] = priorDestination;
                states[_oldId] = old;
                throw;
            }
            finally { _initializingBlood = null; }
        }
        Scope.Bind((LocalPlayer)replacement);
        releaseMovement();
        ForgetPreparation();
    }

    internal static void AbortPreparation() { Scope.Abort(); ForgetPreparation(); }
    internal static void RetireWorld(GameWorld world)
    {
        if (!ReferenceEquals(world, Scope.World)) return;
        Scope.Retire(world);
        ForgetPreparation();
    }

    private static void ForgetPreparation()
    {
        _preparedState = null;
        _oldId = null;
        _movement = null;
        _skills = null;
        _recover = false;
    }

    private static string Fault(Exception ex)
    {
        _failure = "P.L.A.T.E. body replacement compatibility failed: " + ex.GetBaseException().Message;
        if (_warning == _failure) return _failure;
        _warning = _failure;
        CoreLog.LogWarning(_failure);
        return _failure;
    }

    private static void RemovePatches()
    {
        if (_patched)
        {
            _cleanupPending = true;
            Patches.UnpatchSelf();
            _patched = false;
        }
        _cleanupPending = false;
    }

    private static void Unbind()
    {
        _plugin = null;
        _version = null;
        RemovePatches();
        _contract = null;
        _failure = null;
    }
}

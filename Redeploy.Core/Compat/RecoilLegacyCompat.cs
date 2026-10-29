using System;
using System.Linq;
using System.Reflection;

using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Animations.NewRecoil;
using HarmonyLib;

using Redeploy.Core.Api;

using UnityEngine;
using Object = UnityEngine.Object;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Transfer optional recoil components between exact Core replacement bodies, never their GameObjects.</summary>
internal static class RecoilLegacyCompat
{
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.RecoilLegacyCompat");
    private static readonly RecoilLegacyScope Scope = new();
    private static RecoilLegacyContract _contract;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static bool _initialized, _patchAttempted, _fault, _newerWarned, _refreshing;
    private static string _failure, _lastWarning;
    private static RecoilLegacyScope.Lease _preparedLease;
    private static object _preparedLastHands;
    private static object[] _adoptedHands;
    private static bool _newAdoption;
    private sealed class Creation { internal RecoilLegacyScope.Lease Lease; }
    private sealed class Recalculation
    {
        internal RecoilLegacyScope.Lease Lease;
        internal Player.FirearmController Hands;
        internal object[] Before;
    }

    internal static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        RespawnApi.Replaced += Replaced;
        RespawnApi.ReplacementFailed += ReplacementFailed;
        RespawnApi.RaidEnded += Ended;
        Refresh();
    }
    internal static string GetRejectionReason() { if (!_initialized) Initialize(); else Refresh(); return _failure; }
    private static void Refresh()
    {
        if (_fault) return;
        if (!Chainloader.PluginInfos.TryGetValue(RecoilLegacyContract.PluginGuid, out var info))
        { ClearBinding(); _failure = null; return; }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Recoil Rework Legacy has not finished loading");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance)
                && _version == RecoilLegacyContract.Normalize(info.Metadata?.Version))
            { _contract.VerifyNativeBindings(); VerifyOwnPatches(); return; }
            ClearBinding();
            var c = RecoilLegacyContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version, out var v);
            _contract = c; _plugin = info.Instance; _version = v;
            _patchAttempted = true;
            Patch(c.InitPostfix, nameof(BeforeInit), nameof(AfterInit));
            Patch(c.RecalculatePostfix, nameof(BeforeRecalculate), nameof(AfterRecalculate));
            Patch(c.RecoilUpdate, nameof(BeforeComponentUpdate));
            Patch(c.CameraUpdate, nameof(BeforeComponentUpdate));
            VerifyOwnPatches(); _failure = null;
            CoreLog.LogDebug("Core Recoil Rework Legacy: physical-body component handoff ready.");
            if (v > RecoilLegacyContract.ReviewedVersion && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("Recoil Rework Legacy " + v + " is newer than reviewed 2.0.0; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex) { ClearBinding(); Reject(ex); }
    }
    private static void Patch(MethodInfo target, string before, string after = null)
        => Patches.Patch(target, prefix: new HarmonyMethod(typeof(RecoilLegacyCompat), before) { priority = Priority.First },
            finalizer: after == null ? null : new HarmonyMethod(typeof(RecoilLegacyCompat), after) { priority = Priority.Last });
    private static void VerifyOwnPatches()
    {
        Check(_contract.InitPostfix, nameof(BeforeInit), nameof(AfterInit));
        Check(_contract.RecalculatePostfix, nameof(BeforeRecalculate), nameof(AfterRecalculate));
        Check(_contract.RecoilUpdate, nameof(BeforeComponentUpdate), null);
        Check(_contract.CameraUpdate, nameof(BeforeComponentUpdate), null);
    }
    private static void Check(MethodInfo target, string before, string after)
    {
        var info = Harmony.GetPatchInfo(target);
        bool Has(System.Collections.Generic.IEnumerable<Patch> entries, string name)
            => entries != null && entries.Count(p => p.owner == Patches.Id && p.PatchMethod ==
                typeof(RecoilLegacyCompat).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)) == 1;
        if (!Has(info?.Prefixes, before) || after != null && !Has(info?.Finalizers, after))
            throw new MissingMethodException("Recoil Rework Legacy compatibility hook missing: " + before);
    }

    private static LocalGame Game => Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
    private static GameWorld World => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
    private static bool Exact(LocalGame game, GameWorld world)
        => BodyReplacementContext.OwnsRaid(game, world);

    /// <summary>Adopt the unmodified first body before the engine tears it down.</summary>
    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        CompletePreparation();
        var failure = GetRejectionReason();
        if (failure != null) throw new InvalidOperationException(failure);
        if (_contract == null) return;
        var world = game?.GameWorld;
        if (!BodyReplacementContext.IsCreating || !Exact(game, world)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, oldBody)
            || !BodyReplacementContext.OwnsBody(oldBody)
            || !ReferenceEquals(game.LocalPlayer, oldBody) || !ReferenceEquals(world.MainPlayer, oldBody))
            throw new InvalidOperationException("Recoil adoption requires Core's exact old player body");

        var recoil = oldBody.GetComponent(_contract.RecoilType);
        var camera = oldBody.GetComponent(_contract.CameraType);
        if (!Exists(recoil) || !Exists(camera)
            || !ReferenceEquals(_contract.RecoilInstance.GetValue(null), recoil)
            || !ReferenceEquals(_contract.CameraInstance.GetValue(null), camera)
            || !ReferenceEquals(_contract.RecoilPlayer.GetValue(recoil), oldBody)
            || _contract.Spring.GetValue(recoil) == null
            || _contract.CameraSprings.Any(f => f.GetValue(camera) == null))
            throw new InvalidOperationException("Existing recoil components are missing or belong to another body");

        var cached = _contract.Cache[0].GetValue(null) as Player.FirearmController;
        if (Exists(cached) && !ReferenceEquals(cached.GetComponent<Player>(), oldBody))
            throw new InvalidOperationException("Existing recoil cache belongs to another body");
        var lease = Scope.Find(oldBody);
        if (lease != null && (lease.Retired || lease.Failed || !lease.Committed || lease.RecoveryPending
            || lease.Previous != null || !ReferenceEquals(lease.Game, game)
            || !ReferenceEquals(lease.World, world) || !ReferenceEquals(lease.Recoil, recoil)
            || !ReferenceEquals(lease.Camera, camera)))
            throw new InvalidOperationException("Existing recoil lease no longer matches the old body");
        _newAdoption = lease == null;
        lease ??= Scope.Add(oldBody, world, game, null);
        _preparedLease = lease;
        _preparedLastHands = lease.LastHands;
        _adoptedHands = new object[] { oldBody.HandsController as Player.FirearmController, cached };
        lease.Recoil = recoil; lease.Camera = camera; lease.Committed = true;
        // Adoption does not disable, destroy or alter foreign state. A later adapter
        // can still reject preparation while the original player remains untouched.
        if (oldBody.HandsController is Player.FirearmController hands) Scope.TrackHands(hands, lease);
        if (!ReferenceEquals(cached, null)) Scope.TrackHands(cached, lease);
        lease.LastHands = cached;
    }
    internal static void AbortPreparation()
    {
        if (_preparedLease != null)
        {
            if (_newAdoption) Scope.AbandonAdoption(_preparedLease, _adoptedHands);
            else _preparedLease.LastHands = _preparedLastHands;
        }
        CompletePreparation();
    }
    internal static void CompletePreparation()
    {
        _preparedLease = null; _preparedLastHands = null; _adoptedHands = null; _newAdoption = false;
    }
    private static bool Current(RecoilLegacyScope.Lease lease)
        => Authoritative(lease) && ((Player)lease.Body).HealthController?.IsAlive == true;
    private static bool Authoritative(RecoilLegacyScope.Lease lease)
    {
        var game = Game; var world = World;
        return lease?.Body is Player body && body != null
            && RecoilLegacyScope.Current(lease, world, game, world?.MainPlayer, game?.LocalPlayer,
                Exact(game, world), true);
    }
    private static bool Exists(object obj) => obj is Object unity && unity != null;
    private static Behaviour Behaviour(object obj) => obj as Behaviour;
    private static void Enable(object obj, bool value) { if (Exists(obj)) Behaviour(obj).enabled = value; }

    private static bool BeforeInit(Player __0, out Creation __state)
    {
        __state = null;
        if (_contract == null || __0 is not LocalPlayer body || !body.IsYourPlayer) return true;
        if (Scope.Find(body) != null) return false; // Includes delayed callbacks on retired bodies from a prior world.
        var game = Game; var world = World;
        // An own-Scav replacement body carries the replacement profile while game.Profile still
        // is the old one (the engine commits it after creation), so both profiles qualify.
        if (!Exact(game, world) || !ReferenceEquals(body.GameWorld, world)
            || !ReplacementProfileRule.CarriesRaidProfile(body.Profile, game.Profile, BodyReplacementContext.ExpectedProfile)) return true;
        try
        {
            // Initial ordinary spawning remains entirely upstream. Only a candidate
            // proven by the engine's active replacement can acquire a new lease.
            if (!BodyReplacementContext.IsCreating || !ReferenceEquals(world.MainPlayer, body)) return true;
            var old = Scope.Find(BodyReplacementContext.PreviousBody);
            bool oldOwned = old != null && !old.Retired && !old.Failed
                && ReferenceEquals(old.World, world) && ReferenceEquals(old.Game, game);
            if (!RecoilLegacyScope.MayCreate(BodyReplacementContext.TryOwnCandidate(body),
                BodyReplacementContext.IsCreating, oldOwned, ReferenceEquals(old?.Body, body)))
                throw new InvalidOperationException("Recoil replacement has no exact native construction owner");
            var real = _contract.RecoilInstance.GetValue(null); var cam = _contract.CameraInstance.GetValue(null);
            if (Exists(real) && (old == null || !ReferenceEquals(old.Recoil, real))
                || Exists(cam) && (old == null || !ReferenceEquals(old.Camera, cam)))
                throw new InvalidOperationException("Recoil singleton belongs to an unrelated physical body");
            if (old != null && (!Exists(real) || !Exists(cam)))
                throw new InvalidOperationException("Previous player recoil components unexpectedly disappeared");
            var lease = Scope.Add(body, world, game, old);
            __state = new Creation { Lease = lease };
            if (old != null)
            {
                lease.OldRecoilEnabled = Behaviour(old.Recoil).enabled;
                lease.OldCameraEnabled = Behaviour(old.Camera).enabled;
            }
            lease.OldCache = _contract.CaptureCache();
            if (old != null)
            {
                Enable(old.Recoil, false); Enable(old.Camera, false);
            }
            // Original AddComponent immediately calls Awake. Both static guards must
            // be empty BEFORE either foreign Awake can schedule Destroy(new player).
            _contract.RecoilInstance.SetValue(null, null); _contract.CameraInstance.SetValue(null, null);
            _contract.ClearCache();
            return true;
        }
        catch (Exception ex)
        {
            if (__state?.Lease != null) Rollback(__state.Lease);
            __state = null; Fault(ex);
            throw new InvalidOperationException("Recoil component handoff failed before foreign initialization", ex);
        }
    }
    private static Exception AfterInit(Exception __exception, Player __0, Creation __state)
    {
        if (__state?.Lease == null || _contract == null) return __exception;
        var lease = __state.Lease;
        try
        {
            lease.Recoil = __0.GetComponent(_contract.RecoilType);
            lease.Camera = __0.GetComponent(_contract.CameraType);
            if (__exception != null) { Rollback(lease); return __exception; }
            if (!Exists(lease.Recoil) || !Exists(lease.Camera)
                || !ReferenceEquals(_contract.RecoilInstance.GetValue(null), lease.Recoil)
                || !ReferenceEquals(_contract.CameraInstance.GetValue(null), lease.Camera)
                || !ReferenceEquals(_contract.RecoilPlayer.GetValue(lease.Recoil), __0)
                || _contract.Spring.GetValue(lease.Recoil) == null
                || _contract.CameraSprings.Any(f => f.GetValue(lease.Camera) == null))
                throw new InvalidOperationException("Replacement recoil components did not initialize safely");
            // Creation is not committed until game.LocalPlayer and MainPlayer agree.
            Enable(lease.Recoil, false); Enable(lease.Camera, false);
        }
        catch (Exception ex) { Rollback(lease); Fault(ex); return __exception ?? ex; }
        return __exception;
    }

    private static bool BeforeComponentUpdate(object __instance)
    {
        if (_contract == null) return true;
        var lease = Scope.Leases.FirstOrDefault(l => RecoilLegacyScope.OwnsComponent(l, __instance));
        if (lease == null) return true;
        return lease.Committed && Current(lease);
    }
    private static bool BeforeRecalculate(NewRecoilShotEffect __0, Player.FirearmController __3, out Recalculation __state)
    {
        __state = null;
        if (_contract == null || ReferenceEquals(__3, null)) return true;
        var lease = Scope.ForHands(__3);
        if (lease != null && (!lease.Committed || !Current(lease))) return false;
        if (__3 == null) return lease == null;
        var body = __3.GetComponent<Player>(); lease ??= Scope.Find(body);
        if (lease == null) return true;
        Scope.TrackHands(__3, lease);
        // The constructor may draw before Core has bound its owner; Tick refreshes
        // once both authoritative body pointers and physical hands agree.
        if (!lease.Committed || !Current(lease) || !ReferenceEquals(body.HandsController, __3)
            || !ReferenceEquals(body.ProceduralWeaponAnimation?.Shootingg?.NewShotRecoil, __0)) return false;
        try
        {
            __state = new Recalculation { Lease = lease, Hands = __3, Before = _contract.CaptureCache() };
            _contract.Cache[0].SetValue(null, __3);
            return true;
        }
        catch (Exception ex) { Fault(ex); return false; }
    }
    private static Exception AfterRecalculate(Exception __exception, Recalculation __state)
    {
        if (__state == null || _contract == null) return __exception;
        try
        {
            var lease = __state.Lease;
            if (__exception != null || !Current(lease) || !ReferenceEquals(((Player)lease.Body).HandsController, __state.Hands))
            {
                // A nested unrelated operation may have become the new owner.
                if (ReferenceEquals(_contract.Cache[0].GetValue(null), __state.Hands))
                    _contract.RestoreCache(__state.Before);
            }
            else lease.LastHands = __state.Hands;
        }
        catch (Exception ex) { Fault(ex); return __exception ?? ex; }
        return __exception;
    }

    internal static void Tick()
    {
        if (_contract == null || _refreshing) return;
        try
        {
            _refreshing = true;
            foreach (var lease in Scope.Leases.ToArray())
            {
                if (lease.Retired || lease.Failed) continue;
                if (lease.RecoveryPending)
                {
                    if (RespawnApi.IsReplacing) continue;
                    if (Authoritative(lease.Previous))
                    {
                        Rollback(lease);
                        continue;
                    }
                    if (!Current(lease))
                    {
                        Enable(lease.Recoil, false); Enable(lease.Camera, false);
                        continue;
                    }
                    // The requester's native recovery kept the new body. Only its
                    // authoritative current-body pointers permit this commit.
                    lease.RecoveryPending = false;
                }
                if (!Current(lease))
                {
                    Enable(lease.Recoil, false); Enable(lease.Camera, false);
                    continue;
                }
                if (!lease.Committed)
                {
                    lease.Committed = true;
                    if (lease.Previous != null) Retire(lease.Previous);
                    lease.Previous = null; lease.OldCache = null;
                }
                Enable(lease.Recoil, true); Enable(lease.Camera, true);
                var body = (Player)lease.Body;
                if (body.HandsController is not Player.FirearmController hands
                    || ReferenceEquals(lease.LastHands, hands)) continue;
                Scope.TrackHands(hands, lease);
                // This invokes the normal native recoil calculation exactly once for
                // each physical equipped controller, including a freshly rewired body.
                body.ProceduralWeaponAnimation.Shootingg.OnWeaponParametersChanged();
            }
            Scope.Prune(l => (l.Retired || l.Failed) && !Exists(l.Recoil) && !Exists(l.Camera));
        }
        catch (Exception ex) { Fault(ex); }
        finally { _refreshing = false; }
    }
    private static void Replaced(Player oldBody, Player newBody, string reason)
    {
        var lease = Scope.Find(newBody);
        if (lease != null && ReferenceEquals(lease.Previous?.Body, oldBody)) Tick();
    }
    private static void ReplacementFailed(Player oldBody, string stage)
    {
        foreach (var l in Scope.Leases.Where(l => !l.Committed && !l.Failed && ReferenceEquals(l.Previous?.Body, oldBody)))
            l.RecoveryPending = true; // Core emits this BEFORE the caller decides which body to recover.
    }
    private static void Rollback(RecoilLegacyScope.Lease lease)
    {
        if (lease.Failed || lease.Retired || _contract == null) return;
        var body = lease.Body as Player;
        if (body != null)
        {
            lease.Recoil ??= body.GetComponent(_contract.RecoilType);
            lease.Camera ??= body.GetComponent(_contract.CameraType);
        }
        lease.Failed = true;
        RemoveComponent(lease.Recoil, _contract.RecoilInstance);
        RemoveComponent(lease.Camera, _contract.CameraInstance);
        var old = lease.Previous;
        var game = Game; var world = World;
        // The native pipeline does not itself roll back. Preserve the old components
        // for the requester's recovery, but only while the same game owns the old body.
        bool restore = old != null && !old.Retired && !old.Failed && Exact(game, world)
            && ReferenceEquals(old.Game, game) && ReferenceEquals(old.World, world)
            && ReferenceEquals(game.LocalPlayer, old.Body);
        if (restore)
        {
            if (!Exists(_contract.RecoilInstance.GetValue(null))) _contract.RecoilInstance.SetValue(null, old.Recoil);
            if (!Exists(_contract.CameraInstance.GetValue(null))) _contract.CameraInstance.SetValue(null, old.Camera);
            // Restoring singleton ownership must also work when the recovered old
            // body is still dead (standalone can retry). Its updates stay fenced.
            Enable(old.Recoil, lease.OldRecoilEnabled && Current(old));
            Enable(old.Camera, lease.OldCameraEnabled && Current(old));
            if (lease.OldCache != null && !Exists(_contract.Cache[0].GetValue(null))) _contract.RestoreCache(lease.OldCache);
            old.LastHands = null;
        }
        lease.Previous = null; lease.OldCache = null;
    }
    private static void RemoveComponent(object component, PropertyInfo singleton)
    {
        if (component == null) return;
        if (ReferenceEquals(singleton.GetValue(null), component)) singleton.SetValue(null, null);
        if (!Exists(component)) return;
        Enable(component, false);
        Object.Destroy((Object)component); // Component only. Never Destroy(component.gameObject).
    }
    private static void Retire(RecoilLegacyScope.Lease lease)
    {
        lease.Retired = true;
        RemoveComponent(lease.Recoil, _contract.RecoilInstance);
        RemoveComponent(lease.Camera, _contract.CameraInstance);
    }
    internal static void RetireWorld(GameWorld world)
    {
        if (ReferenceEquals(world, null)) return;
        RetireWhere(l => ReferenceEquals(l.World, world));
    }
    private static void Ended() => RetireWhere(_ => true);
    private static void RetireWhere(Predicate<RecoilLegacyScope.Lease> included)
    {
        if (_contract == null) return;
        try
        {
            var cached = _contract.Cache[0].GetValue(null);
            var owner = Scope.ForHands(cached);
            if (owner != null && included(owner)) _contract.ClearCache();
            foreach (var l in Scope.Leases.Where(l => included(l)).ToArray()) Retire(l);
            // Weak tombstones protect delayed calls after an ordinary raid begins;
            // exact world filtering also fences a delayed old-world pump teardown.
            Scope.Prune(included);
            if (_preparedLease != null && included(_preparedLease)) CompletePreparation();
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static void Fault(Exception ex) { _fault = true; Reject(ex); }
    private static void Reject(Exception ex)
    {
        _failure = "Core cannot safely rebind Recoil Rework Legacy: " + ex.GetBaseException().Message
            + ". Update or remove Recoil Rework Legacy, then restart. Normal spawning remains unchanged.";
        if (_failure == _lastWarning) return;
        _lastWarning = _failure; CoreLog.LogWarning(_failure);
    }
    private static void ClearBinding()
    {
        CompletePreparation();
        Ended();
        Scope.Clear();
        _contract = null; _plugin = null; _version = null;
        if (!_patchAttempted) return;
        _patchAttempted = false;
        try { Patches.UnpatchSelf(); } catch (Exception ex) { Reject(ex); }
    }
    internal static void Shutdown()
    {
        RespawnApi.Replaced -= Replaced; RespawnApi.ReplacementFailed -= ReplacementFailed; RespawnApi.RaidEnded -= Ended;
        ClearBinding(); _initialized = _fault = false; _failure = _lastWarning = null;
    }
}

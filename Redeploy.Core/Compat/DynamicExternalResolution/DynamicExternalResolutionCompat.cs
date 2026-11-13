using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>DERP's private cache must follow the physical body, not a retained corpse.</summary>
internal static class DynamicExternalResolutionCompat
{
    private sealed class Retired { }
    private sealed class Callback { internal object Owner; }
    private static readonly ConditionalWeakTable<object, Retired> RetiredOwners = new();
    private static readonly Harmony Hooks = new("com.maschine.Redeploy.Core.DynamicExternalResolutionCompat");
    [ThreadStatic] private static Stack<Callback> _callbacks;
    private static DynamicExternalResolutionContract _contract;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static string _failure;
    private static bool _hooked, _newerWarned;
    private static LocalPlayer _pendingBody;
    private static object[] _pendingOwners;
    private static Player _cachedBody;
    private static GameWorld _cachedWorld;
    private static LocalGame Game => Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
    private static GameWorld World => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
    private static bool Owned => BodyReplacementContext.OwnsRaid(Game, World);
    private static Player CommittedBody => Owned && ReferenceEquals(Game.LocalPlayer, World.MainPlayer)
        && BodyReplacementContext.OwnsBody(World.MainPlayer) ? World.MainPlayer : null;

    internal static string GetRejectionReason()
    {
        if (!Chainloader.PluginInfos.TryGetValue(DynamicExternalResolutionContract.Guid, out var info))
        {
            if (_contract != null) ClearBinding();
            return null;
        }
        var version = DynamicExternalResolutionContract.Normalize(info?.Metadata?.Version);
        // Admission is queried repeatedly by menus. Successful and rejected identities
        // are cached, with no reflection, Harmony walk or repeated patch work here.
        if (info?.Instance != null && ReferenceEquals(info.Instance, _plugin) && version == _version)
            return _failure;
        if (Owned) return "DERP changed during an owned body-replacement raid";
        ClearBinding();
        if (info?.Instance == null) return "DERP has not finished loading";
        _plugin = info.Instance;
        _version = version;
        try
        {
            _contract = DynamicExternalResolutionContract.Resolve(info.Instance.GetType(), info.Metadata.GUID, info.Metadata.Version);
            _hooked = true;
            Hooks.Patch(_contract.Getter, Hook(nameof(BeforeGetPlayer)));
            foreach (var callback in _contract.Callbacks) Hooks.Patch(callback, Hook(nameof(BeforeCallback)));
            foreach (var native in _contract.Native)
                Hooks.Patch(native, Hook(nameof(BeforeNative)), finalizer: Hook(nameof(AfterNative)));
            if (version > DynamicExternalResolutionContract.Reviewed && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("DERP " + info.Metadata.Version + " is newer than reviewed 1.1.2; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            Unpatch(); _contract = null;
            _failure = "DERP: " + ex.GetBaseException().Message;
        }
        return _failure;
    }
    private static HarmonyMethod Hook(string name) => new(typeof(DynamicExternalResolutionCompat), name) { priority = Priority.First };
    private static void Unpatch()
    {
        if (_hooked) { Hooks.UnpatchSelf(); _hooked = false; }
    }
    private static void ClearBinding()
    {
        Unpatch(); _contract = null; _plugin = null; _version = null; _failure = null;
    }

    internal static void Prepare(LocalGame game, LocalPlayer body)
    {
        if (_contract == null) return;
        if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, body))
            throw new InvalidOperationException("DERP preparation requires Core's exact outgoing body");
        _contract.Verify();
        VerifyHooks();
        _pendingBody = body;
        // Only this body's hierarchy, once per replacement. No scene search or
        // frame poll. Do not tombstone anything until the new body is committed.
        var owners = body.GetComponentsInChildren<OpticSight>(true).Cast<object>().ToList();
        if (body.HandsController is Player.FirearmController gun)
        {
            owners.Add(gun);
            // Native death can detach the held gun from Player.gameObject.
            if (gun.ControllerGameObject != null)
                owners.AddRange(gun.ControllerGameObject.GetComponentsInChildren<OpticSight>(true));
        }
        _pendingOwners = owners.Distinct().ToArray();
    }

    internal static void AbortPreparation() { _pendingBody = null; _pendingOwners = null; }
    internal static void EndAttempt()
    {
        if (_contract == null || _pendingBody == null) { AbortPreparation(); return; }
        try
        {
            var current = CommittedBody;
            if (current == null || ReferenceEquals(current, _pendingBody)) return;
            foreach (var owner in _pendingOwners)
                if (!RetiredOwners.TryGetValue(owner, out _)) RetiredOwners.Add(owner, new Retired());
            SetCache(current);
        }
        finally { AbortPreparation(); }
    }
    private static void SetCache(Player body)
    {
        if (!ReferenceEquals(_contract.Player.GetValue(null), body)) _contract.Player.SetValue(null, body);
        _cachedBody = body; _cachedWorld = World;
    }
    private static bool BeforeGetPlayer(ref Player __result)
    {
        if (_contract == null || !Owned) return true;
        // During construction neither the old nor partially built body can
        // authorize writes to the shared camera. Native Create initializes the
        // successor camera from its normal graphics settings.
        __result = BodyReplacementContext.IsCreating ? null : CommittedBody;
        if (__result != null) SetCache(__result);
        return false;
    }
    private static void BeforeNative(object __instance, out Callback __state)
    {
        __state = null;
        // EFT pools optic objects. A genuinely reused object held by the new
        // current player no longer belongs to its old tombstone, even in a
        // subsequent ordinary raid. Physical held-root identity is required.
        if (RetiredOwners.TryGetValue(__instance, out _) && HeldByCurrentPlayer(__instance))
            RetiredOwners.Remove(__instance);
        if (!Owned && !RetiredOwners.TryGetValue(__instance, out _)) return;
        __state = new Callback { Owner = __instance };
        (_callbacks ??= new Stack<Callback>()).Push(__state);
    }
    private static bool HeldByCurrentPlayer(object owner)
    {
        var body = World?.MainPlayer;
        if (body == null || Game == null || !body.IsYourPlayer || !ReferenceEquals(Game.LocalPlayer, body)
            || !ReferenceEquals(body.GameWorld, World) || body.HandsController is not Player.FirearmController gun)
            return false;
        return ReferenceEquals(owner, gun) || owner is OpticSight optic && optic != null
            && gun.ControllerGameObject != null && optic.transform.IsChildOf(gun.ControllerGameObject.transform);
    }
    private static void AfterNative(Callback __state)
    {
        if (__state != null && _callbacks?.Count > 0 && ReferenceEquals(_callbacks.Peek(), __state)) _callbacks.Pop();
    }
    private static bool BeforeCallback(MethodBase __originalMethod)
    {
        if (_callbacks?.Count > 0 && RetiredOwners.TryGetValue(_callbacks.Peek().Owner, out _)) return false;
        if (!Owned) return true;
        var body = BodyReplacementContext.IsCreating ? null : CommittedBody;
        if (body == null) return false;
        // CurrentScope indexes an empty list with knife/empty hands. DERP reads
        // it before testing IsAiming. Limit this guard to a Core-owned raid.
        if (__originalMethod == _contract.Callbacks[1]) return true;
        var animation = body.ProceduralWeaponAnimation;
        return animation != null && animation.ScopeAimTransforms?.Count > 0 && animation.AimIndex >= 0
            && animation.ScopeAimTransforms[animation.AimIndex % animation.ScopeAimTransforms.Count] != null;
    }
    internal static void RetireWorld(GameWorld world)
    {
        if (!ReferenceEquals(world, _cachedWorld)) return;
        if (_contract != null && ReferenceEquals(_contract.Player.GetValue(null), _cachedBody))
            _contract.Player.SetValue(null, null);
        _cachedBody = null; _cachedWorld = null; AbortPreparation();
    }
    private static void VerifyHooks()
    {
        void Check(MethodInfo target, string hook, bool finalizer = false)
        {
            var info = Harmony.GetPatchInfo(target);
            var entries = finalizer ? info?.Finalizers : info?.Prefixes;
            var expected = typeof(DynamicExternalResolutionCompat).GetMethod(hook, BindingFlags.Static | BindingFlags.NonPublic);
            if (entries?.Count(p => p.owner == Hooks.Id && p.PatchMethod == expected) != 1)
                throw new MissingMethodException("DERP compatibility hook missing: " + hook);
        }
        Check(_contract.Getter, nameof(BeforeGetPlayer));
        foreach (var callback in _contract.Callbacks) Check(callback, nameof(BeforeCallback));
        foreach (var native in _contract.Native)
        { Check(native, nameof(BeforeNative)); Check(native, nameof(AfterNative), true); }
    }
}

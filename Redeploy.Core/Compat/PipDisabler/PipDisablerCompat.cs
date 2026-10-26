using System;
using System.Collections;
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
using UnityEngine;

namespace Redeploy.Core.Compat;

/// <summary>Scope and cut-mesh state belong to a physical held optic, never a profile ID.</summary>
internal static class PipDisablerCompat
{
    private sealed class OpticLease
    {
        internal WeakReference<Player> Body;
        internal WeakReference<GameWorld> World;
        internal bool Matches(Player body, GameWorld world) => Body.TryGetTarget(out var b) && ReferenceEquals(b, body)
            && World.TryGetTarget(out var w) && ReferenceEquals(w, world);
    }
    private sealed class CacheLease { internal object Profile; internal Player Body; internal GameWorld World; internal GameObject Root; }
    private sealed class Callback { internal OpticSight Optic; internal bool Ran; }
    private static readonly ConditionalWeakTable<OpticSight, OpticLease> Optics = new();
    // Observation is not a lease: before Core's first unregister this table only
    // remembers where a genuinely held local optic came from. Native death can
    // detach the gun from Player.gameObject before Core gets to Prepare.
    private static readonly ConditionalWeakTable<Transform, OpticLease> ObservedRoots = new();
    private static readonly List<CacheLease> Leases = new();
    private static readonly Harmony Hooks = new("com.maschine.Redeploy.Core.PipDisablerCompat");
    private static readonly List<MethodInfo> Unregisters = new();
    [ThreadStatic] private static Stack<Callback> _callbacks;
    private static PipDisablerContract _contract;
    private static BaseUnityPlugin _plugin;
    private static LocalGame _game;
    private static GameWorld _world;
    private static LocalPlayer _pending;
    private static Player _observedBody;
    private static System.Version _version;
    private static string _failure;
    private static bool _fault, _newerWarned;
    private static LocalGame Game => Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
    private static GameWorld World => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
    private static bool Current => _world != null && ReferenceEquals(_world, World) && ReferenceEquals(_game, Game)
        && BodyReplacementContext.OwnsRaid(_game, _world);
    private static Player CurrentBody => Current && !BodyReplacementContext.IsCreating
        && ReferenceEquals(_game.LocalPlayer, _world.MainPlayer) && BodyReplacementContext.OwnsBody(_world.MainPlayer)
        ? _world.MainPlayer : null;

    internal static string GetRejectionReason()
    {
        if (_fault) return _failure;
        if (!Chainloader.PluginInfos.TryGetValue(PipDisablerContract.Guid, out var info))
        {
            if (_world != null) return Fail(new InvalidOperationException("plugin disappeared during an owned raid"));
            ClearBinding(); return _failure = null;
        }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            if (_contract != null && ReferenceEquals(info.Instance, _plugin) && _version == PipDisablerContract.Normalize(info.Metadata?.Version))
            { VerifyHooks(); return _failure; }
            if (_world != null) throw new InvalidOperationException("plugin changed during an owned raid");
            ClearBinding();
            _contract = PipDisablerContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version);
            _plugin = info.Instance;
            _version = PipDisablerContract.Normalize(info.Metadata.Version);
            foreach (var target in new[] { _contract.Enable, _contract.Disable, _contract.Bypass }) Patch(target, nameof(BeforeOptic));
            Patch(_contract.Check, nameof(BeforeUpdate), nameof(AfterUpdate)); Patch(_contract.Tick, nameof(BeforeUpdate));
            Patch(_contract.GetCache, nameof(BeforeCache), nameof(AfterCacheSelection));
            Patch(_contract.Apply, nameof(BeforeOptic), nameof(AfterApply));
            foreach (var name in new[] { "OnEnable", "OnDisable" })
            {
                var method = AccessTools.DeclaredMethod(typeof(OpticSight), name);
                if (method == null || method.IsStatic || method.ReturnType != typeof(void) || method.GetParameters().Length != 0)
                    throw new MissingMethodException("native OpticSight " + name);
                Hooks.Patch(method, Hook(nameof(BeforeNative)), Hook(nameof(AfterNative)), finalizer: Hook(nameof(FinalNative)));
            }
            VerifyHooks();
            if (PipDisablerContract.Normalize(info.Metadata.Version) > PipDisablerContract.Reviewed && !_newerWarned)
            { _newerWarned = true; CoreLog.LogWarning("PiP Disabler " + info.Metadata.Version + " is newer than reviewed 2.1.0; APIs match, native compatibility is unverified."); }
            CoreLog.LogDebug("PiP Disabler: physical optic ownership and bounded cut-mesh retirement ready.");
            _failure = null;
        }
        catch (Exception ex)
        {
            if (_world != null) Fail(ex);
            else { ClearBinding(); _failure = "PiP Disabler: " + ex.GetBaseException().Message; }
        }
        return _failure;
    }
    private static HarmonyMethod Hook(string name) => new(typeof(PipDisablerCompat), name) { priority = Priority.First };
    private static void Patch(MethodInfo target, string prefix, string postfix = null)
        => Hooks.Patch(target, prefix == null ? null : Hook(prefix), postfix == null ? null : Hook(postfix));
    private static void VerifyHooks()
    {
        void Check(MethodInfo method, string name, int kind = 0)
        {
            var p = Harmony.GetPatchInfo(method); var entries = kind == 0 ? p?.Prefixes : kind == 1 ? p?.Postfixes : p?.Finalizers;
            var hook = AccessTools.DeclaredMethod(typeof(PipDisablerCompat), name);
            if (entries?.Count(x => x.owner == Hooks.Id && x.PatchMethod == hook) != 1)
                throw new MissingMethodException("PiP compatibility hook missing: " + name);
        }
        foreach (var target in new[] { _contract.Enable, _contract.Disable, _contract.Bypass }) Check(target, nameof(BeforeOptic));
        Check(_contract.Check, nameof(BeforeUpdate)); Check(_contract.Check, nameof(AfterUpdate), 1); Check(_contract.Tick, nameof(BeforeUpdate));
        Check(_contract.GetCache, nameof(BeforeCache)); Check(_contract.GetCache, nameof(AfterCacheSelection), 1);
        Check(_contract.Apply, nameof(BeforeOptic)); Check(_contract.Apply, nameof(AfterApply), 1);
        foreach (var name in new[] { "OnEnable", "OnDisable" })
        {
            var target = AccessTools.DeclaredMethod(typeof(OpticSight), name);
            Check(target, nameof(BeforeNative)); Check(target, nameof(AfterNative), 1); Check(target, nameof(FinalNative), 2);
        }
        foreach (var target in Unregisters) Check(target, nameof(BeforeUnregister));
    }
    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        if (_contract == null) return;
        if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, oldBody) || !BodyReplacementContext.OwnsBody(oldBody))
            throw new InvalidOperationException("PiP preparation requires Core's exact old body");
        // Validate every field touched by ForceExit before any destructive work. A foreign
        // bot/preload state is an explicit blocker, never authority to reset global state.
        ValidateVisualOwnership(oldBody);
        var method = game.GameWorld.GetType().GetMethod(nameof(GameWorld.UnregisterPlayer), new[] { typeof(IPlayer) });
        method = method?.DeclaringType?.GetMethod(nameof(GameWorld.UnregisterPlayer), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null, new[] { typeof(IPlayer) }, null);
        if (method == null || method.ReturnType != typeof(void) || method.GetMethodBody() == null
            || method.GetBaseDefinition() != typeof(GameWorld).GetMethod(nameof(GameWorld.UnregisterPlayer), new[] { typeof(IPlayer) }))
            throw new MissingMethodException("PiP cannot verify native world unregister");
        if (!Unregisters.Contains(method)) { Patch(method, nameof(BeforeUnregister)); Unregisters.Add(method); }
        VerifyHooks(); _pending = oldBody;
    }
    internal static void AbortPreparation() => _pending = null;
    private static void BeforeUnregister(GameWorld __instance, IPlayer __0)
    {
        if (_contract == null || _pending == null || !ReferenceEquals(_pending, __0)
            || !BodyReplacementContext.IsCreating || !ReferenceEquals(BodyReplacementContext.PreviousBody, _pending)
            || !BodyReplacementContext.OwnsRaid(Game, __instance)) return;
        var old = _pending; _pending = null;
        // Repeat read-only validation at the actual commit boundary; preparation may
        // span frames. Throwing here is handled by Core's replacement recovery.
        ValidateVisualOwnership(old);
        _game = Game; _world = __instance;
        foreach (var optic in old.GetComponentsInChildren<OpticSight>(true)) Adopt(optic, old);
        foreach (var field in new[] { _contract.Active, _contract.Last })
            if (field.GetValue(null) is OpticSight optic && optic != null) Adopt(optic, old);
        Capture(old);
        RetireBody(old);
    }
    private static void BeforeNative(OpticSight __instance, out Callback __state)
    {
        __state = null;
        if (!Current) return;
        __state = new Callback { Optic = __instance };
        (_callbacks ??= new Stack<Callback>()).Push(__state);
    }
    private static void AfterNative(bool __runOriginal, Callback __state) { if (__state != null) __state.Ran = __runOriginal; }
    private static void FinalNative(Callback __state)
    {
        if (__state != null && _callbacks?.Count > 0 && ReferenceEquals(_callbacks.Peek(), __state)) _callbacks.Pop();
    }
    private static bool BeforeOptic(OpticSight __0, MethodBase __originalMethod)
    {
        if (_contract == null) return true;
        ObserveLocalOptic(__0);
        var body = CurrentBody;
        if (Current)
        {
            if (_fault || body == null || __0 == null) return false;
            if (_callbacks?.Count > 0 && ReferenceEquals(_callbacks.Peek().Optic, __0) && !_callbacks.Peek().Ran) return false;
            if (Mounted(body, __0.transform)) { Adopt(__0, body); return true; }
            return __originalMethod == _contract.Disable && ReferenceEquals(_contract.Active.GetValue(null), __0)
                && Optics.TryGetValue(__0, out var lease) && lease.Matches(body, World);
        }
        // A pooled optic reused by an ordinary raid is no longer a delayed old callback.
        if (__0 == null) return true;
        if (Mounted(World?.MainPlayer, __0.transform)) { Optics.Remove(__0); return true; }
        return !Optics.TryGetValue(__0, out _);
    }
    private static bool BeforeUpdate() => !Current || (!_fault && CurrentBody != null);
    private static void AfterUpdate(bool __runOriginal)
    {
        if (!__runOriginal || _contract == null) return;
        ObserveLocalOptic(_contract.Active.GetValue(null) as OpticSight);
        ObserveLocalOptic(_contract.Last.GetValue(null) as OpticSight);
    }
    private static bool BeforeCache(Transform __0) => !Current || (!_fault && Mounted(CurrentBody, __0));
    private static void AfterCacheSelection(bool __runOriginal, object __result)
    {
        if (!__runOriginal || !Current || CurrentBody == null || __result == null) return;
        try
        {
            var prior = Leases.FirstOrDefault(l => ReferenceEquals(l.Profile, __result));
            if (prior == null || ReferenceEquals(prior.Root, _contract.Root.GetValue(__result))) return;
            // Upstream first changes WeaponRoot, then rebinds Entries in ApplyForOptic.
            // Restore the still-old, leased filters before that handover; otherwise a
            // retained old model can keep a cut mesh that the new model later destroys.
            if (prior.Root != null) ValidateEntries(prior.Profile, prior.Root);
            _contract.RestoreMeshes.Invoke(null, new[] { prior.Profile });
        }
        catch (Exception ex) { Fail(ex); }
    }
    private static void AfterApply(bool __runOriginal)
    {
        if (!__runOriginal || !Current || CurrentBody == null || _fault) return;
        try { Capture(CurrentBody); } catch (Exception ex) { Fail(ex); }
    }
    private static bool Under(Transform value, Transform root) => value != null && root != null && value.IsChildOf(root);
    private static bool Mounted(Player body, Transform value) => body != null && body.IsYourPlayer && ReferenceEquals(body.GameWorld, World)
        && body.HandsController is Player.FirearmController gun && gun.ControllerGameObject != null
        && Under(value, gun.ControllerGameObject.transform);
    private static void ObserveLocalOptic(OpticSight optic)
    {
        var body = World?.MainPlayer;
        if (optic == null || body == null || Game == null || !ReferenceEquals(Game.GameWorld, World)
            || !ReferenceEquals(Game.LocalPlayer, body) || !ReferenceEquals(Game.Profile, body.Profile)
            || !Mounted(body, optic.transform)) return;
        var root = body.HandsController.ControllerGameObject.transform;
        if (ObservedRoots.TryGetValue(root, out var observed) && observed.Matches(body, World)) return;
        ObservedRoots.Remove(root);
        ObservedRoots.Add(root, new OpticLease { Body = new WeakReference<Player>(body), World = new WeakReference<GameWorld>(World) });
    }
    private static bool BodyOwns(Player body, Transform value)
    {
        if (body == null || value == null) return false;
        // Reparented retained guns may have no Player ancestor. A gun now parented
        // to someone else is never authorized by an earlier local observation.
        var currentOwner = value.GetComponentInParent<Player>(true);
        if (!ReferenceEquals(currentOwner, null) && !ReferenceEquals(currentOwner, body)) return false;
        if (Under(value, body.gameObject.transform)) return true;
        for (var root = value; root != null; root = root.parent)
            if (ObservedRoots.TryGetValue(root, out var observation) && observation.Matches(body, body.GameWorld)) return true;
        return false;
    }
    private static void Adopt(OpticSight optic, Player body)
    {
        if (optic == null) return;
        Optics.Remove(optic); Optics.Add(optic, new OpticLease { Body = new WeakReference<Player>(body), World = new WeakReference<GameWorld>(body.GameWorld) });
    }
    private static void ValidateVisualOwnership(Player body)
    {
        foreach (var field in new[] { _contract.Active, _contract.Last })
            if (field.GetValue(null) is OpticSight optic && (optic == null || !BodyOwns(body, optic.transform)))
                throw new InvalidOperationException("PiP scope state belongs to a different physical body or preload (" + field.Name
                    + ", " + (optic == null ? "destroyed optic" : "live optic without verified local weapon provenance") + ")");
        if (_contract.ReticleOptic.GetValue(null) is Transform reticle && (reticle == null || !BodyOwns(body, reticle)))
            throw new InvalidOperationException("PiP reticle belongs to another physical body");
        foreach (var entry in (IEnumerable)_contract.Hidden.GetValue(null))
            if (_contract.HiddenRenderer.GetValue(entry) is not Renderer renderer || renderer == null || !BodyOwns(body, renderer.transform))
                throw new InvalidOperationException("PiP hidden lens ownership cannot be established");
        foreach (var field in new[] { _contract.Lights, _contract.Spheres })
            foreach (DictionaryEntry entry in (IDictionary)field.GetValue(null))
                if (entry.Key is not GameObject root || root == null || !BodyOwns(body, root.transform))
                    throw new InvalidOperationException("PiP hidden weapon effect ownership cannot be established");
    }
    private static void Capture(Player body)
    {
        foreach (DictionaryEntry weapon in (IDictionary)_contract.Caches.GetValue(null))
            foreach (DictionaryEntry entry in (IDictionary)_contract.Profiles.GetValue(weapon.Value))
            {
                var profile = entry.Value;
                if (_contract.Root.GetValue(profile) is not GameObject root || root == null || !BodyOwns(body, root.transform)) continue;
                ValidateEntries(profile, root);
                var prior = Leases.FirstOrDefault(l => ReferenceEquals(l.Profile, profile));
                if (prior != null)
                {
                    // Foreign code can reuse the same profile-key object for a new
                    // physical gun. The root and current body, not the key, own it.
                    prior.Body = body; prior.World = body.GameWorld; prior.Root = root;
                }
                else Leases.Add(new CacheLease { Profile = profile, Body = body, World = body.GameWorld, Root = root });
            }
    }
    private static void ValidateEntries(object profile, GameObject root)
    {
        foreach (var entry in (IEnumerable)_contract.Entries.GetValue(profile))
            if (_contract.Filter.GetValue(entry) is MeshFilter filter && filter != null && !Under(filter.transform, root.transform))
                throw new InvalidOperationException("PiP cut mesh escaped its physical weapon root");
    }
    private static void RetireBody(Player body)
    {
        ValidateVisualOwnership(body);
        // All active reticle/lens/effect state was proven to be this body above.
        bool ownsVisual = _contract.Active.GetValue(null) is OpticSight active && active != null
            || _contract.Last.GetValue(null) is OpticSight last && last != null
            || _contract.ReticleOptic.GetValue(null) is Transform optic && optic != null;
        if (ownsVisual)
        {
            _contract.ForceExit.Invoke(null, null); _contract.ClearRestore.Invoke(null, null);
            _contract.ForceCameraRestore.Invoke(null, null);
            _contract.Active.SetValue(null, null); _contract.Last.SetValue(null, null);
        }
        if (ReferenceEquals(_contract.Movement.GetValue(null), body.MovementContext)) _contract.Unsubscribe.Invoke(null, null);
        if (ReferenceEquals(_contract.Inventory.GetValue(null), body.InventoryController)) _contract.Unbind.Invoke(null, null);
        foreach (var lease in Leases.Where(l => ReferenceEquals(l.Body, body)).ToArray()) RetireCache(lease);
    }
    private static void RetireCache(CacheLease lease)
    {
        if (!ReferenceEquals(_contract.Root.GetValue(lease.Profile), lease.Root)) { Leases.Remove(lease); return; }
        if (lease.Root != null) ValidateEntries(lease.Profile, lease.Root);
        _contract.RestoreMeshes.Invoke(null, new[] { lease.Profile });
        _contract.DestroyMeshes.Invoke(null, new[] { lease.Profile });
        ((IList)_contract.Entries.GetValue(lease.Profile)).Clear();
        var caches = (IDictionary)_contract.Caches.GetValue(null);
        foreach (var weaponKey in caches.Keys.Cast<object>().ToArray())
        {
            var profiles = (IDictionary)_contract.Profiles.GetValue(caches[weaponKey]);
            var keys = profiles.Keys.Cast<object>().Where(k => ReferenceEquals(profiles[k], lease.Profile)).ToArray();
            foreach (var key in keys) profiles.Remove(key);
            if (keys.Length > 0 && profiles.Count == 0) caches.Remove(weaponKey);
        }
        if (ReferenceEquals(_contract.Current.GetValue(null), lease.Profile))
        { _contract.Current.SetValue(null, null); _contract.CurrentId.SetValue(null, null); }
        Leases.Remove(lease);
    }
    internal static void Tick()
    {
        if (_pending != null && !BodyReplacementContext.IsCreating) _pending = null;
        if (!Current || CurrentBody == null || _fault || ReferenceEquals(_observedBody, CurrentBody)) return;
        try { Capture(CurrentBody); _observedBody = CurrentBody; } catch (Exception ex) { Fail(ex); }
    }
    internal static void RetireWorld(GameWorld world)
    {
        if (_contract == null || !ReferenceEquals(world, _world)) return;
        try
        {
            // Never let late old-world disposal touch a new world's global renderer.
            if (ReferenceEquals(World, world) && world.MainPlayer != null && BodyReplacementContext.OwnsBody(world.MainPlayer)) RetireBody(world.MainPlayer);
            foreach (var lease in Leases.Where(l => ReferenceEquals(l.World, world)).ToArray()) RetireCache(lease);
        }
        catch (Exception ex) { Fail(ex); }
        finally
        {
            // A changed foreign contract may prevent safe teardown. Do not keep the
            // entire retired world alive merely to retry unsafe foreign cleanup.
            Leases.RemoveAll(lease => ReferenceEquals(lease.World, world));
            _world = null; _game = null; _pending = null; _observedBody = null;
        }
    }
    private static string Fail(Exception ex)
    {
        _fault = true;
        var message = "PiP Disabler body compatibility stopped: " + ex.GetBaseException().Message;
        if (_failure != message) CoreLog.LogWarning(message);
        return _failure = message;
    }
    private static void ClearBinding()
    { Hooks.UnpatchSelf(); Unregisters.Clear(); _contract = null; _plugin = null; _version = null; _pending = null; }
}

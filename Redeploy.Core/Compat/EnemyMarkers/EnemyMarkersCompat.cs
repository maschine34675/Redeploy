using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Preserve Enemy Markers' physical bot markers across Core body/FPS-camera replacements.</summary>
internal static class EnemyMarkersCompat
{
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.EnemyMarkersCompat");
    private static readonly EnemyMarkersScope Scope = new();
    private static readonly List<MethodInfo> UnregisterTargets = new();
    private static EnemyMarkersContract _contract;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static bool _patched, _fault, _newerWarned;
    private static string _failure, _warning;
    private static LocalGame Game => Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
    private static GameWorld World => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
    private static bool CurrentWorld => Scope.Enabled && ReferenceEquals(Scope.Game, Game)
        && ReferenceEquals(Scope.World, World) && BodyReplacementContext.OwnsRaid(Game, World);

    internal static string GetRejectionReason()
    {
        if (_fault) return _failure;
        if (!Chainloader.PluginInfos.TryGetValue(EnemyMarkersContract.PluginGuid, out var info))
        {
            if (Scope.Enabled) { Fault(new InvalidOperationException("Enemy Markers disappeared during an owned raid")); return _failure; }
            ClearBinding(); return _failure = null;
        }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Enemy Markers has not finished loading");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance) && _version == EnemyMarkersContract.Normalize(info.Metadata?.Version))
            { _contract.Verify(); VerifyPatches(); return _failure; }
            if (Scope.Enabled) throw new InvalidOperationException("Enemy Markers changed during an owned raid");
            ClearBinding();
            _contract = EnemyMarkersContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version, out _version);
            _plugin = info.Instance; _patched = true;
            Patch(_contract.Unregister, nameof(BeforeUnregister));
            UnregisterTargets.Add(_contract.Unregister);
            Patch(_contract.PersonAdded, nameof(BeforePersonAdded), nameof(AfterPersonAdded));
            Patch(_contract.PersonGone, nameof(BeforePersonGone));
            Patch(_contract.Initialize, null, nameof(AfterInitialize));
            Patch(_contract.Destroy, nameof(BeforeDestroy));
            Patch(_contract.BillboardUpdate, nameof(BeforeBillboard));
            VerifyPatches(); _failure = null;
            CoreLog.LogDebug("Enemy Markers: physical local-body exclusion and persistent billboard camera handover ready.");
            if (_version > EnemyMarkersContract.ReviewedVersion && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("Enemy Markers " + _version + " is newer than reviewed 1.4.0; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            if (Scope.Enabled) Fault(ex);
            else { ClearBinding(); Reject(ex); }
        }
        return _failure;
    }
    private static void Patch(MethodInfo target, string prefix, string postfix = null)
        => Patches.Patch(target, prefix == null ? null : Hook(prefix), postfix == null ? null : Hook(postfix));
    private static HarmonyMethod Hook(string name) => new(typeof(EnemyMarkersCompat), name) { priority = Priority.First };
    private static void VerifyPatches()
    {
        void Check(MethodInfo target, string name, bool prefix = true)
        {
            var info = Harmony.GetPatchInfo(target);
            var entries = prefix ? info?.Prefixes : info?.Postfixes;
            var hook = typeof(EnemyMarkersCompat).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            if (entries?.Count(p => p.owner == Patches.Id && p.PatchMethod == hook) != 1)
                throw new MissingMethodException("Enemy Markers compatibility hook is missing: " + name);
        }
        foreach (var target in UnregisterTargets) Check(target, nameof(BeforeUnregister));
        Check(_contract.PersonAdded, nameof(BeforePersonAdded)); Check(_contract.PersonAdded, nameof(AfterPersonAdded), false);
        Check(_contract.PersonGone, nameof(BeforePersonGone)); Check(_contract.Initialize, nameof(AfterInitialize), false);
        Check(_contract.Destroy, nameof(BeforeDestroy));
        Check(_contract.BillboardUpdate, nameof(BeforeBillboard));
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        if (_contract == null) return;
        bool authorized = BodyReplacementContext.IsCreating && BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            && BodyReplacementContext.OwnsBody(oldBody) && ReferenceEquals(BodyReplacementContext.PreviousBody, oldBody)
            && ReferenceEquals(game?.LocalPlayer, oldBody) && ReferenceEquals(game?.GameWorld?.MainPlayer, oldBody);
        if (!authorized) throw new InvalidOperationException("Enemy Markers requires Core's exact old body");
        // ClientGameWorld may return before calling the base method when death has
        // already unregistered the old body. Intercept the actual virtual entry too.
        var unregister = game.GameWorld.GetType().GetMethod(nameof(GameWorld.UnregisterPlayer), new[] { typeof(IPlayer) });
        // Patch the declared method even when the concrete world inherits it.
        unregister = unregister?.DeclaringType?.GetMethod(nameof(GameWorld.UnregisterPlayer),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new[] { typeof(IPlayer) }, null);
        if (unregister == null || unregister.IsStatic || unregister.ReturnType != typeof(void)
            || unregister.GetMethodBody() == null || unregister.GetBaseDefinition() != _contract.Unregister)
            throw new MissingMethodException("Enemy Markers cannot verify the runtime world unregister entry");
        if (!UnregisterTargets.Contains(unregister))
        {
            Patch(unregister, nameof(BeforeUnregister));
            UnregisterTargets.Add(unregister);
        }
        VerifyPatches();
        Scope.Prepare(game, game?.GameWorld, oldBody, authorized);
        // Read only until native unregister: a refused preparation grants no marker authority.
        foreach (var component in game.GameWorld.gameObject.GetComponents(_contract.ComponentType))
            ValidateComponent(component, game.GameWorld);
    }
    internal static void AbortPreparation() => Scope.AbortPreparation();
    private static void BeforeUnregister(GameWorld __instance, IPlayer __0)
    {
        if (_contract == null || __0 is not Player body) return;
        try
        {
            if (Scope.Start(Game, __instance, body, BodyReplacementContext.IsCreating
                && ReferenceEquals(BodyReplacementContext.PreviousBody, body)
                && BodyReplacementContext.OwnsBody(body) && BodyReplacementContext.OwnsRaid(Game, __instance)))
                CaptureWorld(__instance);
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static void ValidateComponent(Component component, GameWorld world)
    {
        if (component == null || !ReferenceEquals(component.gameObject, world.gameObject))
            throw new InvalidOperationException("Enemy Markers component is attached to another world");
        var declared = _contract.World.GetValue(component);
        // Its Start may be scheduled after an Arena initial kit replacement.
        if (declared != null && !ReferenceEquals(declared, world))
            throw new InvalidOperationException("Enemy Markers caches another world");
        if (_contract.Markers.GetValue(component) is not IDictionary)
            throw new InvalidOperationException("Enemy Markers marker table is missing");
    }
    private static bool Observe(object instance)
    {
        if (Scope.OwnsComponent(instance)) return Scope.ActiveComponent(instance) && CurrentWorld;
        if (!CurrentWorld || instance is not Component component || component == null
            || !ReferenceEquals(component.gameObject, World.gameObject)
            || !ReferenceEquals(_contract.World.GetValue(instance), World)) return false;
        Scope.AdoptComponent(instance, Game, World, true);
        return true;
    }
    private static void CaptureWorld(GameWorld world)
    {
        foreach (var component in world.gameObject.GetComponents(_contract.ComponentType))
        {
            ValidateComponent(component, world);
            Scope.AdoptComponent(component, Game, world, CurrentWorld);
            CaptureMarkers(component);
        }
    }
    private static void CaptureMarkers(object component)
    {
        var markers = (IDictionary)_contract.Markers.GetValue(component);
        foreach (DictionaryEntry entry in markers) CaptureMarker(component, entry.Value);
    }
    private static void CaptureMarker(object component, object info)
    {
        var marker = _contract.Marker.GetValue(info) as GameObject;
        if (marker == null) return;
        var billboard = marker.GetComponent(_contract.BillboardType);
        if (billboard == null) throw new InvalidOperationException("Enemy Markers marker has no billboard");
        Scope.AdoptBillboard(component, billboard);
    }
    private static bool BeforePersonAdded(object __instance, IPlayer __0, out bool __state)
    {
        __state = false;
        try
        {
            if (!Observe(__instance)) return !Scope.OwnsComponent(__instance);
            bool local = __0 is Player body && ReferenceEquals(body.GameWorld, World)
                && (BodyReplacementContext.OwnsBody(body) || BodyReplacementContext.TryOwnCandidate(body));
            return __state = !_fault && Scope.AllowsPerson(__instance, CurrentWorld, local);
        }
        catch (Exception ex) { Fault(ex); return !Scope.OwnsComponent(__instance); }
    }
    private static void AfterPersonAdded(object __instance, IPlayer __0, bool __state)
    {
        if (!__state || !Scope.ActiveComponent(__instance) || !CurrentWorld) return;
        try
        {
            var markers = (IDictionary)_contract.Markers.GetValue(__instance);
            if (__0 != null && markers.Contains(__0)) CaptureMarker(__instance, markers[__0]);
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static bool BeforePersonGone(object __instance)
        => !Scope.OwnsComponent(__instance) || (Scope.ActiveComponent(__instance) && CurrentWorld);
    private static void AfterInitialize(object __instance)
    {
        try { if (Observe(__instance)) CaptureMarkers(__instance); }
        catch (Exception ex) { Fault(ex); }
    }
    private static void BeforeDestroy(object __instance)
    {
        if (!Scope.OwnsComponent(__instance)) return;
        // Retired-world callbacks are fenced, so rows may still contain destroyed
        // Unity player wrappers. Upstream teardown reads player.name even in its
        // catch block and removes the wrong death event. Finish only the owned
        // marker rows first; its original coroutine/world/UI/logger cleanup still runs.
        var markers = (IDictionary)_contract.Markers.GetValue(__instance);
        var entries = new DictionaryEntry[markers.Count];
        markers.CopyTo(entries, 0);
        var handler = (Action<IPlayer>)Delegate.CreateDelegate(typeof(Action<IPlayer>), __instance, _contract.PersonGone);
        foreach (var entry in entries)
        {
            try
            {
                // The event is a managed field; unsubscribe through the interface
                // even if the Unity object has already been destroyed.
                if (entry.Key is IPlayer player) player.OnIPlayerDeadOrUnspawn -= handler;
            }
            catch (Exception ex) { CoreLog.LogWarning("Enemy Markers owned death-handler cleanup: " + ex.GetBaseException().Message); }
            try
            {
                if (_contract.Marker.GetValue(entry.Value) is GameObject marker && marker != null)
                    UnityEngine.Object.Destroy(marker);
            }
            catch (Exception ex) { CoreLog.LogWarning("Enemy Markers owned marker cleanup: " + ex.GetBaseException().Message); }
            markers.Remove(entry.Key);
        }
    }
    private static Camera CurrentCamera()
    {
        var body = World?.MainPlayer;
        if (!CurrentWorld || BodyReplacementContext.IsCreating || body == null || !body.IsYourPlayer
            || !BodyReplacementContext.OwnsBody(body) || !ReferenceEquals(Game.LocalPlayer, body)
            || !World.RegisteredPlayers.Contains(body) || !Singleton<PlayerCameraController>.Instantiated
            || !ReferenceEquals(Singleton<PlayerCameraController>.Instance.Player, body)) return null;
        var camera = CameraManager.Instance?.Camera;
        return camera != null && ReferenceEquals(camera, Camera.main) ? camera : null;
    }
    private static bool BeforeBillboard(object __instance)
    {
        if (!Scope.OwnsBillboard(__instance)) return true;
        try
        {
            var camera = _fault ? null : CurrentCamera();
            if (!Scope.AllowsBillboard(__instance, CurrentWorld, camera != null)) return false;
            if (!ReferenceEquals(_contract.BillboardCamera.GetValue(__instance), camera))
                _contract.BillboardCamera.SetValue(__instance, camera);
            // Retain upstream rotation semantics, including roll. Do not rebuild marker state.
            return true;
        }
        catch (Exception ex) { Fault(ex); return false; }
    }
    internal static void Tick()
    {
        if (!BodyReplacementContext.IsCreating && Scope.Prepared) Scope.AbortPreparation();
        if (_contract == null || !Scope.Enabled) return;
        if (!CurrentWorld) RetireWorld(Scope.World as GameWorld);
    }
    internal static void RetireWorld(GameWorld world) => Scope.RetireWorld(world);
    private static void Fault(Exception ex) { _fault = true; Reject(ex); }
    private static void Reject(Exception ex)
    {
        _failure = "Core cannot safely replace the player with Enemy Markers: " + ex.GetBaseException().Message
            + ". Update or remove Enemy Markers, then restart. Ordinary raids remain available.";
        if (_warning == _failure) return;
        _warning = _failure; CoreLog.LogWarning(_failure);
    }
    private static void ClearBinding()
    {
        if (_patched) { _patched = false; Patches.UnpatchSelf(); }
        UnregisterTargets.Clear();
        _contract = null; _plugin = null; _version = null;
    }
}

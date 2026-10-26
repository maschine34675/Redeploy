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
using Redeploy.Core.Api;
using UnityEngine;
using UnityEngine.SceneManagement;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Reapply Amand's native graphics setup after a verified Core FPS-camera replacement.</summary>
internal static class AmandsGraphicsCompat
{
    internal const string PluginGuid = AmandsGraphicsContract.PluginGuid;
    private sealed class Lease
    {
        internal LocalGame Game;
        internal GameWorld World;
        internal object Instance;
        internal LevelSettings Level;
        internal string SceneName;
        internal object[] Defaults;
        internal Player PublishedBody;
        internal GameObject BoundCamera;
        internal bool Mode;
        internal bool Committed;
        internal List<DictionaryEntry> PreviousLights, PreviousVolumetrics;
    }
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.AmandsGraphicsCompat");
    private static readonly ConditionalWeakTable<GameObject, object> Cameras = new();
    private static AmandsGraphicsContract _contract;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static Lease _lease, _beforePreparation;
    private static Player _preparedBody;
    private static bool _subscribed, _patched, _fault, _newerWarned, _refreshing;
    private static string _failure, _warning;
    private static List<DictionaryEntry> _savedLights, _savedVolumetrics;
    private static LocalGame Game => Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
    private static GameWorld World => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
    private static bool OwnsWorld => _lease != null && ReferenceEquals(_lease.Game, Game)
        && ReferenceEquals(_lease.World, World) && BodyReplacementContext.OwnsRaid(Game, World);
    private static bool CurrentWorld => OwnsWorld && _lease.Committed;

    internal static string GetRejectionReason()
    {
        if (!_subscribed) { _subscribed = true; RespawnApi.Replaced += Replaced; }
        if (_fault) return _failure;
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info))
        {
            if (_lease != null) { Fault(new InvalidOperationException("Amand's Graphics disappeared during a Core raid")); return _failure; }
            Clear(); return _failure = null;
        }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Amand's Graphics has not finished loading");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance) && _version == AmandsGraphicsContract.Normalize(info.Metadata?.Version))
            { _contract.Verify(); VerifyPatches(); return _failure; }
            if (_lease != null) throw new InvalidOperationException("Amand's Graphics changed during a Core raid");
            Clear();
            _contract = AmandsGraphicsContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version, out _version);
            _plugin = info.Instance; _patched = true;
            Patch(_contract.Activate, nameof(BeforeActivate));
            Patch(_contract.Update, nameof(BeforeUpdate));
            Patch(_contract.Reset, nameof(BeforeReset));
            Patch(_contract.PrismPostfix, nameof(BeforePrismPostfix));
            VerifyPatches(); _failure = null;
            if (_version > AmandsGraphicsContract.Reviewed && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("Amand's Graphics " + _version + " is newer than reviewed 1.8.0; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex) { if (_lease != null) Fault(ex); else { Clear(); Reject(ex); } }
        return _failure;
    }
    private static void Patch(MethodInfo target, string prefix)
        => Patches.Patch(target, prefix: new HarmonyMethod(typeof(AmandsGraphicsCompat), prefix) { priority = Priority.First });
    private static void VerifyPatches()
    {
        var targets = new[] { _contract.Activate, _contract.Update, _contract.Reset, _contract.PrismPostfix };
        var names = new[] { nameof(BeforeActivate), nameof(BeforeUpdate), nameof(BeforeReset), nameof(BeforePrismPostfix) };
        for (int i = 0; i < targets.Length; i++)
        {
            var hook = typeof(AmandsGraphicsCompat).GetMethod(names[i], BindingFlags.Static | BindingFlags.NonPublic);
            if (Harmony.GetPatchInfo(targets[i])?.Prefixes.Count(p => p.owner == Patches.Id && p.PatchMethod == hook) != 1)
                throw new MissingMethodException("Amand's Graphics compatibility hook missing: " + names[i]);
        }
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        if (_contract == null) return;
        if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, oldBody) || !BodyReplacementContext.OwnsBody(oldBody)
            || !ReferenceEquals(game.LocalPlayer, oldBody) || !ReferenceEquals(game.GameWorld.MainPlayer, oldBody))
            throw new InvalidOperationException("Amand's Graphics requires Core's exact old body");
        _contract.Verify(); VerifyPatches();
        var instance = _contract.Instance.GetValue(null);
        var camera = CameraManager.Instance?.Camera;
        var level = _contract.Level.GetValue(null) as LevelSettings;
        if (!(instance is UnityEngine.Object graphics) || graphics == null || camera == null || level == null
            || !ReferenceEquals(_contract.Player.GetValue(null), oldBody)
            || !ReferenceEquals(_contract.CameraObject.GetValue(null), camera.gameObject)
            || !ReferenceEquals(_contract.Camera.GetValue(null), camera)
            || !ReferenceEquals(_contract.Prism.GetValue(null), camera.GetComponent<PrismEffects>()))
            throw new InvalidOperationException("Amand's Graphics did not own the current body and FPS camera before replacement");
        _beforePreparation = _lease;
        _preparedBody = oldBody;
        _lease = new Lease
        {
            Game = game, World = game.GameWorld, Instance = instance, Level = level,
            SceneName = SceneManager.GetActiveScene().name,
            Defaults = _beforePreparation?.Defaults ?? _contract.CaptureDefaults(),
            BoundCamera = camera.gameObject, PublishedBody = _beforePreparation?.PublishedBody,
            Mode = (bool)_contract.GraphicsMode.GetValue(instance),
            PreviousLights = LiveEntries(_contract.Lights), PreviousVolumetrics = LiveEntries(_contract.Volumetrics)
        };
        // Read-only preparation: later adapters can still refuse this replacement.
    }
    internal static void AbortPreparation()
    {
        if (ReferenceEquals(_preparedBody, null)) return;
        _lease = _beforePreparation; _beforePreparation = null; _preparedBody = null;
    }
    private static void Replaced(Player oldBody, Player newBody, string reason)
    {
        if (!OwnsWorld || !ReferenceEquals(oldBody, _preparedBody) || !BodyReplacementContext.OwnsBody(newBody)
            || !ReferenceEquals(newBody, Game.LocalPlayer) || !ReferenceEquals(newBody, World.MainPlayer)) return;
        Track(_lease.BoundCamera);
        _lease.PublishedBody = newBody;
        _lease.Committed = true;
        _preparedBody = null; _beforePreparation = null;
        // Native Destroy is deferred. Tick waits for actual Unity-null before activation.
    }
    private static void Track(GameObject camera)
    {
        if (!ReferenceEquals(camera, null) && !Cameras.TryGetValue(camera, out _)) Cameras.Add(camera, new object());
    }
    private static Camera ExactCamera(bool requireLivingBody = true)
    {
        var body = World?.MainPlayer;
        if (!CurrentWorld || BodyReplacementContext.IsCreating || body == null || !body.IsYourPlayer
            || !ReferenceEquals(body, _lease.PublishedBody) || !BodyReplacementContext.OwnsBody(body)
            || !ReferenceEquals(body, Game.LocalPlayer)
            || (requireLivingBody && (body.HealthController?.IsAlive != true || !World.RegisteredPlayers.Contains(body)))
            || !Singleton<PlayerCameraController>.Instantiated
            || !ReferenceEquals(Singleton<PlayerCameraController>.Instance.Player, body)) return null;
        var camera = CameraManager.Instance?.Camera;
        return camera != null && ReferenceEquals(camera, Camera.main) && camera.enabled && camera.gameObject.activeInHierarchy ? camera : null;
    }
    internal static void Tick()
    {
        if (!BodyReplacementContext.IsCreating && !ReferenceEquals(_preparedBody, null)) AbortPreparation();
        if (_contract == null || _lease == null || _fault) return;
        if (!OwnsWorld) { RetireWorld(_lease.World); return; }
        if (!_lease.Committed) return;
        try
        {
            var camera = ExactCamera();
            if (camera == null) return;
            var cached = _contract.CameraObject.GetValue(null) as GameObject;
            bool nativeAlreadyBound = ReferenceEquals(cached, camera.gameObject);
            if (nativeAlreadyBound && ReferenceEquals(_lease.BoundCamera, camera.gameObject)) return;
            // A different live foreign camera is never overwritten. Also wait for the
            // exact previous camera to finish native deferred destruction.
            if (!nativeAlreadyBound && cached != null) return;
            if (nativeAlreadyBound && _lease.BoundCamera != null) return;
            if (!nativeAlreadyBound && (!ReferenceEquals(cached, _lease.BoundCamera) || ReferenceEquals(cached, null)))
                throw new InvalidOperationException("Amand camera cache changed outside the owned replacement");
            if (!ReferenceEquals(_contract.Instance.GetValue(null), _lease.Instance)
                || !ReferenceEquals(_contract.Player.GetValue(null), _lease.PublishedBody)
                || !ReferenceEquals(_contract.Level.GetValue(null), _lease.Level)
                || SceneManager.GetActiveScene().name != _lease.SceneName)
                throw new InvalidOperationException("Amand body, scene or graphics owner changed before camera activation");
            var prism = camera.GetComponent<PrismEffects>();
            if (prism == null || !prism.enabled) throw new InvalidOperationException("The replacement camera has no active PrismEffects");
            if (nativeAlreadyBound && (!ReferenceEquals(_contract.Camera.GetValue(null), camera)
                || !ReferenceEquals(_contract.Prism.GetValue(null), prism)))
                throw new InvalidOperationException("Amand partially bound the replacement camera before publication");
            _savedLights = MergeLights(_lease.PreviousLights, LiveEntries(_contract.Lights));
            _savedVolumetrics = MergeLights(_lease.PreviousVolumetrics, LiveEntries(_contract.Volumetrics));
            _refreshing = true;
            try
            {
                if (nativeAlreadyBound)
                {
                    // Upstream may activate while construction is provisional, when
                    // our hooks must remain native. Its new camera caches are valid;
                    // correct only the recaptured scene/lamp baseline after commitment.
                    _contract.RestoreDefaults(_lease.Defaults);
                    RestoreLights(_contract.Lights, _savedLights); RestoreLights(_contract.Volumetrics, _savedVolumetrics);
                    _contract.GraphicsMode.SetValue(_lease.Instance, _lease.Mode);
                    (_lease.Mode ? _contract.Update : _contract.Reset).Invoke(_lease.Instance, null);
                }
                else _contract.Activate.Invoke(_lease.Instance, new object[] { camera.gameObject, prism });
            }
            finally
            {
                // Never retain already-transformed map/weather values as new defaults,
                // even if upstream throws after assigning only part of its camera state.
                try
                {
                    _contract.RestoreDefaults(_lease.Defaults);
                    RestoreLights(_contract.Lights, _savedLights); RestoreLights(_contract.Volumetrics, _savedVolumetrics);
                }
                finally
                {
                    try { _contract.GraphicsMode.SetValue(_lease.Instance, _lease.Mode); }
                    finally { _refreshing = false; _savedLights = null; _savedVolumetrics = null; }
                }
            }
            if (!ReferenceEquals(_contract.CameraObject.GetValue(null), camera.gameObject)
                || !ReferenceEquals(_contract.Camera.GetValue(null), camera)
                || !ReferenceEquals(_contract.Prism.GetValue(null), prism)
                || !ReferenceEquals(_contract.Level.GetValue(null), _lease.Level)
                || (bool)_contract.GraphicsMode.GetValue(_lease.Instance) != _lease.Mode)
                throw new InvalidOperationException("Amand native activation did not bind the exact replacement camera");
            _lease.BoundCamera = camera.gameObject; Track(camera.gameObject);
            _lease.PreviousLights = null; _lease.PreviousVolumetrics = null;
            CoreLog.LogDebug("Amand's Graphics: native graphics rebound to the replacement FPS camera; scene defaults preserved.");
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static bool BeforeActivate(object __instance, GameObject __0)
    {
        if (_contract == null) return true;
        // A later adapter can refuse preparation. Do not suppress even an owned
        // camera's ordinary callback until Core actually publishes replacement.
        if (OwnsWorld && !_lease.Committed) return true;
        // Weak tombstones fence delayed old-camera callbacks after a later raid starts.
        if (!ReferenceEquals(__0, null) && Cameras.TryGetValue(__0, out _)
            && (!CurrentWorld || !ReferenceEquals(__0, CameraManager.Instance?.Camera?.gameObject))) return false;
        if (!CurrentWorld || !ReferenceEquals(__instance, _lease.Instance)) return true;
        if (_refreshing) return ReferenceEquals(__0, ExactCamera()?.gameObject);
        if (BodyReplacementContext.IsCreating) return false;
        // A native delayed activation must use the same verified wrapper as our Tick,
        // including the original scene defaults. Ordinary raids stay entirely native.
        if (ExactCamera() is { } camera && ReferenceEquals(__0, camera.gameObject)) return false;
        return true;
    }
    private static bool BeforePrismPostfix(PrismEffects __0)
    {
        if (!CurrentWorld || __0 == null) return true;
        var camera = ExactCamera(requireLivingBody: false);
        // Keep the native Prism lifecycle intact. Only the foreign duplicate
        // activation is redundant: it sets GraphicsMode=false before scheduling
        // an async setup that then skips a still-live cached camera.
        return camera == null || !ReferenceEquals(__0, camera.GetComponent<PrismEffects>());
    }
    private static bool BeforeUpdate(object __instance)
    {
        if (!CurrentWorld || !ReferenceEquals(__instance, _lease.Instance)) return true;
        try
        {
            if (_refreshing)
            {
                _contract.RestoreDefaults(_lease.Defaults);
                RestoreLights(_contract.Lights, _savedLights); RestoreLights(_contract.Volumetrics, _savedVolumetrics);
                _contract.GraphicsMode.SetValue(__instance, _lease.Mode);
                if (!_lease.Mode) return false;
            }
            PruneLights(); return !_fault;
        }
        catch (Exception ex) { Fault(ex); return false; }
    }
    private static bool BeforeReset(object __instance)
    {
        if (!CurrentWorld || !ReferenceEquals(__instance, _lease.Instance)) return true;
        try
        {
            // The native reset also iterates old camera bloom components. Until the
            // replacement has been activated, there is no live camera state to reset.
            if ((_contract.CameraObject.GetValue(null) as GameObject) == null) return false;
            PruneLights(); return !_fault;
        }
        catch (Exception ex) { Fault(ex); return false; }
    }
    private static List<DictionaryEntry> LiveEntries(FieldInfo field)
    {
        var entries = new List<DictionaryEntry>();
        if (field.GetValue(null) is not IDictionary dictionary) throw new InvalidOperationException("Amand light registry is unavailable");
        foreach (DictionaryEntry entry in dictionary)
            if (entry.Key is UnityEngine.Object key && key != null) entries.Add(entry);
        return entries;
    }
    private static List<DictionaryEntry> MergeLights(List<DictionaryEntry> previous, List<DictionaryEntry> current)
    {
        var result = new List<DictionaryEntry>();
        foreach (var source in new[] { previous, current })
        {
            if (source == null) continue;
            foreach (var entry in source)
                if (entry.Key is UnityEngine.Object key && key != null && !result.Any(existing => ReferenceEquals(existing.Key, key)))
                    result.Add(entry);
        }
        return result;
    }
    private static void RestoreLights(FieldInfo field, List<DictionaryEntry> entries)
    {
        if (entries == null) return;
        if (field.GetValue(null) is not IDictionary dictionary) throw new InvalidOperationException("Amand light registry changed");
        foreach (var entry in entries)
            if (entry.Key is UnityEngine.Object key && key != null) dictionary[entry.Key] = entry.Value;
    }
    private static void PruneLights()
    {
        foreach (var field in new[] { _contract.Lights, _contract.Volumetrics })
        {
            if (field.GetValue(null) is not IDictionary dictionary) throw new InvalidOperationException("Amand light registry changed");
            var live = LiveEntries(field);
            if (live.Count == dictionary.Count) continue;
            // Rebuild: a destroyed Unity key can no longer be found by its old hash.
            dictionary.Clear(); RestoreLights(field, live);
        }
    }
    internal static void RetireWorld(GameWorld world)
    {
        if (_lease != null && ReferenceEquals(_lease.World, world))
        { _lease = null; _beforePreparation = null; _preparedBody = null; }
    }
    private static void Fault(Exception ex) { _fault = true; Reject(ex); }
    private static void Reject(Exception ex)
    {
        _failure = "Core cannot safely replace the player with Amand's Graphics: " + ex.GetBaseException().Message
            + ". Update or remove Amand's Graphics, then restart. Ordinary raids remain available.";
        if (_warning == _failure) return;
        _warning = _failure; CoreLog.LogWarning(_failure);
    }
    private static void Clear()
    {
        if (_patched) { Patches.UnpatchSelf(); _patched = false; }
        _contract = null; _plugin = null; _version = null;
    }
}

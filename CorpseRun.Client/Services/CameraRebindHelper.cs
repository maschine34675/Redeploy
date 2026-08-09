using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace CorpseRun.Services;

internal static class CameraRebindHelper
{
    private static readonly MethodInfo FaceShieldSyncMethod =
        AccessTools.Method(typeof(PlayerCameraController), "method_1");

    private static readonly MethodInfo NightVisionSyncMethod =
        AccessTools.Method(typeof(PlayerCameraController), "method_4");

    private static readonly MethodInfo ThermalSyncMethod =
        AccessTools.Method(typeof(PlayerCameraController), "method_5");

    // CameraManager.CG_method_17 (VisorEffect.SetIntensity) is the callback the game feeds
    // into VisorSwitcher.Init once per raid during camera setup.
    private static readonly MethodInfo VisorIntensityMethod =
        AccessTools.Method(typeof(CameraManager), "CG_method_17");

    /// <summary>
    /// Destroys the controller living on the dead player's gameObject and recreates it on the
    /// new player via the game's raid-start path. Keeping the old component alive and only
    /// swapping its Player reference breaks mods that look the controller up per
    /// player.GetComponent (e.g. Freecam) and leaves visor/NVG observers bound to the corpse.
    /// </summary>
    public static void RebindToPlayer(LocalPlayer newPlayer)
    {
        if (newPlayer == null)
            return;

        if (Singleton<PlayerCameraController>.Instantiated)
        {
            var existing = Singleton<PlayerCameraController>.Instance;
            if (existing.Player == newPlayer)
            {
                EnsureCameraActive(newPlayer);
                return;
            }

            // DestroyImmediate so OnDestroy's teardown (singleton release, VisorSwitcher.Deinit,
            // audio listener reset, EffectsController unbind) hits the OLD registration now.
            // A deferred Destroy would run at end of frame and tear down the NEW controller:
            // its OnDestroy releases whatever Singleton.Instance currently holds.
            existing.enabled = false;
            UnityEngine.Object.DestroyImmediate(existing);

            if (Singleton<PlayerCameraController>.Instantiated)
                Singleton<PlayerCameraController>.Release(Singleton<PlayerCameraController>.Instance);
        }

        PlayerCameraController.Create(newPlayer);
        RepairSharedCameraState();
        SyncVisionEffects();
        EnsureCameraActive(newPlayer);

        if (Singleton<BetterAudio>.Instantiated)
            Singleton<BetterAudio>.Instance.SetProtagonist(newPlayer);
    }

    /// <summary>
    /// PlayerCameraController.OnDestroy tears down two camera-shared states the game only
    /// initializes once per raid: the VisorSwitcher callback (next face-shield toggle would
    /// NRE on the nulled delegate) and the audio listener follow target (spatial audio would
    /// stay parked at (0,-1000,0)).
    /// </summary>
    private static void RepairSharedCameraState()
    {
        var cameraClass = CameraManager.Instance;

        if (cameraClass.VisorSwitcher != null && VisorIntensityMethod != null)
        {
            var setIntensity = (Action<float>)Delegate.CreateDelegate(
                typeof(Action<float>), cameraClass, VisorIntensityMethod);
            cameraClass.VisorSwitcher.Init(setIntensity);
        }

        if (Singleton<AudioListenerConsistencyManager>.Instantiated && cameraClass.Camera != null)
            Singleton<AudioListenerConsistencyManager>.Instance.Follow(cameraClass.Camera.transform);
    }

    /// <summary>
    /// method_0 binds the face-shield/NVG/thermal observers but never applies the current
    /// state, and no Changed event fires for the naked respawned player — sync once so
    /// leftover visor/NVG/thermal overlays from the old life disappear.
    /// </summary>
    private static void SyncVisionEffects()
    {
        if (!Singleton<PlayerCameraController>.Instantiated)
            return;

        var controller = Singleton<PlayerCameraController>.Instance;
        FaceShieldSyncMethod.Invoke(controller, null);
        NightVisionSyncMethod.Invoke(controller, null);
        ThermalSyncMethod.Invoke(controller, null);
    }

    private static void EnsureCameraActive(LocalPlayer player)
    {
        var cameraClass = CameraManager.Instance;
        var camera = cameraClass.Camera;
        if (camera == null)
            return;

        cameraClass.IsActive = true;
        camera.enabled = true;

        if (player?.CameraPosition != null)
            cameraClass.ForceSetPosition(player.CameraPosition.position);
    }
}

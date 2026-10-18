using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace Redeploy.Core.Compat;

/// <summary>
/// Terkoiz.Freecam caches the GamePlayerOwner component once in FreecamController.Start();
/// our respawn destroys that owner, so the cached reference would throw on the next toggle.
/// Point the cached field at the freshly created owner and force-exit freecam mode if it was
/// active during death — otherwise the new camera controller and the freecam script fight
/// over the camera every frame. No-op when Freecam isn't loaded.
/// </summary>
public static class FreecamCompatHelper
{
    private static bool _resolved;
    private static Type _controllerType;
    private static Type _freecamScriptType;
    private static FieldInfo _ownerField;
    private static FieldInfo _freecamScriptField;
    private static FieldInfo _controlsToggledField;
    private static FieldInfo _freecamIsActiveField;
    private static FieldInfo _mainCameraField;
    private static FieldInfo _pluginControllerField;
    private static bool _warnedSceneSearch;

    private static readonly FieldInfo CameraOperationField =
        AccessTools.Field(typeof(PlayerCameraController), "_currentCameraState");

    private static bool _patchesEnabled;

    /// <summary>
    /// Called at raid start (all plugins are loaded by then — plugin load order makes
    /// Awake too early). Forces the camera operation switch after Freecam's own mode
    /// changes: entering freecam must land on the free operation (camera released),
    /// leaving it on the first-person operation — regardless of which link in Freecam's
    /// GetComponent/UpdatePointOfView chain misbehaves on a respawned player.
    /// </summary>
    public static void EnablePatchesIfPresent()
    {
        if (_patchesEnabled)
            return;

        _patchesEnabled = true;

        Resolve();
        if (_controllerType == null)
            return;

        new RedeployCoreForceFreeCameraOperationPatch().Enable();
        new RedeployCoreForceFirstPersonOperationPatch().Enable();
        CoreLog.LogDebug("Freecam detected — camera operation patches enabled.");
    }

    private sealed class RedeployCoreForceFreeCameraOperationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(_controllerType, "SetPlayerToFreecamMode");

        [PatchPostfix]
        private static void Postfix()
        {
            ForceOperation<LockCameraState>("freecam");
            EnsureFreecamScriptAlive();
        }
    }

    /// <summary>
    /// FreecamController caches its Freecam movement script (added to the camera gameObject)
    /// once at raid start. If that script got destroyed or sits on a stale camera object,
    /// the camera is released by the operation switch but nothing moves it — IsActive is a
    /// plain bool field, so Freecam never notices its own script is dead. Recreate it on the
    /// live camera and rewire the cached fields.
    /// </summary>
    private static void EnsureFreecamScriptAlive()
    {
        try
        {
            if (_freecamScriptField == null || _freecamScriptType == null || !CameraManager.Exist)
                return;

            var camera = CameraManager.Instance.Camera;
            if (camera == null)
                return;

            var instance = FindController();
            if (instance == null)
                return;

            var script = _freecamScriptField.GetValue(instance) as UnityEngine.MonoBehaviour;
            var scriptDead = script == null || script.Equals(null);
            var wrongCamera = !scriptDead && script.gameObject != camera.gameObject;

            if (!scriptDead && !wrongCamera)
            {
                CoreLog.LogDebug(
                    $"Freecam script ok: enabled={script.isActiveAndEnabled}, cam={camera.gameObject.GetInstanceID()}");
                return;
            }

            if (wrongCamera)
                UnityEngine.Object.Destroy(script);

            var newScript = camera.gameObject.AddComponent(_freecamScriptType) as UnityEngine.MonoBehaviour;
            _freecamIsActiveField?.SetValue(newScript, true);
            _freecamScriptField.SetValue(instance, newScript);
            _mainCameraField?.SetValue(instance, camera.gameObject);

            CoreLog.LogWarning(
                $"Freecam script was {(scriptDead ? "destroyed" : "on a stale camera object")} — recreated on live camera.");
        }
        catch (Exception ex)
        {
            CoreLog.LogWarning($"Freecam script revive failed: {ex.Message}");
        }
    }

    private sealed class RedeployCoreForceFirstPersonOperationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(_controllerType, "SetPlayerToFirstPersonMode");

        [PatchPostfix]
        private static void Postfix()
        {
            ForceOperation<FirstPersonCameraState>("first person");
        }
    }

    private static void ForceOperation<TOperation>(string label) where TOperation : CameraState
    {
        try
        {
            if (!Singleton<PlayerCameraController>.Instantiated)
            {
                CoreLog.LogWarning($"Freecam {label}: no PlayerCameraController singleton!");
                return;
            }

            var controller = Singleton<PlayerCameraController>.Instance;
            var before = CameraOperationField?.GetValue(controller)?.GetType().Name ?? "null";
            controller.InitiateOperation<TOperation>();
            CoreLog.LogDebug(
                $"Freecam {label}: op {before} -> {typeof(TOperation).Name}, playerPOV={controller.Player?.PointOfView}");
        }
        catch (Exception ex)
        {
            CoreLog.LogWarning($"Freecam operation force failed: {ex.Message}");
        }
    }

    public static void RebindOwner(GamePlayerOwner newOwner)
    {
        if (newOwner == null)
            return;

        try
        {
            Resolve();

            if (_controllerType == null || _ownerField == null)
                return;

            var instance = FindController();
            if (instance == null)
                return;

            if (_freecamScriptField?.GetValue(instance) is object freecamScript)
                _freecamIsActiveField?.SetValue(freecamScript, false);

            _controlsToggledField?.SetValue(instance, false);
            _ownerField.SetValue(instance, newOwner);
            CoreLog.LogDebug("Freecam compat: rebound cached GamePlayerOwner and reset freecam state.");
        }
        catch (Exception ex)
        {
            CoreLog.LogWarning($"Freecam compat rebind failed: {ex.Message}");
        }
    }

    /// <summary>
    /// The raid's FreecamController without walking the scene. Freecam keeps it in
    /// FreecamPlugin.FreecamControllerInstance and adds it to the GameWorld's own GameObject
    /// (1.4.8, FreecamPatch on GameWorld.OnGameStarted). FindObjectOfType, used before, walked
    /// every object in the scene and grew with every corpse a long raid keeps: 94 to 309 ms per
    /// respawn in the measurement raid (docs/RESPAWN-API.md, "Messraid 17.09.2026"). The scene
    /// search stays only as a last resort, with a warning that the lookup needs an update.
    /// </summary>
    private static UnityEngine.Object FindController()
    {
        if (_pluginControllerField?.GetValue(null) is UnityEngine.Object kept && kept != null)
            return kept;

        if (Singleton<GameWorld>.Instantiated)
        {
            var onWorld = Singleton<GameWorld>.Instance.gameObject.GetComponent(_controllerType);
            if (onWorld != null)
                return onWorld;
        }

        var searched = UnityEngine.Object.FindObjectOfType(_controllerType);
        if (searched != null && !_warnedSceneSearch)
        {
            _warnedSceneSearch = true;
            CoreLog.LogWarning("Freecam compat: controller found only by a scene search (slow in long raids); Freecam changed where it keeps it.");
        }
        return searched;
    }

    private static void Resolve()
    {
        if (_resolved)
            return;

        _resolved = true;

        Type pluginType = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            _controllerType ??= assembly.GetType("Terkoiz.Freecam.FreecamController", false);
            _freecamScriptType ??= assembly.GetType("Terkoiz.Freecam.Freecam", false);
            pluginType ??= assembly.GetType("Terkoiz.Freecam.FreecamPlugin", false);
            if (_controllerType != null && _freecamScriptType != null && pluginType != null)
                break;
        }

        if (_controllerType == null)
            return;

        _ownerField = AccessTools.Field(_controllerType, "_gamePlayerOwner");
        _freecamScriptField = AccessTools.Field(_controllerType, "_freeCamScript");
        _controlsToggledField = AccessTools.Field(_controllerType, "_controlsToggled");
        _mainCameraField = AccessTools.Field(_controllerType, "_mainCamera");
        if (pluginType != null)
            _pluginControllerField = AccessTools.Field(pluginType, "FreecamControllerInstance");

        if (_freecamScriptType != null)
            _freecamIsActiveField = AccessTools.Field(_freecamScriptType, "IsActive");
    }
}

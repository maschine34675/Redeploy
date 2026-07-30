using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace CorpseRun.Services;

/// <summary>
/// Resets the post-process death fade/blur on the FPS camera and rebinds EffectsController to the new player.
/// </summary>
internal static class DeathEffectsHelper
{
    private static readonly FieldInfo EffectsPlayerField =
        AccessTools.Field(typeof(EffectsController), "player_0");

    private static readonly MethodInfo EffectsBindMethod =
        AccessTools.Method(typeof(EffectsController), "method_0");

    private static readonly MethodInfo EffectStartedMethod =
        AccessTools.Method(typeof(EffectsController), "method_11");

    private static readonly MethodInfo EffectRemovedMethod =
        AccessTools.Method(typeof(EffectsController), "method_12");

    private static readonly MethodInfo DamageReceivedMethod =
        AccessTools.Method(typeof(EffectsController), "method_7");

    private static readonly MethodInfo ApplyDamageMethod =
        AccessTools.Method(typeof(EffectsController), "method_6");

    private static readonly MethodInfo DiedMethod =
        AccessTools.Method(typeof(EffectsController), "method_9");

    private static readonly MethodInfo BurnEyesMethod =
        AccessTools.Method(typeof(EffectsController), "method_5");

    private static readonly MethodInfo StimulatorBuffMethod =
        AccessTools.Method(typeof(EffectsController), "method_13");

    private static readonly MethodInfo GlassesChangedMethod =
        AccessTools.Method(typeof(EffectsController), "method_4");

    // Unity lifecycle method name, cannot be obfuscated. Clears the internal
    // per-effect ActiveEffects lists (stun/contusion double-vision, radial blur,
    // vignette, ...) that otherwise keep referencing the dead player's IEffect
    // instances and stay visible after the player object is swapped on respawn.
    private static readonly MethodInfo ClearActiveEffectsMethod =
        AccessTools.Method(typeof(EffectsController), "OnDisable");

    public static void ClearDeathScreenEffects()
    {
        if (!CameraClass.Exist)
            return;

        var camera = CameraClass.Instance.Camera;
        if (camera == null)
            return;

        var deathFade = camera.GetComponent<DeathFade>();
        if (deathFade != null)
        {
            deathFade.DisableEffect();
            deathFade.enabled = false;
        }

        var fastBlur = camera.GetComponent<FastBlur>();
        if (fastBlur != null)
        {
            fastBlur.Reset();
            fastBlur.enabled = false;
        }
    }

    public static void RebindEffectsController(LocalPlayer newPlayer)
    {
        if (newPlayer == null || !CameraClass.Exist)
            return;

        var effects = CameraClass.Instance.EffectsController;
        if (effects == null)
            return;

        if (EffectsPlayerField.GetValue(effects) is Player oldPlayer)
            UnsubscribeEffectsFromPlayer(effects, oldPlayer);

        ClearDeathScreenEffects();
        ClearActiveEffectsMethod.Invoke(effects, null);

        if (!Singleton<PlayerCameraController>.Instantiated)
            return;

        var controller = Singleton<PlayerCameraController>.Instance;
        EffectsBindMethod.Invoke(effects, new object[] { controller, controller.Camera });
    }

    private static void UnsubscribeEffectsFromPlayer(EffectsController effects, Player player)
    {
        if (player?.HealthController == null)
            return;

        player.HealthController.EffectStartedEvent -= CreateDelegate<Action<IEffect>>(effects, EffectStartedMethod);
        player.HealthController.EffectRemovedEvent -= CreateDelegate<Action<IEffect>>(effects, EffectRemovedMethod);
        player.OnDamageReceived -= CreateDelegate<Player.GDelegate65>(effects, DamageReceivedMethod);
        player.HealthController.ApplyDamageEvent -= CreateDelegate<Action<EBodyPart, float, DamageInfoStruct>>(effects, ApplyDamageMethod);
        player.HealthController.DiedEvent -= CreateDelegate<Action<EDamageType>>(effects, DiedMethod);
        player.HealthController.BurnEyesEvent -= CreateDelegate<Action<Vector3, float, float>>(effects, BurnEyesMethod);
        player.HealthController.StimulatorBuffEvent -= CreateDelegate<Action<IPlayerBuff>>(effects, StimulatorBuffMethod);
        player.OnGlassesChanged -= CreateDelegate<Action<VisorsItemClass, bool>>(effects, GlassesChangedMethod);

        EffectsPlayerField.SetValue(effects, null);
    }

    private static T CreateDelegate<T>(EffectsController effects, MethodInfo method) where T : Delegate
        => (T)Delegate.CreateDelegate(typeof(T), effects, method);
}

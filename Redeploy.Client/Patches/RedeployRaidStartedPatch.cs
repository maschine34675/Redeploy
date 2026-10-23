using Redeploy.Config;
using Redeploy.Core.Compat;
using Redeploy.Services;
using EFT;
using EFT.Communications;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace Redeploy.Patches;

internal sealed class RedeployRaidStartedPatch : ModulePatch
{
    public static void EnablePatch()
    {
        new RedeployRaidStartedPatch().Enable();
    }

    protected override MethodBase GetTargetMethod()
    {
        return typeof(GameWorld).GetMethod(nameof(GameWorld.OnGameStarted));
    }

    [PatchPostfix]
    private static void Postfix()
    {
        if (!Settings.Enabled.Value)
            return;

        // Freecam's camera-operation patches are enabled by Core's own raid-start postfix
        // now, for every consumer of the engine.
        if (FikaCompatCheck.IsFikaLoaded)
        {
            NotificationManager.DisplayMessageNotification(
                "Redeploy disabled: not compatible with Fika co-op.",
                ENotificationDurationType.Long,
                ENotificationIconType.Alert,
                Color.red);
            return;
        }

        if (!ServerModCheck.IsServerModAvailable)
        {
            NotificationManager.DisplayMessageNotification(
                "Redeploy disabled: server mod missing! (user/mods/Redeploy)",
                ENotificationDurationType.Long,
                ENotificationIconType.Alert,
                Color.red);
            return;
        }

        var compatibilityFailure = BodyReplacementCompatibility.GetRejectionReason();
        if (compatibilityFailure != null)
        {
            NotificationManager.DisplayMessageNotification(
                "Redeploy respawn unavailable: " + compatibilityFailure,
                ENotificationDurationType.Long, ENotificationIconType.Alert, Color.red);
            // The raid itself remains ordinary. The driver checks again before claiming a death.
        }
        RespawnSessionState.OnRaidStarted();
    }
}

using CorpseRun.Config;
using CorpseRun.Services;
using EFT;
using EFT.Communications;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace CorpseRun.Patches;

internal sealed class RaidStartedPatch : ModulePatch
{
    public static void EnablePatch()
    {
        new RaidStartedPatch().Enable();
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

        FreecamCompatHelper.EnablePatchesIfPresent();

        if (FikaCompatCheck.IsFikaLoaded)
        {
            NotificationManager.DisplayMessageNotification(
                "CorpseRun disabled: not compatible with Fika co-op.",
                ENotificationDurationType.Long,
                ENotificationIconType.Alert,
                Color.red);
            return;
        }

        if (!ServerModCheck.IsServerModAvailable)
        {
            NotificationManager.DisplayMessageNotification(
                "CorpseRun disabled: server mod missing! (user/mods/CorpseRun)",
                ENotificationDurationType.Long,
                ENotificationIconType.Alert,
                Color.red);
            return;
        }

        RespawnSessionState.OnRaidStarted();
    }
}

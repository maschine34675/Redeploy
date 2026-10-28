using System;
using System.Reflection;
using System.Threading.Tasks;
using EFT;
using EFT.UI;
using HarmonyLib;
using Redeploy.Services;
using SPT.Reflection.Patching;

namespace Redeploy.Patches;

internal static class ScavRecoveryMenuPatch
{
    public static bool Ready { get; private set; }

    public static void EnablePatches()
    {
        try
        {
            new MenuSelectionPatch().Enable();
            new RaidReadyPatch().Enable();
            new InventoryErrorPatch().Enable();
            new MenuOperationPatch().Enable();
            new MenuShownPatch().Enable();
            Ready = true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Scav recovery menu is unavailable with this game build ({ex.GetType().Name}).");
        }
    }

    private sealed class MenuOperationPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MainMenuShowOperation), nameof(MainMenuShowOperation.ShowMenuScreen));

        [PatchPrefix]
        private static void Prefix(MainMenuShowOperation __instance) => ScavRecoveryClient.PreparingMenu(__instance);
    }

    private sealed class MenuShownPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MenuScreen), nameof(MenuScreen.Show), new[] { typeof(MenuScreen.MainMenuBaseScreenController) });

        [PatchPostfix]
        private static void Postfix(MenuScreen __instance, MenuScreen.MainMenuBaseScreenController controller)
        {
            if (Ready)
                ScavRecoveryClient.MenuShown(__instance, controller);
        }
    }

    private sealed class MenuSelectionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MainMenuShowOperation), nameof(MainMenuShowOperation.ShowScreen));

        [PatchPrefix]
        private static bool Prefix(MainMenuShowOperation __instance, EMenuType screen)
            => !ScavRecoveryClient.BlocksMenu(__instance) || screen == EMenuType.Exit;
    }

    // Invited-group ready callbacks can bypass the ordinary Play button. Fence
    // the native ready entry too while recovery owns this exact menu session.
    private sealed class RaidReadyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MainMenuShowOperation), "method_27", Type.EmptyTypes);

        [PatchPrefix]
        private static bool Prefix(MainMenuShowOperation __instance, ref Task __result)
        {
            if (!ScavRecoveryClient.BlocksMenu(__instance))
                return true;
            __result = Task.CompletedTask;
            return false;
        }
    }

    // Native menu error handling calls GetProfiles/Init behind the transfer UI.
    // During recovery, return to the recovery panel instead of rebuilding old
    // menu controllers while an inventory operation is still in flight.
    private sealed class InventoryErrorPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MainMenuShowOperation), nameof(MainMenuShowOperation.InventoryErrorHandler),
                new[] { typeof(string), typeof(string) });

        [PatchPrefix]
        private static bool Prefix(MainMenuShowOperation __instance)
        {
            if (!ScavRecoveryClient.OwnsMutatedMenu(__instance))
                return true;
            ScavRecoveryClient.InventoryError();
            return false;
        }
    }
}

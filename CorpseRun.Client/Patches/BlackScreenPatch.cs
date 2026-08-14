using CorpseRun.Config;
using CorpseRun.Services;
using EFT.UI;
using SPT.Reflection.Patching;
using System.Reflection;

namespace CorpseRun.Patches;

internal static class BlackScreenPatch
{
    public static void Enable()
    {
        new StartBlackScreenPatch().Enable();
    }

    private static bool ShouldBlock()
    {
        if (!Settings.Enabled.Value || RespawnSessionState.HasGivenUp)
            return false;

        return RespawnSessionState.PendingRespawnChoice || RespawnSessionState.IsRespawnInProgress;
    }

    private sealed class StartBlackScreenPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => typeof(PreloaderUI).GetMethod(nameof(PreloaderUI.StartBlackScreenShow));

        [PatchPrefix]
        private static bool Prefix()
        {
            if (!ShouldBlock())
                return true;

            Plugin.ModLogger.LogInfo("Blocked raid-end black screen while respawn is pending.");
            return false;
        }
    }
}

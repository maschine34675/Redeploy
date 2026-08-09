using CorpseRun.Services;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace CorpseRun.Patches;

/// <summary>
/// A new statistics session on respawn clears EftStats.Victims; restore the pre-respawn kill list.
/// </summary>
internal static class RespawnStatisticsPatch
{
    public static void Enable()
    {
        new BeginStatisticsSessionPatch().Enable();
    }

    private sealed class BeginStatisticsSessionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BaseStatisticsManager), nameof(BaseStatisticsManager.BeginStatisticsSession));
        }

        [PatchPostfix]
        private static void Postfix(BaseStatisticsManager __instance)
        {
            if (!RespawnSessionState.IsRespawnInProgress)
                return;

            RespawnStatisticsHelper.RestoreRaidVictims(__instance.Profile);
        }
    }
}

using CorpseRun.Services;
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
            return AccessTools.Method(typeof(LocationStatisticsCollectorAbstractClass), nameof(LocationStatisticsCollectorAbstractClass.BeginStatisticsSession));
        }

        [PatchPostfix]
        private static void Postfix(LocationStatisticsCollectorAbstractClass __instance)
        {
            if (!RespawnSessionState.IsRespawnInProgress)
                return;

            RespawnStatisticsHelper.RestoreRaidVictims(__instance.Profile_0);
        }
    }
}

using CorpseRun.Config;
using CorpseRun.Services;
using EFT;
using EFT.UI.SessionEnd;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace CorpseRun.Patches;

internal static class SessionResultPatch
{
    public static void Enable()
    {
        new SessionResultExitStatusPatch().Enable();
        new RaidEndCleanupPatch().Enable();
    }

    private sealed class SessionResultExitStatusPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(SessionResultExitStatus),
                nameof(SessionResultExitStatus.Show),
                new[]
                {
                    typeof(Profile),
                    typeof(PlayerVisualRepresentation),
                    typeof(ESideType),
                    typeof(ExitStatus),
                    typeof(TimeSpan),
                    typeof(IEftSession),
                    typeof(bool)
                });
        }

        [PatchPrefix]
        private static bool Prefix(ExitStatus exitStatus)
        {
            if (!Settings.Enabled.Value)
                return true;

            if (exitStatus != ExitStatus.Killed)
                return true;

            if (RespawnSessionState.PendingRespawnChoice && !RespawnSessionState.HasGivenUp)
                return false;

            return true;
        }
    }

    private sealed class RaidEndCleanupPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "GameEnd");
        }

        [PatchPostfix]
        private static void Postfix()
        {
            RespawnSessionState.OnRaidEnded();
        }
    }
}

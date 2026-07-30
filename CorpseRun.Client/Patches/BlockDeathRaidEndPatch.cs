using CorpseRun.Config;
using CorpseRun.Services;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;

namespace CorpseRun.Patches;

internal static class BlockDeathRaidEndPatch
{
    public static void Enable()
    {
        new DeathHandlerPatch().Enable();
        new DeathScreenPatch().Enable();
        new StopPatch().Enable();
    }

    private static bool ShouldIntercept(LocalGame game)
    {
        if (!Settings.Enabled.Value)
            return false;

        if (!RespawnSessionState.IsInRaid)
            return false;

        if (RespawnSessionState.HasGivenUp)
            return false;

        if (RespawnSessionState.IsRespawnInProgress)
            return false;

        if (!Settings.HasRespawnsRemaining(RespawnSessionState.RespawnsUsed))
            return false;

        if (game?.Location_0?.Id == "hideout")
            return false;

        return true;
    }

    private sealed class DeathHandlerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "method_19");
        }

        [PatchPrefix]
        private static bool Prefix(BaseLocalGame<EftGamePlayerOwner> __instance)
        {
            if (__instance is not LocalGame localGame)
                return true;

            if (!ShouldIntercept(localGame))
                return true;

            RespawnService.OnPlayerDied(localGame);
            return false;
        }
    }

    private sealed class DeathScreenPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "method_11");
        }

        [PatchPrefix]
        private static bool Prefix(BaseLocalGame<EftGamePlayerOwner> __instance)
        {
            if (__instance is not LocalGame localGame)
                return true;

            if (RespawnSessionState.PendingRespawnChoice && ShouldIntercept(localGame))
                return false;

            return true;
        }
    }

    private sealed class StopPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(BaseLocalGame<EftGamePlayerOwner>),
                nameof(BaseLocalGame<EftGamePlayerOwner>.Stop),
                new[] { typeof(string), typeof(ExitStatus), typeof(string), typeof(float) });
        }

        [PatchPrefix]
        private static bool Prefix(BaseLocalGame<EftGamePlayerOwner> __instance, ExitStatus exitStatus)
        {
            if (exitStatus != ExitStatus.Killed)
                return true;

            if (__instance is not LocalGame localGame)
                return true;

            if (RespawnSessionState.PendingRespawnChoice && ShouldIntercept(localGame))
                return false;

            return true;
        }
    }
}

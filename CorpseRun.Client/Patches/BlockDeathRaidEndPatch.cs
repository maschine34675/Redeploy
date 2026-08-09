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

        if (game?.Location?.Id == "hideout")
            return false;

        return true;
    }

    private sealed class DeathHandlerPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "CG_Spawn");
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
            return AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "InitiateGameStopping");
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
            if (__instance is not LocalGame localGame)
                return true;

            if (exitStatus == ExitStatus.Killed
                && RespawnSessionState.PendingRespawnChoice
                && ShouldIntercept(localGame))
                return false;

            // An external raid end (MIA timer, menu leave, transit, another mod) is
            // proceeding. Stand down so BlackScreenPatch lets Stop's continuation
            // (OnGameSessionEnd + raid serialization) through — otherwise the game
            // soft-locks in Stopping and the raid is never saved.
            StandDownForExternalStop();
            return true;
        }

        private static void StandDownForExternalStop()
        {
            if (!RespawnSessionState.PendingRespawnChoice && !RespawnSessionState.IsRespawnInProgress)
                return;

            Plugin.ModLogger.LogWarning("[CorpseRun] External raid end while respawn pending — standing down.");
            RespawnSessionState.HasGivenUp = true;
            RespawnSessionState.PendingRespawnChoice = false;
            UI.RespawnPromptPanel.Instance?.Hide();
        }
    }
}

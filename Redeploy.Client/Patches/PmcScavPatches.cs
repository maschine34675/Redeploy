using EFT;
using EFT.UI;
using HarmonyLib;
using JsonType;
using Redeploy.Services;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Redeploy.Patches;

internal static class PmcScavPatches
{
    public static string UnavailableReason { get; private set; } = "Scav continuation has not initialized.";

    public static void EnablePatches()
    {
        try
        {
            new PmcScavBeforeGameEndPatch().Enable();
            new PmcScavLostInsurancePatch().Enable();
            new PmcScavSessionResultPatch().Enable();
            new PmcScavRegularScavSavePatch().Enable();
            new PmcScavBackendEndPatch().Enable();
            new PmcScavTransitInteractionPatch().Enable();
            new PmcScavTransitStartPatch().Enable();
            new PmcScavTransitUiPatch().Enable();
            UnavailableReason = null;
        }
        catch (Exception ex)
        {
            // Any already-installed hooks are inert without an owned context. Do not let
            // an optional feature prevent ordinary PMC/Scav Redeploy initialization.
            UnavailableReason = "Scav continuation is unavailable with this game build.";
            Plugin.ModLogger.LogWarning($"{UnavailableReason} {ex.Message}");
        }
    }

    private sealed class PmcScavBeforeGameEndPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "GameEnd");

        [PatchPrefix]
        private static void Prefix(object __instance, ExitStatus exitStatus)
        {
            if (__instance is LocalGame game)
                PmcScavSession.BeforeGameEnd(game, exitStatus);
        }
    }

    private sealed class PmcScavLostInsurancePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "GetLostInsuredItems");

        [PatchPrefix]
        private static bool Prefix(object __instance, ref FlatItem[] __result)
        {
            if (__instance is LocalGame game && PmcScavSession.TryGetFinalInsurance(game, out var items))
            {
                __result = items;
                return false;
            }
            return true;
        }
    }

    private sealed class PmcScavSessionResultPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(SessionResultShowOperation), "Execute");

        [PatchPrefix]
        private static void Prefix(IEftSession session, string profileId, ref ExitStatus exitStatus, ref TimeSpan exitTime)
            => PmcScavSession.RewritePmcResult(session, profileId, ref exitStatus, ref exitTime);
    }

    private sealed class PmcScavBackendEndPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(EftClientBackendSession), "LocalRaidEnded");

        [PatchPrefix]
        private static bool Prefix(LocalRaidSettings settings, SessionResult results, ref Task __result)
        {
            var reason = PmcScavSession.GetUnsafeSettlementReason(settings, results);
            if (reason == null)
                return true;
            __result = Task.FromException(new InvalidOperationException(reason));
            return false;
        }

        [PatchPostfix]
        private static void Postfix(LocalRaidSettings settings, SessionResult results, ref Task __result)
            => __result = PmcScavSession.TrackBackendEnd(settings, results, __result);
    }

    private sealed class PmcScavRegularScavSavePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // SPT's Init prefix assumes every final Savage body is the account's regular
            // Player-Scav. Patch that handler itself, preserving the native result screen
            // and every other Init patch. Ordinary PMC/Scav results still run it normally.
            var type = AccessTools.TypeByName("SPT.SinglePlayer.Patches.ScavMode.SendPlayerScavProfileToServerAfterRaidPatch");
            var method = type == null ? null : AccessTools.DeclaredMethod(type, "PatchPrefix",
                new[] { typeof(SessionResultShowOperation) });
            if (method == null || !method.IsStatic || method.ReturnType != typeof(void)
                || type.Assembly.GetName().Name != "spt-singleplayer")
                throw new MissingMethodException("The SPT Player-Scav result handler could not be verified.");
            return method;
        }

        [PatchPrefix]
        private static bool Prefix(SessionResultShowOperation __0)
            => !PmcScavSession.BlocksRegularScavSave(__0);
    }

    private sealed class PmcScavTransitInteractionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(LocalTransitController), "InteractWithTransit");

        [PatchPrefix]
        private static bool Prefix(Player player) => !PmcScavSession.BlocksTransit(player);
    }

    private sealed class PmcScavTransitStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(LocalTransitController), "Transit");

        [PatchPrefix]
        private static bool Prefix(Player player) => !PmcScavSession.BlocksTransit(player);
    }

    private sealed class PmcScavTransitUiPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(ExtractionTimersPanel), "SetTransits");

        [PatchPrefix]
        private static bool Prefix(ExtractionTimersPanel __instance)
            => !PmcScavSession.BlocksTransitUi(__instance);
    }
}

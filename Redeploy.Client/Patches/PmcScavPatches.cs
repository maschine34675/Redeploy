using EFT;
using EFT.Dialogs;
using EFT.InventoryLogic;
using EFT.Quests;
using EFT.UI;
using EFT.Vehicle;
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
            ScavExtractionUi.ValidateContract();
            new PmcScavStatisticsStartPatch().Enable();
            new PmcScavStatisticsEndPatch().Enable();
            new PmcScavBeforeGameEndPatch().Enable();
            new PmcScavLostInsurancePatch().Enable();
            new PmcScavSessionResultPatch().Enable();
            new PmcScavRegularScavSavePatch().Enable();
            new PmcScavBackendEndPatch().Enable();
            new PmcScavTransitInteractionPatch().Enable();
            new PmcScavTransitStartPatch().Enable();
            new PmcScavTransitUiPatch().Enable();
            new PmcScavBtrDialogPatch().Enable();
            new PmcScavBtrScreenPatch().Enable();
            new PmcScavBtrPurchasePatch().Enable();
            new PmcScavBtrTransferPatch().Enable();
            new PmcScavSellAllPatch().Enable();
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

    private sealed class PmcScavStatisticsStartPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player), nameof(Player.OnGameSessionBegin));

        [PatchPostfix]
        private static void Postfix(Player __instance) => PmcScavSession.RecordScavLifeStart(__instance);
    }

    private sealed class PmcScavStatisticsEndPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(LocalPlayer), nameof(LocalPlayer.OnGameSessionEnd),
                new[] { typeof(ExitStatus), typeof(float), typeof(string), typeof(string) });

        [PatchPrefix]
        private static void Prefix(LocalPlayer __instance, ref float pastTime)
            => PmcScavSession.RewriteScavSessionTime(__instance, ref pastTime);
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
        private static void Prefix(IEftSession session, ref string profileId, ref Profile savageProfile,
            ref ExitStatus exitStatus, ref TimeSpan exitTime)
            => PmcScavSession.RewriteScavResult(session, ref profileId, ref savageProfile, ref exitStatus, ref exitTime);
    }

    private sealed class PmcScavBackendEndPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(EftClientBackendSession), "LocalRaidEnded");

        [PatchPrefix]
        private static bool Prefix(LocalRaidSettings settings, SessionResult results, ref Task __result)
        {
            PmcScavSession.RewriteScavBackendTime(settings, results);
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
            // Combined settlement already persists the actual Scav. SPT's ordinary Init
            // handler would replace that inventory with an old client snapshot, including
            // after partial transfer or after death generated the next Scav. Skip only
            // the owned hybrid handler; keep native result UI and ordinary raid handling.
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

    private sealed class PmcScavBtrDialogPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(TraderDialogScreen), nameof(TraderDialogScreen.HandlePurchaseService),
                new[] { typeof(DialogAction) });

        [PatchPrefix]
        private static bool Prefix(DialogAction action, Profile ____profile)
            => !(action is DialogPurchaseServiceAction purchase
                && PmcScavSession.BlocksBtrItemDelivery(____profile, purchase.ServiceType));
    }

    private sealed class PmcScavBtrScreenPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(BTRView), nameof(BTRView.OpenTransferItemsScreen), Type.EmptyTypes);

        [PatchPrefix]
        private static bool Prefix()
            => !PmcScavSession.BlocksBtrItemDelivery(GamePlayerOwner.MyPlayer?.Profile,
                ETraderServiceType.BtrItemsDelivery);
    }

    private sealed class PmcScavBtrPurchasePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(InventoryController), nameof(InventoryController.TryPurchaseTraderService),
                new[] { typeof(ETraderServiceType), typeof(QuestController), typeof(string) });

        [PatchPrefix]
        private static bool Prefix(InventoryController __instance, ETraderServiceType serviceType,
            ref Task<bool> __result)
        {
            if (!PmcScavSession.BlocksBtrItemDelivery(__instance.Profile, serviceType))
                return true;
            __result = Task.FromResult(false);
            return false;
        }
    }

    private sealed class PmcScavBtrTransferPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(TransferItemsController), nameof(TransferItemsController.MoveItemsFromTempStashToTransferStash),
                new[] { typeof(Player) });

        [PatchPrefix]
        private static bool Prefix(TransferItemsController __instance, Player player)
            => !PmcScavSession.BlocksBtrItemDelivery(player?.Profile, __instance.ServiceType);
    }

    private sealed class PmcScavSellAllPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(ScavengerInventoryScreen), nameof(ScavengerInventoryScreen.SellAll), Type.EmptyTypes);

        [PatchPrefix]
        private static bool Prefix(BackEndInventoryController ____scavInventoryController, ref Task __result)
        {
            if (!PmcScavSession.BlocksScavSellAll(____scavInventoryController))
                return true;
            __result = Task.CompletedTask;
            return false;
        }
    }
}

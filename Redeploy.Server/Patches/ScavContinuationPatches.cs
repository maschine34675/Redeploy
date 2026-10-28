using System.Reflection;
using Redeploy.Server.Scav;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Callbacks;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.InRaid;
using SPTarkov.Server.Core.DI.Routing;
using SPTarkov.Server.Core.Models.Eft.Common.Request;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;

namespace Redeploy.Server.Patches;

public sealed class ScavRaidStartPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => typeof(LocationLifecycleService).GetMethod(nameof(LocationLifecycleService.StartLocalRaidAsync))!;
    [PatchPrefix] public static void Prefix(MongoId sessionId) => ScavContinuationService.Current?.BeforeStart(sessionId);
    [PatchPostfix] public static void Postfix(MongoId sessionId, StartLocalRaidRequestData request, ref Task<StartLocalRaidResponseData> __result)
    {
        if (ScavContinuationService.Current is { } service) __result = Register(__result, service, sessionId, request);
    }
    private static async Task<StartLocalRaidResponseData> Register(Task<StartLocalRaidResponseData> native,
        ScavContinuationService service, MongoId sessionId, StartLocalRaidRequestData request)
    {
        var result = await native;
        service.RegisterStart(sessionId, request, result);
        return result;
    }
}

public sealed class ScavRaidEndPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        NativeScavOutcome.ValidateContract();
        return typeof(LocationLifecycleService).GetMethod(nameof(LocationLifecycleService.EndLocalRaidAsync))!;
    }
    [PatchPrefix] public static bool Prefix(LocationLifecycleService __instance, MongoId sessionId,
        EndLocalRaidRequestData request, CancellationToken cancellationToken, ref Task __result)
    {
        var service = ScavContinuationService.Current;
        if (service == null || !service.ShouldInterceptEnd(sessionId, request)) return true;
        __result = service.End(sessionId, request,
            adapted => __instance.EndLocalRaidAsync(sessionId, adapted, cancellationToken),
            (pmc, scav, adapted) => NativeScavOutcome.Invoke(__instance, sessionId, pmc, scav, adapted, cancellationToken),
            cancellationToken);
        return false;
    }
}

public sealed class ScavProfileSavePatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => typeof(SaveServer).GetMethod(nameof(SaveServer.SaveProfileAsync))!;
    [PatchPrefix] public static bool Prefix(SaveServer __instance, MongoId sessionID,
        CancellationToken cancellationToken, ref Task<long> __result)
    {
        var service = ScavContinuationService.Current;
        if (service == null || ScavContinuationService.RawSave.Value) return true;
        __result = service.Save(sessionID, () => __instance.SaveProfileAsync(sessionID, cancellationToken), cancellationToken);
        return false;
    }
}

/// <summary>The combined end already persisted this exact transfer inventory. A
/// late native upload must not resurrect items moved out of it or replace a successor.</summary>
public sealed class ScavResultUploadPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => typeof(InRaidController).GetMethod(nameof(InRaidController.SavePostRaidProfileForScav))!;
    [PatchPrefix] public static bool Prefix(ScavSaveRequestData offRaidProfileData, MongoId sessionId)
        => ScavContinuationService.Current?.BlocksScavSave(sessionId, offRaidProfileData.Inventory?.Equipment.ToString()) != true;
}

public sealed class ScavTransferFinishPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => typeof(ProfileCallbacks).GetMethod(nameof(ProfileCallbacks.RegenerateScav))!;
    [PatchPrefix] public static bool Prefix(ProfileCallbacks __instance, string url, EmptyRequestData __1,
        MongoId sessionID, ref ValueTask<string> __result)
    {
        var service = ScavContinuationService.Current;
        if (service?.OwnsRegeneration(sessionID) != true) return true;
        __result = service.FinishTransfer(sessionID, () => __instance.RegenerateScav(url, __1, sessionID),
            () => service.SaveNativeProfile(sessionID), CancellationToken.None);
        return false;
    }
}

public sealed class ScavTransferInventoryPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod() => typeof(ItemEventRouter).GetMethod(nameof(ItemEventRouter.HandleItemEvent))!;
    [PatchPrefix] public static bool Prefix(ItemEventRouter __instance, string url, PmcData pmcData,
        BaseInteractionRequestData body, MongoId sessionID, ItemEventRouterResponse output,
        CancellationToken cancellationToken, ref ValueTask<ItemEventRouterResponse> __result)
    {
        var service = ScavContinuationService.Current;
        if (service?.OwnsInventoryWindow(sessionID) != true) return true;
        __result = service.WithTransferInventory(sessionID,
            () => __instance.HandleItemEvent(url, pmcData, body, sessionID, output, cancellationToken), cancellationToken);
        return false;
    }
}

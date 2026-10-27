using System.Reflection;
using Redeploy.Server.Scav;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Services.InRaid;

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
    protected override MethodBase GetTargetMethod() => typeof(LocationLifecycleService).GetMethod(nameof(LocationLifecycleService.EndLocalRaidAsync))!;
    [PatchPrefix] public static bool Prefix(LocationLifecycleService __instance, MongoId sessionId,
        EndLocalRaidRequestData request, CancellationToken cancellationToken, ref Task __result)
    {
        var service = ScavContinuationService.Current;
        if (service == null || !service.ShouldInterceptEnd(sessionId, request)) return true;
        __result = service.End(sessionId, request, adapted => __instance.EndLocalRaidAsync(sessionId, adapted, cancellationToken), cancellationToken);
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

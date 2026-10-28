using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Callbacks;

namespace Redeploy.Server.Scav;

[Injectable]
public sealed class ScavRouter(JsonUtil json, ScavContinuationService service, LocationLifecycleService lifecycle, MatchCallbacks match) : StaticRouter(json,
[
    new RouteAction<ScavPrepareRequest>("/redeploy/pmc-scav/prepare",
        async (_, request, id, _, ct) => json.Serialize(await service.Prepare(id, request, ct))!),
    new RouteAction<ScavControlRequest>("/redeploy/pmc-scav/activate",
        async (_, request, id, _, ct) => json.Serialize(await service.Control(id, request, false, ct))!),
    new RouteAction<ScavControlRequest>("/redeploy/pmc-scav/cancel",
        async (_, request, id, _, ct) => json.Serialize(await service.Control(id, request, true, ct))!),
    new RouteAction<ScavRecoveryRequest>("/redeploy/pmc-scav/recovery/status",
        async (_, _, id, _, ct) => json.Serialize(await service.RecoveryStatus(id, ct))!),
    new RouteAction<ScavRecoveryRequest>("/redeploy/pmc-scav/recovery/abandon",
        async (_, request, id, _, ct) => json.Serialize(await service.RecoverAbandon(id, request,
            adapted => lifecycle.EndLocalRaidAsync(id, adapted, ct),
            (pmc, scav, adapted) => NativeScavOutcome.Invoke(lifecycle, id, pmc, scav, adapted, ct),
            () => service.SaveNativeProfile(id), ct,
            outerEnd: async adapted => { _ = await match.EndLocalRaidAsync("/client/match/local/end", adapted, id, ct); }))!)
]);

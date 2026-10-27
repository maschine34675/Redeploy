using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Utils;

namespace Redeploy.Server.Scav;

[Injectable]
public sealed class ScavRouter(JsonUtil json, ScavContinuationService service) : StaticRouter(json,
[
    new RouteAction<ScavPrepareRequest>("/redeploy/pmc-scav/prepare",
        async (_, request, id, _, ct) => json.Serialize(await service.Prepare(id, request, ct))!),
    new RouteAction<ScavControlRequest>("/redeploy/pmc-scav/activate",
        async (_, request, id, _, ct) => json.Serialize(await service.Control(id, request, false, ct))!),
    new RouteAction<ScavControlRequest>("/redeploy/pmc-scav/cancel",
        async (_, request, id, _, ct) => json.Serialize(await service.Control(id, request, true, ct))!)
]);

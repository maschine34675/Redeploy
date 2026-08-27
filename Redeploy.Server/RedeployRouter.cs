using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace Redeploy.Server;

/// <summary>
/// Ping route so the client can verify the server component is installed.
/// Without the server mod the profile gets corrupted after a respawn raid.
/// </summary>
[Injectable]
public sealed class RedeployRouter : StaticRouter
{
    public RedeployRouter(JsonUtil jsonUtil)
        : base(jsonUtil, GetRoutes())
    {
    }

    private static List<RouteAction> GetRoutes() =>
    [
        new RouteAction<EmptyRequestData>(
            "/redeploy/ping",
            // 4.1: jede Route-Action bekommt zusaetzlich ein CancellationToken
            (url, info, sessionId, output, _) => new ValueTask<string>("\"pong\""))
    ];
}

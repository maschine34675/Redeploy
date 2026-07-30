using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Utils;

namespace CorpseRun.Server;

/// <summary>
/// Ping route so the client can verify the server component is installed.
/// Without the server mod the profile gets corrupted after a respawn raid.
/// </summary>
[Injectable]
public sealed class CorpseRunRouter : StaticRouter
{
    public CorpseRunRouter(JsonUtil jsonUtil)
        : base(jsonUtil, GetRoutes())
    {
    }

    private static List<RouteAction> GetRoutes() =>
    [
        new RouteAction<EmptyRequestData>(
            "/corpserun/ping",
            (url, info, sessionId, output) => new ValueTask<string>("\"pong\""))
    ];
}

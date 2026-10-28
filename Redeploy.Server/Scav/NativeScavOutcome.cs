using System.Reflection;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Services.InRaid;

namespace Redeploy.Server.Scav;

/// <summary>Runs only the native Scav outcome inside the already-owned PMC raid end.</summary>
public static class NativeScavOutcome
{
    private delegate Task NativeHandler(LocationLifecycleService instance, MongoId sessionId,
        PmcData pmc, PmcData scav, bool isDead, bool isTransfer, bool isSurvived,
        EndLocalRaidRequestData request, CancellationToken cancellationToken);

    private static readonly NativeHandler Handler = Resolve().CreateDelegate<NativeHandler>();

    private static MethodInfo Resolve()
    {
        var method = typeof(LocationLifecycleService).GetMethod("HandlePostRaidPlayerScavAsync",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null,
            [typeof(MongoId), typeof(PmcData), typeof(PmcData), typeof(bool), typeof(bool), typeof(bool),
                typeof(EndLocalRaidRequestData), typeof(CancellationToken)], null);
        if (method == null || method.IsStatic || !method.IsFamily || method.ReturnType != typeof(Task))
            throw new MissingMethodException("The native Player-Scav outcome handler could not be verified.");
        return method;
    }

    public static void ValidateContract() => _ = Handler;

    public static Task Invoke(LocationLifecycleService instance, MongoId sessionId, PmcData pmc,
        PmcData scav, EndLocalRaidRequestData request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.Results);
        // A typed delegate reaches the patched native method and propagates its real
        // exceptions; MethodInfo.Invoke would wrap synchronous errors. Do not enter
        // EndLocalRaidAsync again: outer mods own the one issued PMC raid identity.
        return Handler(instance, sessionId, pmc, scav, request.Results.IsPlayerDead(),
            request.Results.IsMapToMapTransfer(), request.Results.IsPlayerSurvived(), request, cancellationToken);
    }
}

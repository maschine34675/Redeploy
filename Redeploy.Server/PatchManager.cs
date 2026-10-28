using Redeploy.Server.Patches;
using Redeploy.Server.Scav;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace Redeploy.Server;

// 4.1: PreSptModLoader existiert nicht mehr; Preload ist die frueheste Stufe nach
// dem Watermark und liegt weit vor dem ersten HTTP-Request, den die Patches abfangen.
[Injectable(TypePriority = OnLoadOrder.Preload + 50)]
public sealed class PatchManager(ScavContinuationService scavContinuations) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        new RedeployPostRaidPatch().Enable();
        new RedeploySetInventoryPatch().Enable();
        _ = scavContinuations;
        new ScavRaidStartPatch().Enable();
        new ScavRaidEndPatch().Enable();
        new ScavProfileSavePatch().Enable();
        new ScavResultUploadPatch().Enable();
        new ScavTransferFinishPatch().Enable();
        new ScavTransferInventoryPatch().Enable();
        new ScavRecoveryGameStartPatch().Enable();
        return Task.CompletedTask;
    }
}

using CorpseRun.Server.Patches;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace CorpseRun.Server;

// 4.1: PreSptModLoader existiert nicht mehr; Preload ist die frueheste Stufe nach
// dem Watermark und liegt weit vor dem ersten HTTP-Request, den die Patches abfangen.
[Injectable(TypePriority = OnLoadOrder.Preload + 50)]
public sealed class PatchManager : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        new PostRaidPatch().Enable();
        new SetInventoryPatch().Enable();
        return Task.CompletedTask;
    }
}

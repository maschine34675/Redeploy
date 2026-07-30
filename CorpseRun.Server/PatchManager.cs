using CorpseRun.Server.Patches;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace CorpseRun.Server;

[Injectable(TypePriority = OnLoadOrder.PreSptModLoader + 50)]
public sealed class PatchManager : IOnLoad
{
    public Task OnLoad()
    {
        new PostRaidPatch().Enable();
        new SetInventoryPatch().Enable();
        return Task.CompletedTask;
    }
}

using Redeploy.Server.Helpers;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Services.InRaid;
using System.Reflection;

namespace Redeploy.Server.Patches;

/// <summary>
/// Repairs the Inventory.Equipment root id after SPT finishes post-raid PMC handling.
/// </summary>
public sealed class RedeployPostRaidPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(LocationLifecycleService).GetMethod(
            "HandlePostRaidPmc",
            BindingFlags.Instance | BindingFlags.NonPublic,
            Type.DefaultBinder,
            [
                typeof(MongoId),
                typeof(SptProfile),
                typeof(PmcData),
                typeof(bool),
                typeof(bool),
                typeof(bool),
                typeof(EndLocalRaidRequestData),
                typeof(string)
            ],
            null)!;
    }

    // No prefix: on SPT 4.0.x, isDead is derived from the same ExitStatus the old guard
    // compared against SURVIVED — the two are disjoint, so the guard could never fire.

    [PatchPostfix]
    public static void Postfix(SptProfile fullServerProfile, EndLocalRaidRequestData request)
    {
        var pmcData = fullServerProfile?.CharacterData?.PmcData;
        var preferredEquipment = request?.Results?.Profile?.Inventory?.Equipment;
        InventoryEquipmentFixHelper.EnsureEquipmentRootIsValid(pmcData, preferredEquipment);
    }
}

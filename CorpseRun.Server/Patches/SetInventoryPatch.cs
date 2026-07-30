using CorpseRun.Server.Helpers;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Common;
using System.Reflection;

namespace CorpseRun.Server.Patches;

/// <summary>
/// SPT's SetInventory removes the old equipment tree but never updates Inventory.Equipment to the client's new root id.
/// After a respawn equipment swap the stale id breaks profile deserialization at raid end.
/// </summary>
public sealed class SetInventoryPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(InRaidHelper).GetMethod(
            nameof(InRaidHelper.SetInventory),
            BindingFlags.Instance | BindingFlags.Public)!;
    }

    [PatchPostfix]
    public static void Postfix(PmcData serverProfile, PmcData postRaidProfile)
    {
        InventoryEquipmentFixHelper.EnsureEquipmentRootIsValid(
            serverProfile,
            postRaidProfile?.Inventory?.Equipment);
    }
}

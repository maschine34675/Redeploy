using CorpseRun.Server.Helpers;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Services;
using System.Reflection;

namespace CorpseRun.Server.Patches;

/// <summary>
/// Ensures raids that end as Survived after a respawn are not processed as a death wipe.
/// </summary>
public sealed class PostRaidPatch : AbstractPatch
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

    [PatchPrefix]
    public static void Prefix(EndLocalRaidRequestData request, ref bool isDead)
    {
        if (request?.Results == null)
            return;

        if (request.Results.Result == ExitStatus.SURVIVED && isDead)
            isDead = false;
    }

    [PatchPostfix]
    public static void Postfix(SptProfile fullServerProfile, EndLocalRaidRequestData request)
    {
        var pmcData = fullServerProfile?.CharacterData?.PmcData;
        var preferredEquipment = request?.Results?.Profile?.Inventory?.Equipment;
        InventoryEquipmentFixHelper.EnsureEquipmentRootIsValid(pmcData, preferredEquipment);
    }
}

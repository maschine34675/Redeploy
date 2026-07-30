using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace CorpseRun.Server.Helpers;

internal static class InventoryEquipmentFixHelper
{
    private static readonly string[] EquipmentSlotIds =
    [
        "Pockets",
        "FirstPrimaryWeapon",
        "SecondPrimaryWeapon",
        "Holster",
        "Scabbard",
        "ArmorVest",
        "TacticalVest",
        "Headwear",
        "Earpiece",
        "FaceCover",
        "Eyewear",
        "ArmBand",
        "SecuredContainer",
    ];

    /// <summary>
    /// SPT's SetInventory merges raid equipment items but can leave Inventory.Equipment pointing at the removed pre-raid root id.
    /// </summary>
    public static void EnsureEquipmentRootIsValid(PmcData? profile, MongoId? preferredEquipmentId = null)
    {
        if (profile?.Inventory?.Items == null || profile.Inventory.Items.Count == 0)
            return;

        var itemIds = profile.Inventory.Items
            .Where(item => item?.Id != null)
            .Select(item => item.Id.ToString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (itemIds.Count == 0)
            return;

        if (preferredEquipmentId.HasValue
            && itemIds.Contains(preferredEquipmentId.Value.ToString()))
        {
            profile.Inventory.Equipment = preferredEquipmentId.Value;
        }
        else if (profile.Inventory.Equipment.HasValue
                 && itemIds.Contains(profile.Inventory.Equipment.Value.ToString()))
        {
            return;
        }
        else if (TryResolveEquipmentRootId(profile.Inventory.Items, itemIds, out var resolved))
        {
            profile.Inventory.Equipment = resolved;
        }

        SanitizeInventoryReferences(profile, itemIds);
    }

    private static bool TryResolveEquipmentRootId(
        List<Item> items,
        HashSet<string> itemIds,
        out MongoId equipmentRootId)
    {
        foreach (var slotId in EquipmentSlotIds)
        {
            var slottedItem = items.FirstOrDefault(item =>
                string.Equals(item.SlotId, slotId, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(item.ParentId));

            if (slottedItem?.ParentId == null)
                continue;

            var parentId = slottedItem.ParentId.ToString();
            if (itemIds.Contains(parentId))
            {
                equipmentRootId = new MongoId(parentId);
                return true;
            }
        }

        var rootItem = items.FirstOrDefault(item =>
            string.IsNullOrWhiteSpace(item.ParentId)
            && item.Id != null);

        if (rootItem?.Id != null)
        {
            equipmentRootId = rootItem.Id;
            return true;
        }

        equipmentRootId = default;
        return false;
    }

    private static void SanitizeInventoryReferences(PmcData profile, HashSet<string> itemIds)
    {
        if (profile.Inventory?.FastPanel != null)
        {
            foreach (var key in profile.Inventory.FastPanel
                         .Where(kvp => !itemIds.Contains(kvp.Value.ToString()))
                         .Select(kvp => kvp.Key)
                         .ToList())
            {
                profile.Inventory.FastPanel.Remove(key);
            }
        }

        // Keep InsuredItems entries for gear that was lost on a corpse and is reported via LostInsuredItems.
    }
}

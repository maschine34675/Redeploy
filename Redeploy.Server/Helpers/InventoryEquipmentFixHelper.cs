using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Redeploy.Server.Helpers;

internal static class InventoryEquipmentFixHelper
{
    // "Default Inventory" — the template every equipment root uses.
    private const string EquipmentTemplateId = "55d7217a4bdc2d86028b456d";

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

        var itemsById = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in profile.Inventory.Items)
        {
            if (item?.Id != null)
                itemsById.TryAdd(item.Id.ToString(), item);
        }

        if (itemsById.Count == 0)
            return;

        if (preferredEquipmentId.HasValue
            && IsEquipmentRoot(itemsById, preferredEquipmentId.Value.ToString()))
        {
            profile.Inventory.Equipment = preferredEquipmentId.Value;
        }
        else if (profile.Inventory.Equipment.HasValue
                 && IsEquipmentRoot(itemsById, profile.Inventory.Equipment.Value.ToString()))
        {
            return;
        }
        else if (TryResolveEquipmentRootId(profile.Inventory.Items, itemsById, out var resolved))
        {
            profile.Inventory.Equipment = resolved;
        }

        SanitizeInventoryReferences(
            profile,
            itemsById.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// A valid equipment root must exist, be parentless, and use the equipment template —
    /// a bare id-exists check could accept the stash or another profile root and corrupt
    /// the profile worse than the bug this helper fixes.
    /// </summary>
    private static bool IsEquipmentRoot(Dictionary<string, Item> itemsById, string candidateId)
    {
        return itemsById.TryGetValue(candidateId, out var item)
            && string.IsNullOrWhiteSpace(item.ParentId?.ToString())
            && string.Equals(item.Template.ToString(), EquipmentTemplateId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveEquipmentRootId(
        List<Item> items,
        Dictionary<string, Item> itemsById,
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
            if (IsEquipmentRoot(itemsById, parentId))
            {
                equipmentRootId = new MongoId(parentId);
                return true;
            }
        }

        // Fail closed: only a parentless item with the equipment template qualifies.
        // Never fall back to an arbitrary parentless item (stash, quest roots, ...).
        var rootItem = items.FirstOrDefault(item =>
            item?.Id != null
            && string.IsNullOrWhiteSpace(item.ParentId?.ToString())
            && string.Equals(item.Template.ToString(), EquipmentTemplateId, StringComparison.OrdinalIgnoreCase));

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

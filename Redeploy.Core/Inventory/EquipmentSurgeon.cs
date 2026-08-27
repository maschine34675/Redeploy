using Comfort.Common;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using System;
using System.Linq;

namespace Redeploy.Core.Inventory;

/// <summary>
/// Swaps a profile's equipment tree for a freshly created one while keeping the old
/// tree on the corpse. Transfers everything that must never be lost (special pocket
/// slots, secured container, scabbard, armband), lets an optional <c>populate</c>
/// callback fill the new tree (kit systems), then sanitizes all profile references
/// that still point at the old tree.
/// </summary>
public static class EquipmentSurgeon
{
    private static readonly AccessTools.FieldRef<Player, Corpse> CorpseField =
        AccessTools.FieldRefAccess<Player, Corpse>("Corpse");

    private static readonly AccessTools.FieldRef<EFT.InventoryLogic.Inventory, InventoryEquipment> EquipmentField =
        AccessTools.FieldRefAccess<EFT.InventoryLogic.Inventory, InventoryEquipment>(nameof(EFT.InventoryLogic.Inventory.Equipment));

    /// <summary>
    /// Replaces <c>profile.Inventory.Equipment</c> with a fresh root of the same template.
    /// With <paramref name="populate"/> null this is the "naked respawn": empty pockets of
    /// the same template plus the transferred retained/special slots.
    /// </summary>
    /// <param name="requireCorpse">When true (respawn path), skip silently if the old body
    /// has no corpse yet — the old tree would otherwise be orphaned into nothing.</param>
    public static void ReplaceEquipment(
        Profile profile,
        LocalPlayer oldBody,
        Action<InventoryEquipment, ItemFactory> populate = null,
        bool requireCorpse = true)
    {
        try
        {
            if (profile?.Inventory == null || oldBody == null)
                return;

            if (requireCorpse && CorpseField(oldBody) == null)
            {
                CoreLog.LogWarning("No corpse found; skipping naked equipment swap.");
                return;
            }

            var oldEquipment = profile.Inventory.Equipment;
            if (oldEquipment == null)
                return;

            var factory = Singleton<ItemFactory>.Instance;
            var newEquipment = (InventoryEquipment)factory.CreateItem(
                factory.NextId,
                oldEquipment.TemplateId,
                null);

            TryEquipEmptyPockets(oldEquipment, newEquipment, factory);
            TryTransferSpecialSlots(oldEquipment, newEquipment);
            TryTransferRetainedSlots(oldEquipment, newEquipment);

            populate?.Invoke(newEquipment, factory);

            EquipmentField(profile.Inventory) = newEquipment;
            ProfileInventorySanitizer.Sanitize(profile);
            CoreLog.LogInfo("Profile equipment swapped; corpse retains raid loadout.");
        }
        catch (Exception ex)
        {
            // Keep the historical log string for the naked path — it is what existing
            // troubleshooting docs and log greps look for.
            CoreLog.LogError(populate == null
                ? $"Naked equipment swap failed: {ex}"
                : $"Equipment swap failed: {ex}");
            throw;
        }
    }

    private static void TryEquipEmptyPockets(
        InventoryEquipment oldEquipment,
        InventoryEquipment newEquipment,
        ItemFactory factory)
    {
        var oldPockets = oldEquipment.GetSlot(EquipmentSlot.Pockets).ContainedItem;
        if (oldPockets == null)
            return;

        var newPockets = factory.CreateItem(factory.NextId, oldPockets.TemplateId, null);
        var result = newEquipment.GetSlot(EquipmentSlot.Pockets).AddWithoutRestrictions(newPockets);

        if (result.Failed)
            CoreLog.LogWarning($"Failed to equip empty pockets: {result.Error}");
    }

    // Secure container and special slots always transfer: the corpse tree is never
    // serialized into the post-raid profile, so anything left there is gone for good.
    private static void TryTransferSpecialSlots(
        InventoryEquipment oldEquipment,
        InventoryEquipment newEquipment)
    {
        var oldPockets = oldEquipment.GetSlot(EquipmentSlot.Pockets).ContainedItem as CompoundItem;
        var newPockets = newEquipment.GetSlot(EquipmentSlot.Pockets).ContainedItem as CompoundItem;
        if (oldPockets == null || newPockets == null)
            return;

        foreach (var oldSlot in oldPockets.Slots)
        {
            if (!oldSlot.IsSpecial)
                continue;

            var item = oldSlot.ContainedItem;
            if (item == null)
                continue;

            var newSlot = newPockets.Slots.FirstOrDefault(slot => slot.ID == oldSlot.ID);
            if (newSlot == null)
                continue;

            MoveSlotItem(oldSlot, newSlot, item, "special-slot item");
        }
    }

    // Slots that vanilla keeps on death and that are unlootable on corpses
    // (UnlootableComponent) — leaving them on our corpse would lose them for good.
    private static readonly EquipmentSlot[] RetainedSlots =
    {
        EquipmentSlot.SecuredContainer,
        EquipmentSlot.Scabbard,
        EquipmentSlot.ArmBand,
    };

    private static void TryTransferRetainedSlots(
        InventoryEquipment oldEquipment,
        InventoryEquipment newEquipment)
    {
        foreach (var slotType in RetainedSlots)
        {
            var oldSlot = oldEquipment.GetSlot(slotType);
            var item = oldSlot?.ContainedItem;
            if (item == null)
                continue;

            MoveSlotItem(oldSlot, newEquipment.GetSlot(slotType), item, slotType.ToString());
        }
    }

    public static void MoveSlotItem(Slot oldSlot, Slot newSlot, Item item, string label)
    {
        var removeResult = oldSlot.RemoveItemWithoutRestrictions();
        if (removeResult.Failed)
        {
            CoreLog.LogWarning($"Failed to detach {label} from corpse: {removeResult.Error}");
            return;
        }

        var addResult = newSlot.AddWithoutRestrictions(item);
        if (addResult.Failed)
        {
            // Put it back on the corpse tree — a parentless item would vanish from both
            // serialized equipment trees at raid end.
            var rollback = oldSlot.AddWithoutRestrictions(item);
            CoreLog.LogWarning(
                $"Failed to transfer {label}: {addResult.Error} — " +
                (rollback.Failed ? $"ROLLBACK ALSO FAILED: {rollback.Error}" : "rolled back onto corpse."));
        }
    }
}

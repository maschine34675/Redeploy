using Comfort.Common;
using CorpseRun.Config;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CorpseRun.Services;

internal static class RespawnEquipmentHelper
{
    private static readonly AccessTools.FieldRef<Player, Corpse> CorpseField =
        AccessTools.FieldRefAccess<Player, Corpse>("Corpse");

    private static readonly AccessTools.FieldRef<Inventory, InventoryEquipment> EquipmentField =
        AccessTools.FieldRefAccess<Inventory, InventoryEquipment>(nameof(Inventory.Equipment));

    private static readonly AccessTools.FieldRef<Inventory, EFT.InventoryLogic.FastAccess> FastAccessField =
        AccessTools.FieldRefAccess<Inventory, EFT.InventoryLogic.FastAccess>(nameof(Inventory.FastAccess));

    /// <summary>
    /// Replaces profile equipment with a fresh empty rig so the corpse keeps the old gear.
    /// </summary>
    public static void PrepareNakedRespawnProfile(Profile profile, LocalPlayer deadPlayer)
    {
        try
        {
            if (profile?.Inventory == null || deadPlayer == null)
                return;

            if (CorpseField(deadPlayer) == null)
            {
                Plugin.ModLogger.LogWarning("[CorpseRun] No corpse found; skipping naked equipment swap.");
                return;
            }

            var oldEquipment = profile.Inventory.Equipment;
            if (oldEquipment == null)
                return;

            var factory = Singleton<ItemFactoryClass>.Instance;
            var newEquipment = (InventoryEquipment)factory.CreateItem(
                factory.MongoID_0,
                oldEquipment.TemplateId,
                null);

            TryEquipEmptyPockets(oldEquipment, newEquipment, factory);
            TryTransferSpecialSlots(oldEquipment, newEquipment);
            TryTransferSecureContainer(oldEquipment, newEquipment);

            EquipmentField(profile.Inventory) = newEquipment;
            SanitizeProfileInventoryReferences(profile, oldEquipment);
            Plugin.ModLogger.LogInfo("[CorpseRun] Profile equipment swapped; corpse retains raid loadout.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError($"[CorpseRun] Naked equipment swap failed: {ex}");
            throw;
        }
    }

    private static void TryEquipEmptyPockets(
        InventoryEquipment oldEquipment,
        InventoryEquipment newEquipment,
        ItemFactoryClass factory)
    {
        var oldPockets = oldEquipment.GetSlot(EquipmentSlot.Pockets).ContainedItem;
        if (oldPockets == null)
            return;

        var newPockets = factory.CreateItem(factory.MongoID_0, oldPockets.TemplateId, null);
        var result = newEquipment.GetSlot(EquipmentSlot.Pockets).AddWithoutRestrictions(newPockets);

        if (result.Failed)
            Plugin.ModLogger.LogWarning($"[CorpseRun] Failed to equip empty pockets: {result.Error}");
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

    private static void TryTransferSecureContainer(
        InventoryEquipment oldEquipment,
        InventoryEquipment newEquipment)
    {
        var oldSlot = oldEquipment.GetSlot(EquipmentSlot.SecuredContainer);
        var secure = oldSlot.ContainedItem;
        if (secure == null)
            return;

        MoveSlotItem(oldSlot, newEquipment.GetSlot(EquipmentSlot.SecuredContainer), secure, "secure container");
    }

    private static void MoveSlotItem(Slot oldSlot, Slot newSlot, Item item, string label)
    {
        var removeResult = oldSlot.RemoveItemWithoutRestrictions();
        if (removeResult.Failed)
        {
            Plugin.ModLogger.LogWarning($"[CorpseRun] Failed to detach {label} from corpse: {removeResult.Error}");
            return;
        }

        var addResult = newSlot.AddWithoutRestrictions(item);
        if (addResult.Failed)
        {
            // Put it back on the corpse tree — a parentless item would vanish from both
            // serialized equipment trees at raid end.
            var rollback = oldSlot.AddWithoutRestrictions(item);
            Plugin.ModLogger.LogWarning(
                $"[CorpseRun] Failed to transfer {label}: {addResult.Error} — " +
                (rollback.Failed ? $"ROLLBACK ALSO FAILED: {rollback.Error}" : "rolled back onto corpse."));
        }
    }

    /// <summary>
    /// Drops stale item references that still point at the old corpse equipment tree.
    /// Insured items on the corpse are kept so raid-end insurance (method_13) can still find them.
    /// </summary>
    private static void SanitizeProfileInventoryReferences(Profile profile, InventoryEquipment corpseEquipment)
    {
        if (profile?.Inventory?.Equipment == null)
            return;

        var validIds = new HashSet<string>(
            profile.Inventory.GetPlayerItems(EPlayerItems.All).Select(item => item.Id.ToString()),
            StringComparer.Ordinal);

        var corpseItemIds = new HashSet<string>(
            corpseEquipment.GetAllItemsFromCollection().Select(item => item.Id.ToString()),
            StringComparer.Ordinal);

        RebuildFastAccess(profile.Inventory, validIds);
        PreserveInsuredItemsOnCorpse(profile, validIds, corpseItemIds);
        SanitizeFavoriteItems(profile.Inventory, validIds);
        SanitizeDiscardLimits(profile.Inventory, validIds);
        SanitizeCheckedMagazines(profile, validIds);
    }

    private static void RebuildFastAccess(Inventory inventory, HashSet<string> validIds)
    {
        var boundIds = new Dictionary<EBoundItem, MongoID>();
        foreach (var binding in inventory.FastAccess.BoundItems)
        {
            if (validIds.Contains(binding.Value.Id.ToString()))
                boundIds[binding.Key] = binding.Value.Id;
        }

        FastAccessField(inventory) = new EFT.InventoryLogic.FastAccess(inventory.Equipment, boundIds);
    }

    private static void PreserveInsuredItemsOnCorpse(
        Profile profile,
        HashSet<string> validIds,
        HashSet<string> corpseItemIds)
    {
        if (profile.InsuredItems == null || profile.InsuredItems.Length == 0)
            return;

        profile.InsuredItems = profile.InsuredItems
            .Where(insured =>
                insured != null
                && (validIds.Contains(insured.ItemId) || corpseItemIds.Contains(insured.ItemId)))
            .ToArray();
    }

    private static void SanitizeFavoriteItems(Inventory inventory, HashSet<string> validIds)
    {
        foreach (var staleId in inventory.FavoriteItemsStorage
                     .Where(id => !validIds.Contains(id.ToString()))
                     .ToList())
        {
            inventory.FavoriteItemsStorage.Remove(staleId);
        }
    }

    private static void SanitizeDiscardLimits(Inventory inventory, HashSet<string> validIds)
    {
        foreach (var staleId in inventory.DiscardLimits.Keys
                     .Where(id => !validIds.Contains(id.ToString()))
                     .ToList())
        {
            inventory.DiscardLimits.Remove(staleId);
        }
    }

    private static void SanitizeCheckedMagazines(Profile profile, HashSet<string> validIds)
    {
        foreach (var staleId in profile.CheckedMagazines.Keys
                     .Where(id => !validIds.Contains(id.ToString()))
                     .ToList())
        {
            profile.CheckedMagazines.Remove(staleId);
        }

        profile.CheckedChambers.RemoveAll(id => !validIds.Contains(id.ToString()));
    }
}

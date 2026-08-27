using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Redeploy.Core.Inventory;

/// <summary>
/// Drops stale item references that still point at a replaced equipment tree.
/// Profile.InsuredItems is deliberately NOT touched: raid-end insurance (method_13)
/// intersects it with world items, stale entries are harmless everywhere, and any
/// filtering here would strip insurance for dropped items and earlier corpses.
/// </summary>
public static class ProfileInventorySanitizer
{
    private static readonly AccessTools.FieldRef<EFT.InventoryLogic.Inventory, EFT.InventoryLogic.FastAccess> FastAccessField =
        AccessTools.FieldRefAccess<EFT.InventoryLogic.Inventory, EFT.InventoryLogic.FastAccess>(nameof(EFT.InventoryLogic.Inventory.FastAccess));

    public static void Sanitize(Profile profile)
    {
        if (profile?.Inventory?.Equipment == null)
            return;

        var validIds = new HashSet<string>(
            profile.Inventory.GetPlayerItems(EPlayerItems.All).Select(item => item.Id.ToString()),
            StringComparer.Ordinal);

        RebuildFastAccess(profile.Inventory, validIds);
        SanitizeFavoriteItems(profile.Inventory, validIds);
        SanitizeDiscardLimits(profile.Inventory, validIds);
        SanitizeCheckedMagazines(profile, validIds);
    }

    private static void RebuildFastAccess(EFT.InventoryLogic.Inventory inventory, HashSet<string> validIds)
    {
        var boundIds = new Dictionary<EBoundItem, MongoID>();
        foreach (var binding in inventory.FastAccess.BoundItems)
        {
            if (validIds.Contains(binding.Value.Id.ToString()))
                boundIds[binding.Key] = binding.Value.Id;
        }

        FastAccessField(inventory) = new EFT.InventoryLogic.FastAccess(inventory.Equipment, boundIds);
    }

    private static void SanitizeFavoriteItems(EFT.InventoryLogic.Inventory inventory, HashSet<string> validIds)
    {
        foreach (var staleId in inventory.FavoriteItemsStorage
                     .Where(id => !validIds.Contains(id.ToString()))
                     .ToList())
        {
            inventory.FavoriteItemsStorage.Remove(staleId);
        }
    }

    private static void SanitizeDiscardLimits(EFT.InventoryLogic.Inventory inventory, HashSet<string> validIds)
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

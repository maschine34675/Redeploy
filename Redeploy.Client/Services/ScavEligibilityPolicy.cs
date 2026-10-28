using EFT.InventoryLogic;
using JsonType;
using System.Collections.Generic;
using System.Linq;

namespace Redeploy.Services;

/// <summary>Read-only prerequisites shared by the choice prompt and preparation.</summary>
internal static class ScavEligibilityPolicy
{
    public const string PriorDeliveryReason = "Scav continuation is unavailable after sending items through BTR or transit.";
    public const string BtrDeliveryReason = "BTR item delivery is unavailable while continuing as your own Scav. Extract to transfer your loot to the stash.";

    public static string UnavailableReason(bool hasDeathSnapshot, bool hasRaidIdentity,
        bool hasScavExits, bool hasExitAuthority, bool hasDeliveredItems)
    {
        if (!hasDeathSnapshot)
            return "The PMC death snapshot is unavailable.";
        if (!hasRaidIdentity)
            return "The original raid identity is unavailable.";
        if (!hasScavExits)
            return "This location has no Scav extraction points.";
        if (!hasExitAuthority)
            return "Scav extraction compatibility could not be verified.";
        return hasDeliveredItems ? PriorDeliveryReason : null;
    }

    // Native GetTransferItems serializes the stash root even when its grids are empty.
    // A direct child in any grid is enough to prove actual delivery, without flattening
    // every item tree on each prompt OnGUI call. Temporary, unpurchased grids are not
    // this stash and are deliberately not treated as delivered contents.
    public static bool HasDeliveredItems(Stash stash)
        => stash?.Grids != null && stash.Grids.Any(grid => grid?.ItemCollection?.Any() == true);

    public static FlatItem[] SelectInsuredSnapshot(IEnumerable<FlatItem> serialized, IEnumerable<string> insuredIds)
    {
        var insured = new HashSet<string>(insuredIds);
        var snapshot = new Dictionary<string, FlatItem>();
        foreach (var item in serialized)
        {
            string id = item._id;
            if (!insured.Contains(id))
                continue;
            // Match native deduplication: preserve the record with a real parent.
            if (snapshot.TryGetValue(id, out var previous)
                && previous.parentId.HasValue && !string.IsNullOrEmpty(previous.parentId.Value))
                continue;
            snapshot[id] = item;
        }
        return snapshot.Values.ToArray();
    }
}

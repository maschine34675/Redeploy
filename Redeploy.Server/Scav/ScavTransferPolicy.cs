using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace Redeploy.Server.Scav;

public static class ScavTransferPolicy
{
    // EFT ItemFactory.CreateFakeStash, BtrController and LocalTransitController.
    // GetTransferItems emits the root even when its transfer grids contain no items.
    public const string TransferRootId = "656f0f98d80a697f855d34b1";
    public const string TransferRootTemplate = "566abbc34bdc2d92178b4576";

    public static bool ContainsNoTransferredItems(Dictionary<string, IEnumerable<Item>>? transfers)
    {
        if (transfers == null) return true;
        foreach (var pair in transfers)
        {
            if (pair.Value == null) return false;
            // Bound enumeration: any second item is actual contents or a malformed graph.
            var items = pair.Value.Take(2).ToArray();
            if (items.Length == 0) continue;
            if (items.Length != 1) return false;
            var root = items[0];
            if (root == null || root.Id.ToString() != TransferRootId
                || root.Template.ToString() != TransferRootTemplate
                || root.ParentId != null || root.SlotId != null || root.Location != null
                || (pair.Key != TransferRootId + "_btr" && pair.Key != TransferRootId + "_transit"))
                return false;
        }
        return true;
    }
}

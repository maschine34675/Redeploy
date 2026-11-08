using System.Runtime.CompilerServices;
using BepInEx;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using Softwyx.LootInVicinity.LivPlayer;
using Softwyx.LootInVicinity.Raid;
using Softwyx.LootInVicinity.Session;
using UnityEngine;

namespace UnityEngine { public class Transform : Component { public int childCount; } }
namespace EFT.InventoryLogic
{
    public class Inventory { }
    public class InventoryController { public Inventory Inventory = new(); }
    public class Stash { public Grid[] Grids = { new() }; }
    public class Grid { public List<Item> Items = new(); }
    public class Item { public string Id; public Address CurrentAddress; public bool Obsolete; }
    public class Address { public object Owner; public object GetOwnerOrNull() => Owner; }
}
namespace EFT.Interactive { public class LootItem : Component { public Item Item; public object ItemOwner; } }
namespace EFT.UI { public class ItemsPanel : MonoBehaviour { public Inventory Inventory; } }
namespace EFT
{
    public partial class Player { public InventoryController InventoryController = new(); }
    public partial class GameWorld { public readonly Dictionary<object, object> ItemOwners = new(); }
}

namespace Softwyx.LootInVicinity
{
    [BepInPlugin("com.softwyx.lootinvicinity", "Loot In Vicinity", "3.0.6")]
    public class LootInVicinityPlugin : BaseUnityPlugin { }
}
namespace Softwyx.LootInVicinity.LivPlayer
{
    internal static class VicinityLocalPlayer
    {
        internal static Player _raidPlayer;
        internal static InventoryController _cachedController;

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static bool TryBind()
        {
            if (_raidPlayer != null && _cachedController != null) return true;
            _raidPlayer = GamePlayerOwner.MyPlayer;
            _cachedController = _raidPlayer?.InventoryController;
            return _raidPlayer != null;
        }
        internal static Player Find() { TryBind(); return _raidPlayer; }
    }
}
namespace Softwyx.LootInVicinity.Raid
{
    internal static class VicinityRaidServices { public static Stash RadiusStash { get; } = new(); }
}
namespace Softwyx.LootInVicinity.Session
{
    internal static class VicinityStagingRegistry { internal static readonly HashSet<string> StagedItemIds = new(); }
    internal static class VicinityTakeCleanup { internal static readonly HashSet<string> PendingTakeIds = new(); }
    internal static class VicinityListedLootRegistry { internal static readonly Dictionary<string, LootItem> ListedToWorld = new(); }
    internal static class VicinityLootSession { internal static bool IsPopulating { get; set; } internal static bool SuppressWorldSync { get; set; } }
}
namespace Softwyx.LootInVicinity.Ui
{
    internal static class UiAccess { internal static bool WorldLootOpen; public static void SetWorldLootOpen(bool value) => WorldLootOpen = value; }
    internal static class VicinityUiReflection
    {
        internal static Transform Drag;
        public static Transform GetDragLayer() => Drag;
        public static Inventory GetPanelInventory(ItemsPanel panel) => panel.Inventory;
    }
    internal static class VicinityPanelPresenter
    {
        internal static MonoBehaviour _attachHost;
        internal static ItemsPanel BoundItemsPanel { get; set; }
        internal static bool HasActiveVicinityWork { get; set; }
        internal static bool IsInventoryClosing { get; set; }
        internal static int Closed, Cancelled;
        public static void CancelAttachRoutine() { _attachHost = null; Cancelled++; }
        public static void HideIfActive(ItemsPanel panel)
        {
            if (!HasActiveVicinityWork) return;
            // Upstream Close also drops/clears staged items; any call with staged assets fails this test.
            if (VicinityStagingRegistry.StagedItemIds.Count > 0 || VicinityTakeCleanup.PendingTakeIds.Count > 0)
                throw new Exception("unsafe clearing of real items");
            Closed++;
            VicinityListedLootRegistry.ListedToWorld.Clear();
            VicinityRaidServices.RadiusStash.Grids[0].Items.Clear();
            HasActiveVicinityWork = false;
            BoundItemsPanel = null;
        }
    }
}
namespace Softwyx.LootInVicinity.Ui.Handlers
{
    internal static class VicinityItemsPanelOpenHandler { internal static int _openGeneration; }
}
namespace Softwyx.LootInVicinity.World
{
    internal static class VicinityListedWorldCleanup { public static bool IsWorldRepresentationObsolete(Item item) => item.Obsolete; }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal sealed class LootInVicinityContract
{
    internal static readonly Version Reviewed = new(3, 0, 6, 0);
    internal MethodInfo TryBind;
    private FieldInfo _player, _controller, _generation, _staged, _pending, _listed, _attachHost;
    private MethodInfo _stash, _activePanel, _boundPanel, _panelInventory, _populating, _syncing, _closing, _dragLayer;
    private MethodInfo _hide, _cancelAttach, _worldLootOpen, _obsolete;

    internal static LootInVicinityContract Resolve(Type plugin, string guid, Version version)
    {
        const string ns = "Softwyx.LootInVicinity.";
        var assembly = StandaloneHudContract.Plugin(plugin, guid, LootInVicinityCompat.PluginGuid, version,
            Reviewed, ns + "LootInVicinityPlugin", "Softwyx-LootInVicinity", Reviewed);
        Type T(string name) => assembly.GetType(ns + name, true);
        var local = T("LivPlayer.VicinityLocalPlayer");
        var panel = T("Ui.VicinityPanelPresenter");
        var session = T("Session.VicinityLootSession");
        MethodInfo M(Type owner, string name, Type result, params Type[] args) =>
            StandaloneHudContract.Method(owner, name, result, true, args);
        return new LootInVicinityContract
        {
            TryBind = M(local, "TryBind", typeof(bool)),
            _player = StandaloneHudContract.Field(local, "_raidPlayer", typeof(Player), true),
            _controller = StandaloneHudContract.Field(local, "_cachedController", typeof(InventoryController), true),
            _generation = StandaloneHudContract.Field(T("Ui.Handlers.VicinityItemsPanelOpenHandler"), "_openGeneration", typeof(int), true),
            _staged = ReadOnlyField(T("Session.VicinityStagingRegistry"), "StagedItemIds", typeof(HashSet<string>)),
            _pending = ReadOnlyField(T("Session.VicinityTakeCleanup"), "PendingTakeIds", typeof(HashSet<string>)),
            _listed = ReadOnlyField(T("Session.VicinityListedLootRegistry"), "ListedToWorld", typeof(Dictionary<string, LootItem>)),
            _stash = M(T("Raid.VicinityRaidServices"), "get_RadiusStash", typeof(Stash)),
            _activePanel = M(panel, "get_HasActiveVicinityWork", typeof(bool)),
            _boundPanel = M(panel, "get_BoundItemsPanel", typeof(ItemsPanel)),
            _attachHost = StandaloneHudContract.Field(panel, "_attachHost", typeof(MonoBehaviour), true),
            _panelInventory = M(T("Ui.VicinityUiReflection"), "GetPanelInventory", typeof(EFT.InventoryLogic.Inventory), typeof(ItemsPanel)),
            _closing = M(panel, "get_IsInventoryClosing", typeof(bool)),
            _populating = M(session, "get_IsPopulating", typeof(bool)),
            _syncing = M(session, "get_SuppressWorldSync", typeof(bool)),
            _dragLayer = M(T("Ui.VicinityUiReflection"), "GetDragLayer", typeof(Transform)),
            _cancelAttach = M(panel, "CancelAttachRoutine", typeof(void)),
            _hide = M(panel, "HideIfActive", typeof(void), typeof(ItemsPanel)),
            _worldLootOpen = M(T("Ui.UiAccess"), "SetWorldLootOpen", typeof(void), typeof(bool)),
            _obsolete = M(T("World.VicinityListedWorldCleanup"), "IsWorldRepresentationObsolete", typeof(bool), typeof(Item))
        };
    }

    private static FieldInfo ReadOnlyField(Type owner, string name, Type type)
    {
        var field = owner.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field == null || !field.IsInitOnly || field.FieldType != type)
            throw new MissingFieldException(owner.FullName, name);
        return field;
    }

    internal bool CheckOwner(Player player, InventoryController controller)
    {
        var cachedPlayer = _player.GetValue(null);
        var cachedController = _controller.GetValue(null);
        if (ReferenceEquals(cachedPlayer, null) && ReferenceEquals(cachedController, null)) return false;
        if (!ReferenceEquals(cachedPlayer, player) || !ReferenceEquals(cachedController, controller))
            throw new InvalidOperationException("Loot In Vicinity's cache belongs to another player or inventory controller.");
        return true;
    }

    internal void CheckLoot(GameWorld world, InventoryController oldController, bool cached)
    {
        // ClearStashContents also drops staged items. Its TryDropAtFeet catches errors and still
        // unregisters the item. Never ask it to do that as a side effect of replacing a body.
        if (((HashSet<string>)_staged.GetValue(null)).Count != 0)
            throw new InvalidOperationException("Loot In Vicinity still has player items staged in its vicinity grid.");
        if (((HashSet<string>)_pending.GetValue(null)).Count != 0 || Flag(_populating) || Flag(_closing) || Flag(_syncing))
            throw new InvalidOperationException("Loot In Vicinity is finishing an inventory operation.");

        bool active = Flag(_activePanel);
        if (active && !cached)
            throw new InvalidOperationException("Loot In Vicinity has an active panel without the outgoing player's inventory.");
        CheckPanel(_boundPanel.Invoke(null, null) as ItemsPanel, oldController);
        CheckPanel(_attachHost.GetValue(null) as ItemsPanel, oldController);
        if (active && _dragLayer.Invoke(null, null) is Transform drag && drag != null && drag.childCount != 0)
            throw new InvalidOperationException("Loot In Vicinity cannot close while an inventory drag is in progress.");

        // Display-only rows keep the real world's address. A move with its callback still pending
        // can already have changed that address before PendingTakeIds records a pending destroy.
        var listed = (Dictionary<string, LootItem>)_listed.GetValue(null);
        var stash = (Stash)_stash.Invoke(null, null);
        int rows = 0;
        if (stash != null)
        {
            if (stash.Grids == null || stash.Grids.Length != 1 || stash.Grids[0] == null)
                throw new InvalidOperationException("Loot In Vicinity's fake stash grid changed.");
            foreach (var item in stash.Grids[0].Items)
            {
                if (item == null || !listed.TryGetValue(item.Id, out var pile) || pile == null || pile.ItemOwner == null
                    || !world.ItemOwners.ContainsKey(pile.ItemOwner) || !ReferenceEquals(pile.Item, item)
                    || !ReferenceEquals(item.CurrentAddress?.GetOwnerOrNull(), pile.ItemOwner)
                    || (bool)_obsolete.Invoke(null, new object[] { item }))
                    throw new InvalidOperationException("Loot In Vicinity has an item in transit between the world and an inventory.");
                rows++;
            }
        }
        if (rows != listed.Count || (rows != 0 && !active))
            throw new InvalidOperationException("Loot In Vicinity's world rows have not finished settling.");
    }

    private void CheckPanel(ItemsPanel panel, InventoryController controller)
    {
        if (panel == null) return;
        var inventory = _panelInventory.Invoke(null, new object[] { panel });
        if (inventory != null && !ReferenceEquals(inventory, controller.Inventory))
            throw new InvalidOperationException("Loot In Vicinity's panel is displaying a different inventory.");
    }

    internal void ClosePanel()
    {
        _generation.SetValue(null, unchecked((int)_generation.GetValue(null) + 1));
        // Close first: CancelAttachRoutine clears the in-progress flag used by HideIfActive.
        // This is the upstream close of a verified display; the raid's fake stash and trader survive.
        if (Flag(_activePanel)) _hide.Invoke(null, new object[] { null });
        _cancelAttach.Invoke(null, null);
    }

    internal void SetCache(Player player, InventoryController controller)
    {
        _player.SetValue(null, player);
        _controller.SetValue(null, controller);
    }

    internal void EndOldLootScreen() => _worldLootOpen.Invoke(null, new object[] { false });
    private static bool Flag(MethodInfo property) => (bool)property.Invoke(null, null);
}

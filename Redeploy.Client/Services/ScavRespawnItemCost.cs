using System;
using System.Linq;
using EFT;
using EFT.InventoryLogic;
using Redeploy.Config;

namespace Redeploy.Services;

internal sealed class ScavCostRestorationException : InvalidOperationException
{
    public ScavCostRestorationException(Exception inner)
        : base("The respawn cost snapshot could not restore its original item. Raid saving has been stopped to protect your profile.", inner) { }
}

/// <summary>
/// Freezes the exact cost item. The request snapshot excludes it, but the live
/// corpse keeps it until the server reservation and resource preload succeed.
/// </summary>
internal sealed class ScavRespawnItemCost
{
    private readonly LocalPlayer _player;
    private readonly Item _item;
    private readonly string _name;
    private bool _committed;

    public ScavRespawnItemCost(LocalPlayer player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _name = Settings.RequiredRespawnItem.Value;
        var templateId = Settings.GetRequiredRespawnItemTemplateId();
        if (string.IsNullOrEmpty(templateId))
            return;
        _item = player.Profile.Inventory.GetPlayerItems(EPlayerItems.Equipment)
            .FirstOrDefault(item => item.TemplateId == templateId)
            ?? throw new InvalidOperationException("The required respawn item is no longer available.");
    }

    public T CaptureWithoutCostItem<T>(Func<T> serialize)
    {
        if (_committed)
            throw new InvalidOperationException("The respawn cost was already committed.");
        var removal = RemoveExactItem();
        try { return serialize(); }
        finally
        {
            if (removal != null)
            {
                try
                {
                    removal.RollBack();
                    if (!ContainsExactItem())
                        throw new InvalidOperationException("The exact cost item is missing after rollback.");
                }
                catch (Exception ex) { throw new ScavCostRestorationException(ex); }
            }
        }
    }

    public void Commit()
    {
        if (_committed)
            throw new InvalidOperationException("The respawn cost was already committed.");
        RemoveExactItem();
        _committed = true;
        if (_item != null)
            Plugin.ModLogger.LogInfo($"Consumed respawn item {_name}.");
    }

    private bool ContainsExactItem()
        => _player.Profile.Inventory.GetPlayerItems(EPlayerItems.Equipment)
            .Any(item => ReferenceEquals(item, _item));

    private RemoveResult RemoveExactItem()
    {
        if (_item == null)
            return null;
        if (!ContainsExactItem() || _item.Parent == null)
            throw new InvalidOperationException("The reserved respawn item is no longer available.");
        var result = ItemManipulator.RemoveWithoutRestrictions(_item, _player.InventoryController);
        if (result.Failed)
            throw new InvalidOperationException($"The required respawn item could not be consumed: {result.Error}");
        return result.Value;
    }
}

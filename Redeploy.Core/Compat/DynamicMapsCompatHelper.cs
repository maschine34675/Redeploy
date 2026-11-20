using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using System;
using System.Collections;
using System.Reflection;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>
/// Rebinds DynamicMaps' retained own-position marker and, since 1.3.0, its active
/// quest/inventory subscriptions. A continuously visible minimap does not receive a
/// hide/show transition when Core preserves BattleUI across a body replacement.
/// </summary>
public static class DynamicMapsCompatHelper
{
    private const string PluginGuid = "com.mpstark.dynamicmaps";
    private static Type _resolvedPluginType;
    private static Type _providerType;
    private static FieldInfo _mapField;
    private static FieldInfo _providersField;
    private static FieldInfo _playerMarkerField;
    private static PropertyInfo _markerPlayerProperty;
    private static DynamicMapsProviderRebind _providerRebind;
    private static bool _reportedFailure;

    public static void RebindPlayerMarker(LocalPlayer newPlayer)
    {
        try
        {
            // Reject stale callbacks and another world's body before any foreign code.
            if (newPlayer == null || !Singleton<GameWorld>.Instantiated ||
                !ReferenceEquals(Singleton<GameWorld>.Instance.MainPlayer, newPlayer) ||
                !ReferenceEquals(newPlayer.GameWorld, Singleton<GameWorld>.Instance))
                return;

            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info) || info.Instance == null)
                return;

            var plugin = info.Instance;
            Resolve(plugin.GetType(), info.Metadata.Version);
            var map = _mapField?.GetValue(plugin);
            var providers = map == null ? null : _providersField?.GetValue(map) as IDictionary;
            if (providers == null)
                return;

            // A disabled/missing arrow must not prevent the independent event repair.
            try
            {
                var provider = _providerType != null && providers.Contains(_providerType)
                    ? providers[_providerType] : null;
                var marker = provider == null ? null : _playerMarkerField?.GetValue(provider);
                if (marker != null && _markerPlayerProperty != null &&
                    !ReferenceEquals(_markerPlayerProperty.GetValue(marker, null), newPlayer))
                {
                    _markerPlayerProperty.SetValue(marker, newPlayer, null);
                    CoreLog.LogDebug("DynamicMaps compat: rebound own-position marker to new player.");
                }
            }
            catch (Exception ex)
            {
                ReportFailure("own-position marker", ex);
            }

            var refreshed = _providerRebind?.Refresh(map, providers, newPlayer.QuestController,
                newPlayer.InventoryController, newPlayer.Inventory?.Equipment,
                newPlayer.Side == EPlayerSide.Savage, ReportFailure) ?? 0;
            if (refreshed != 0)
                CoreLog.LogDebug($"DynamicMaps compat: refreshed {refreshed} active body-bound provider(s).");
        }
        catch (Exception ex)
        {
            ReportFailure("body rebind", ex);
        }
    }

    private static void Resolve(Type pluginType, Version version)
    {
        if (_resolvedPluginType == pluginType)
            return;

        _resolvedPluginType = pluginType;
        _reportedFailure = false;
        var assembly = pluginType.Assembly;
        var mapScreenType = assembly.GetType("DynamicMaps.UI.ModdedMapScreen", false);
        _providerType = assembly.GetType("DynamicMaps.DynamicMarkers.PlayerMarkerProvider", false);
        _mapField = AccessTools.Field(pluginType, "Map");
        _providersField = mapScreenType == null ? null : AccessTools.Field(mapScreenType, "_dynamicMarkerProviders");
        _playerMarkerField = _providerType == null ? null : AccessTools.Field(_providerType, "_playerMarker");
        _markerPlayerProperty = _playerMarkerField == null ? null : AccessTools.Property(_playerMarkerField.FieldType, "Player");
        _providerRebind = null;

        if (_mapField == null || _providersField == null)
        {
            ReportFailure("map contract", new MissingMemberException("Plugin.Map / ModdedMapScreen._dynamicMarkerProviders"));
            return;
        }

        // Older DynamicMaps does not have these per-body subscriptions or visibility flags.
        if (version != null && version >= new Version(1, 3, 0))
        {
            try { _providerRebind = DynamicMapsProviderRebind.Resolve(mapScreenType, assembly); }
            catch (Exception ex) { ReportFailure("provider contract", ex); }
        }
    }

    private static void ReportFailure(string part, Exception exception)
    {
        if (_reportedFailure)
            return;
        _reportedFailure = true;
        var reason = exception is TargetInvocationException invoked && invoked.InnerException != null
            ? invoked.InnerException : exception;
        CoreLog.LogWarning($"DynamicMaps compat {part} failed; reopen the map if markers stop updating: {reason.GetType().Name}: {reason.Message}");
    }
}

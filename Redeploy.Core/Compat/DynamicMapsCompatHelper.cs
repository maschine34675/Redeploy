using EFT;
using HarmonyLib;
using System;
using System.Collections;
using System.Reflection;

namespace Redeploy.Core.Compat;

/// <summary>
/// SPT-DynamicMaps' PlayerMarkerProvider creates its own-position marker once (raid start /
/// map open) and caches the Player reference on it forever — TryAddMarker early-returns once
/// the marker exists, so it never learns about the new player after our respawn. The marker
/// keeps tracking the corpse's frozen position instead of moving with the respawned player.
/// No-op when DynamicMaps isn't loaded.
/// </summary>
public static class DynamicMapsCompatHelper
{
    private static bool _resolved;
    private static Type _pluginType;
    private static Type _providerType;
    private static FieldInfo _instanceField;
    private static FieldInfo _mapField;
    private static FieldInfo _providersField;
    private static FieldInfo _playerMarkerField;
    private static PropertyInfo _markerPlayerProperty;

    public static void RebindPlayerMarker(LocalPlayer newPlayer)
    {
        if (newPlayer == null)
            return;

        try
        {
            Resolve();

            if (_pluginType == null)
                return;

            var pluginInstance = _instanceField.GetValue(null);
            var map = pluginInstance != null ? _mapField.GetValue(pluginInstance) : null;
            if (map == null)
                return;

            var providers = _providersField.GetValue(map) as IDictionary;
            if (providers == null || !providers.Contains(_providerType))
                return;

            var provider = providers[_providerType];
            var marker = _playerMarkerField.GetValue(provider);
            if (marker == null)
                return;

            _markerPlayerProperty.SetValue(marker, newPlayer, null);
            CoreLog.LogInfo("DynamicMaps compat: rebound own-position marker to new player.");
        }
        catch (Exception ex)
        {
            CoreLog.LogWarning($"DynamicMaps compat rebind failed: {ex.Message}");
        }
    }

    private static void Resolve()
    {
        if (_resolved)
            return;

        _resolved = true;

        Type mapScreenType = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            _pluginType ??= assembly.GetType("DynamicMaps.Plugin", false);
            mapScreenType ??= assembly.GetType("DynamicMaps.UI.ModdedMapScreen", false);
            _providerType ??= assembly.GetType("DynamicMaps.DynamicMarkers.PlayerMarkerProvider", false);
            if (_pluginType != null && mapScreenType != null && _providerType != null)
                break;
        }

        if (_pluginType == null || mapScreenType == null || _providerType == null)
        {
            _pluginType = null;
            return;
        }

        _instanceField = AccessTools.Field(_pluginType, "Instance");
        _mapField = AccessTools.Field(_pluginType, "Map");
        _providersField = AccessTools.Field(mapScreenType, "_dynamicMarkerProviders");
        _playerMarkerField = AccessTools.Field(_providerType, "_playerMarker");

        var markerType = _playerMarkerField?.FieldType;
        _markerPlayerProperty = markerType != null ? AccessTools.Property(markerType, "Player") : null;

        if (_instanceField == null || _mapField == null || _providersField == null ||
            _playerMarkerField == null || _markerPlayerProperty == null)
        {
            _pluginType = null;
        }
    }
}

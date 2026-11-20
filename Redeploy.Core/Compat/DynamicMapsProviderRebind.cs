using System;
using System.Collections;
using System.Reflection;

namespace Redeploy.Core.Compat;

/// <summary>
/// The bounded 1.3.0 private contract. Only provider-local hide/show is replayed:
/// raid-end/disable discards same-world quest data and would trigger scene discovery.
/// No old-body references are retained by Core.
/// </summary>
internal sealed class DynamicMapsProviderRebind
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly FieldInfo _active, _inRaid, _mapView;
    private readonly Provider _quests, _doors;

    private DynamicMapsProviderRebind(Type screen, Assembly assembly)
    {
        _active = Field(screen, "_providersActive", typeof(bool));
        _inRaid = Field(screen, "_providersInRaid", typeof(bool));
        _mapView = Field(screen, "_mapView");
        _quests = new Provider(assembly.GetType("DynamicMaps.QuestMarkerProvider", true),
            _mapView.FieldType, "_questController", "_map");
        _doors = new Provider(assembly.GetType("DynamicMaps.DynamicMarkers.LockedDoorMarkerMutator", true),
            _mapView.FieldType, "_inventoryController", "_equipment");
    }

    internal static DynamicMapsProviderRebind Resolve(Type screen, Assembly assembly) => new(screen, assembly);

    internal int Refresh(object screen, IDictionary providers, object quests, object inventory,
        object equipment, bool isScav, Action<string, Exception> failed)
    {
        if (!(bool)_active.GetValue(screen) || !(bool)_inRaid.GetValue(screen))
            return 0;
        var map = _mapView.GetValue(screen);
        if (map == null)
            return 0;

        var count = 0;
        // Hide always precedes Show. In particular OnShowInRaid returns early for a
        // Scav and cannot itself detach the old PMC's quest event handlers/markers.
        if (Refresh(_quests, providers, map, isScav ? null : quests, map, failed)) count++;
        if (Refresh(_doors, providers, map, inventory, equipment, failed)) count++;
        return count;
    }

    private static bool Refresh(Provider contract, IDictionary providers, object map,
        object controller, object owner, Action<string, Exception> failed)
    {
        try
        {
            if (!providers.Contains(contract.Type))
                return false; // Config-disabled providers must remain disabled.
            var provider = providers[contract.Type];
            if (provider == null || !contract.Type.IsInstanceOfType(provider))
                return false;
            if (ReferenceEquals(contract.Controller.GetValue(provider), controller) &&
                ReferenceEquals(contract.Owner.GetValue(provider), owner))
                return false; // Already refreshed naturally, or repeated callback.

            var args = new[] { map };
            contract.Hide.Invoke(provider, args);
            contract.Show.Invoke(provider, args);
            return true;
        }
        catch (Exception ex)
        {
            // Do not interrupt Core or the other provider after one optional failure.
            // A failed hide must not be followed by show (duplicate subscriptions).
            failed(contract.Type.Name, ex);
            return false;
        }
    }

    private static FieldInfo Field(Type type, string name, Type expectedType = null)
    {
        var field = type.GetField(name, Instance);
        if (field == null || (expectedType != null && field.FieldType != expectedType))
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    private sealed class Provider
    {
        internal readonly Type Type;
        internal readonly FieldInfo Controller, Owner;
        internal readonly MethodInfo Hide, Show;

        internal Provider(Type type, Type map, string controller, string owner)
        {
            Type = type;
            Controller = Field(type, controller);
            Owner = Field(type, owner);
            Hide = Callback(type, "OnHideInRaid", map);
            Show = Callback(type, "OnShowInRaid", map);
        }

        private static MethodInfo Callback(Type type, string name, Type map)
        {
            var method = type.GetMethod(name, Instance, null, new[] { map }, null);
            if (method == null || method.ReturnType != typeof(void))
                throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }
}

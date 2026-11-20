using Mono.Cecil;

internal static class PublishedMetadata
{
    internal static void Verify(string path, Action<bool, string> check)
    {
        using var dll = AssemblyDefinition.ReadAssembly(path);
        TypeDefinition Type(string name) => dll.MainModule.GetType(name) ?? throw new Exception("Released type missing: " + name);
        void Field(TypeDefinition type, string name, string fieldType) => check(type.Fields.Any(f =>
            f.Name == name && !f.IsStatic && (fieldType == null || f.FieldType.FullName == fieldType)), "Released field drift: " + type.FullName + "." + name);
        var plugin = Type("DynamicMaps.Plugin");
        var attribute = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        check((string)attribute.ConstructorArguments[0].Value == "com.mpstark.dynamicmaps", "Released GUID mismatch");
        check((string)attribute.ConstructorArguments[2].Value == "1.3.0", "Reviewed version mismatch");
        Field(plugin, "Map", "DynamicMaps.UI.ModdedMapScreen");
        var screen = Type("DynamicMaps.UI.ModdedMapScreen");
        Field(screen, "_providersActive", "System.Boolean"); Field(screen, "_providersInRaid", "System.Boolean");
        Field(screen, "_dynamicMarkerProviders", null); Field(screen, "_mapView", "DynamicMaps.UI.Components.MapView");
        var arrow = Type("DynamicMaps.DynamicMarkers.PlayerMarkerProvider");
        var markerField = arrow.Fields.Single(f => f.Name == "_playerMarker");
        var marker = Type(markerField.FieldType.FullName);
        check(marker.Properties.Any(p => p.Name == "Player" && p.SetMethod != null && p.GetMethod != null), "Released arrow no longer has Player");
        foreach (var entry in new[] {
            ("DynamicMaps.QuestMarkerProvider", "_questController", "_map"),
            ("DynamicMaps.DynamicMarkers.LockedDoorMarkerMutator", "_inventoryController", "_equipment") })
        {
            var type = Type(entry.Item1); Field(type, entry.Item2, null); Field(type, entry.Item3, null);
            foreach (var name in new[] { "OnHideInRaid", "OnShowInRaid" })
                check(type.Methods.Any(m => !m.IsStatic && m.Name == name && m.ReturnType.FullName == "System.Void" &&
                    m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "DynamicMaps.UI.Components.MapView"), "Released callback drift: " + name);
        }
    }
}

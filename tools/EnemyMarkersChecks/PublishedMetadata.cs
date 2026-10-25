using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class PublishedMetadata
{
    internal static void Verify(string path, Action<bool, string> check)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        check(assembly.Name.Name == "flir.enemymarkers" && assembly.Name.Version == new Version(1, 4, 0, 0), "published assembly identity");
        var plugin = assembly.MainModule.GetType("flir.enemymarkers.Plugin");
        var attr = plugin.CustomAttributes.Single(a => a.AttributeType.Name == "BepInPlugin");
        check((string)attr.ConstructorArguments[0].Value == "flir.enemymarkers" && (string)attr.ConstructorArguments[2].Value == "1.4.0", "published plugin identity");
        var component = assembly.MainModule.GetType("flir.enemymarkers.EmComponent");
        var billboard = component.NestedTypes.Single(t => t.Name == "Billboard");
        foreach (var (type, name, value) in new[] {
            (component, "_gameWorld", "EFT.GameWorld"),
            (component, "_markersDict", "System.Collections.Generic.Dictionary`2<EFT.IPlayer,flir.enemymarkers.PlayerInfo>"),
            (billboard, "_mainCamera", "UnityEngine.Camera"),
            (assembly.MainModule.GetType("flir.enemymarkers.PlayerInfo"), "Marker", "UnityEngine.GameObject") })
            check(type.Fields.Any(f => f.Name == name && !f.IsStatic && !f.IsInitOnly && f.FieldType.FullName == value), "published field " + name);
        foreach (var name in new[] { "OnPersonAdd", "PlayerDeadOrUnspawn" })
            check(component.Methods.Any(m => m.Name == name && !m.IsStatic && m.ReturnType.FullName == "System.Void"
                && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "EFT.IPlayer"), "published " + name);
        check(component.Methods.Any(m => m.Name == "InitializeMarkers" && !m.IsStatic && m.Parameters.Count == 0), "published initialization callback");
        var destroy = component.Methods.Single(m => m.Name == "OnDestroy");
        check(!destroy.IsStatic && destroy.Parameters.Count == 0 && destroy.ReturnType.FullName == "System.Void", "published destruction callback");
        check(destroy.Body.Instructions.Count(i => i.Operand is MethodReference m && m.Name == "get_name") == 2
            && destroy.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "remove_OnPlayerDeadOrUnspawn")
            && !destroy.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "remove_OnIPlayerDeadOrUnspawn"),
            "published destroyed-player name and incorrect-event teardown positive control");
        var late = billboard.Methods.Single(m => m.Name == "LateUpdate");
        check(late.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "set_rotation")
            && !late.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_main"), "published stale-camera positive control and upstream orientation");
        var add = component.Methods.Single(m => m.Name == "OnPersonAdd");
        check(!add.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "get_IsYourPlayer"), "published local-body defect positive control");
        check(component.Methods.Single(m => m.Name == "InitializeMarkers").Body.Instructions.Any(), "published initial scan exists");
        var hook = assembly.MainModule.GetType("NewGamePatch").Methods.Single(m => m.Name == "PatchPrefix");
        check(hook.IsStatic && hook.Parameters.Count == 0 && hook.ReturnType.FullName == "System.Void", "published world hook");
    }
}

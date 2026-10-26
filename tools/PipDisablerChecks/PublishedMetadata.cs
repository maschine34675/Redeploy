using Mono.Cecil;
using Mono.Cecil.Cil;
internal static class PublishedMetadata
{
    internal static Type MakeFuture(string path, bool broken)
    {
        using var a = AssemblyDefinition.ReadAssembly(path);
        var plugin = a.MainModule.GetType("PiPDisabler.PiPDisablerPlugin");
        var attr = plugin.CustomAttributes.Single(x => x.AttributeType.Name == "BepInPlugin");
        attr.ConstructorArguments[2] = new CustomAttributeArgument(a.MainModule.TypeSystem.String, "2.1.1");
        if (broken) a.MainModule.GetType("PiPDisabler.MeshSurgeryManager").NestedTypes.Single(t => t.Name == "CutMeshEntry")
            .Fields.Single(f => f.Name == "CutMesh").FieldType = a.MainModule.TypeSystem.Object;
        using var bytes = new MemoryStream(); a.Write(bytes);
        return System.Reflection.Assembly.Load(bytes.ToArray()).GetType("PiPDisabler.PiPDisablerPlugin", true);
    }
    internal static void Verify(string path, Action<bool, string> check)
    {
        using var a = AssemblyDefinition.ReadAssembly(path);
        check(a.Name.Name == "PiP-Disabler" && a.Name.Version == new Version(1, 0, 0, 0), "released assembly identity");
        var plugin = a.MainModule.GetType("PiPDisabler.PiPDisablerPlugin");
        var attr = plugin.CustomAttributes.Single(x => x.AttributeType.Name == "BepInPlugin");
        check((string)attr.ConstructorArguments[0].Value == "com.fiodor.pipdisabler" && (string)attr.ConstructorArguments[2].Value == "2.1.0", "released plugin identity");
        var scope = a.MainModule.GetType("PiPDisabler.ScopeLifecycle");
        var mesh = a.MainModule.GetType("PiPDisabler.MeshSurgeryManager");
        var lens = a.MainModule.GetType("PiPDisabler.LensTransparency");
        check(a.MainModule.GetType("PiPDisabler.CameraSettingsManager").Methods.Any(m => m.Name == "ForceRestore" && m.IsStatic && m.Parameters.Count == 0 && m.ReturnType.FullName == "System.Void"), "released deferred LOD restore");
        foreach (var name in new[] { "OnOpticEnabled", "OnOpticDisabled", "RestoreBypassedOpticState", "CheckAndUpdate", "Tick", "ForceExit", "ClearPostExitRestore", "UnsubscribeMovementState" })
            check(scope.Methods.Count(m => m.Name == name && m.IsStatic && m.ReturnType.FullName == "System.Void" && m.HasBody) == 1, "released scope method " + name);
        foreach (var name in new[] { "GetOrCreateCurrentWeaponCache", "ApplyForOptic", "RestoreOriginalMeshes", "DestroyCutMeshes", "UnbindInventoryEvents" })
            check(mesh.Methods.Count(m => m.Name == name && m.IsStatic && m.HasBody) == 1, "released cache method " + name);
        foreach (var (t, name, type, readOnly) in new[] {
            (scope, "_activeOptic", "EFT.CameraControl.OpticSight", false), (scope, "_lastEnabledOptic", "EFT.CameraControl.OpticSight", false),
            (scope, "_subscribedMovementContext", "EFT.MovementContext", false),
            (mesh, "_currentWeaponCache", "PiPDisabler.MeshSurgeryManager/CutProfileCache", false), (mesh, "_currentWeaponId", "System.String", false),
            (mesh, "_inventoryEventSource", "System.Object", false),
            (mesh, "_raidCaches", "System.Collections.Generic.Dictionary`2<System.String,PiPDisabler.MeshSurgeryManager/RaidWeaponCache>", true),
            (lens, "_hidden", "System.Collections.Generic.List`1<PiPDisabler.LensTransparency/HiddenEntry>", true),
            (a.MainModule.GetType("PiPDisabler.ReticleRenderer"), "_opticTransform", "UnityEngine.Transform", false) })
            check(t.Fields.Any(f => f.Name == name && f.FieldType.FullName == type && f.IsStatic && f.IsInitOnly == readOnly), "released field " + name);
        foreach (var (owner, name, type, readOnly) in new[] {
            ("CutProfileCache", "WeaponRoot", "UnityEngine.GameObject", false),
            ("CutProfileCache", "Entries", "System.Collections.Generic.List`1<PiPDisabler.MeshSurgeryManager/CutMeshEntry>", true),
            ("RaidWeaponCache", "Profiles", "System.Collections.Generic.Dictionary`2<System.String,PiPDisabler.MeshSurgeryManager/CutProfileCache>", true),
            ("CutMeshEntry", "Filter", "UnityEngine.MeshFilter", false), ("CutMeshEntry", "OriginalMesh", "UnityEngine.Mesh", false),
            ("CutMeshEntry", "CutMesh", "UnityEngine.Mesh", false), ("CutMeshEntry", "Applied", "System.Boolean", false) })
            check(mesh.NestedTypes.Single(t => t.Name == owner).Fields.Any(f => f.Name == name && f.FieldType.FullName == type && !f.IsStatic && f.IsInitOnly == readOnly), "released nested field " + owner + "." + name);
        var disable = scope.Methods.Single(m => m.Name == "OnOpticDisabled");
        check(disable.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "ReticleRenderer" && m.Name == "Hide"), "positive control: any optic disable hides reticle");
        var enabled = a.MainModule.GetType("PiPDisabler.Patches.OpticSightOnEnablePatch").Methods.Single(m => m.Name == "Postfix");
        check(enabled.Parameters.Count == 1 && enabled.Parameters.All(p => p.Name != "__runOriginal"), "positive control: upstream ignores skipped original");
        check(mesh.Methods.Single(m => m.Name == "CleanupForShutdown").Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "DestroyCurrentWeaponCache"), "positive control: foreign cache lifetime tied to plugin shutdown");
    }
}

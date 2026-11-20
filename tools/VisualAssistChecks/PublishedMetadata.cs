using Mono.Cecil;

internal static class PublishedMetadata
{
    internal static void Verify(string path, Action<bool, string> check)
    {
        check(File.Exists(path), "released VisualAssist DLL missing: pass its path as the sole argument");
        using var dll = AssemblyDefinition.ReadAssembly(path);
        var plugin = dll.MainModule.GetType("VisualAssist.Plugin");
        var arc = dll.MainModule.GetType("VisualAssist.GrenadeArc");
        var metadata = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        check((string)metadata.ConstructorArguments[0].Value == "com.janky.visualassist", "released GUID drifted");
        check((string)metadata.ConstructorArguments[2].Value == "1.2.0", "released semantic version drifted");
        check(dll.Name.Version == new Version(1, 2, 0, 23951), "reviewed assembly identity drifted");
        foreach (var name in new[] { "localPlayer", "_line", "_sphere", "_sphereRenderer", "GrenadeThrow" })
            check(arc.Fields.Any(f => f.Name == name && !f.IsStatic), "released field missing: " + name);
        var destroy = arc.Methods.Single(m => m.Name == "OnDestroy");
        check(!destroy.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "UnityEngine.Object"
            && m.Name.StartsWith("Destroy")), "release fixed teardown: re-review the adapter");
        foreach (var name in new[] { "GrenadeAssistPlayerOnDeadPrefixPatch", "GrenadeAssistPlayerDisposePrefixPatch" })
        {
            var hook = dll.MainModule.GetType("VisualAssist." + name).Methods.Single(m => m.Name == "Postfix");
            check(hook.Body.Instructions.Any(i => i.Operand is MethodReference m && m.Name == "Release"
                && m.DeclaringType.FullName.StartsWith("Comfort.Common.Singleton")), "released unconditional singleton release changed");
        }
        check(!plugin.CustomAttributes.Any(a => a.AttributeType.Name == "BepInDependency"), "new hard dependency requires review");
    }
}

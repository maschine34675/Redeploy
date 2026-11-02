using System.Reflection;
using Mono.Cecil;

internal static class PublishedMetadata
{
    internal static void Verify(string path, object contract, string pluginName, string guid, string version,
        string assemblyVersion, Action<bool, string> check)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        check(assembly.Name.Version.ToString() == assemblyVersion, "released assembly version " + assembly.Name.Name);
        var plugin = assembly.MainModule.GetType(pluginName);
        var attribute = plugin.CustomAttributes.Single(a => a.AttributeType.Name == "BepInPlugin");
        check((string)attribute.ConstructorArguments[0].Value == guid && (string)attribute.ConstructorArguments[2].Value == version,
            "released plugin metadata " + guid);
        var fields = contract.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(FieldInfo)).Select(f => (FieldInfo)f.GetValue(contract)).ToArray();
        foreach (var expected in fields)
        {
            var field = assembly.MainModule.GetType(expected.DeclaringType.FullName).Fields.Single(f => f.Name == expected.Name);
            check(Matches(field, expected),
                "released field " + expected.DeclaringType.Name + "." + expected.Name);
            field.IsStatic = !field.IsStatic;
            check(!Matches(field, expected), "released field static drift detected");
            field.IsStatic = !field.IsStatic;
            field.IsInitOnly = true;
            check(!Matches(field, expected), "released readonly field drift detected");
            field.IsInitOnly = false;
        }
        foreach (var expected in contract.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                     .Where(f => f.FieldType == typeof(MethodInfo)).Select(f => (MethodInfo)f.GetValue(contract)))
        {
            var method = assembly.MainModule.GetType(expected.DeclaringType.FullName).Methods.Single(m => m.Name == expected.Name);
            check(Matches(method, expected),
                "released method " + expected.Name);
            method.IsStatic = !method.IsStatic;
            check(!Matches(method, expected), "released method static drift detected");
            method.IsStatic = !method.IsStatic;
            method.ReturnType = assembly.MainModule.TypeSystem.String;
            check(!Matches(method, expected), "released method return drift detected");
        }
    }
    private static bool Matches(FieldDefinition field, FieldInfo expected)
        => field.IsStatic == expected.IsStatic && !field.IsInitOnly && field.FieldType.FullName == expected.FieldType.FullName;
    private static bool Matches(MethodDefinition method, MethodInfo expected)
        => method.IsStatic == expected.IsStatic && method.HasBody && !method.HasGenericParameters
            && method.ReturnType.FullName == expected.ReturnType.FullName
            && method.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(expected.GetParameters().Select(p => p.ParameterType.FullName));
}

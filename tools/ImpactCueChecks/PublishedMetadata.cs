using System.Reflection;
using Mono.Cecil;
using Redeploy.Core.Compat;

internal static class PublishedMetadata
{
    internal static void Verify(string path, Action<bool, string> check)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        check(assembly.Name.Name == "ImpactCue-UI-MOD" && assembly.Name.Version == new Version(2, 0, 3, 0), "shipped assembly identity 2.0.3");
        var plugin = assembly.MainModule.GetType("Dizzlow.ImpactCue.Plugin");
        var attr = plugin.CustomAttributes.Single(a => a.AttributeType.Name == "BepInPlugin");
        check((string)attr.ConstructorArguments[0].Value == ImpactCueContract.PluginGuid
            && (string)attr.ConstructorArguments[2].Value == "2.0.3", "shipped plugin metadata");
        var contract = ImpactCueContract.Resolve(typeof(Dizzlow.ImpactCue.Plugin), ImpactCueContract.PluginGuid, new Version(2, 0, 3), out _);
        var fields = typeof(ImpactCueContract).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(FieldInfo)).Select(f => (FieldInfo)f.GetValue(contract)).Concat(contract.BodyCaches).ToArray();
        var methods = typeof(ImpactCueContract).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(MethodInfo)).Select(f => (MethodInfo)f.GetValue(contract)).ToArray();
        bool Matches(FieldInfo expected)
        {
            var f = assembly.MainModule.GetType(expected.DeclaringType.FullName)?.Fields.SingleOrDefault(f => f.Name == expected.Name);
            return f != null && f.IsStatic && !f.IsInitOnly && f.FieldType.FullName == expected.FieldType.FullName;
        }
        bool MatchesMethod(MethodInfo expected)
        {
            var candidates = assembly.MainModule.GetType(expected.DeclaringType.FullName)?.Methods.Where(m => m.Name == expected.Name
                && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(expected.GetParameters().Select(p => p.ParameterType.FullName))).ToArray();
            return candidates?.Length == 1 && candidates[0].IsStatic && candidates[0].HasBody
                && candidates[0].ReturnType.FullName == expected.ReturnType.FullName && !candidates[0].HasGenericParameters;
        }
        foreach (var f in fields) check(Matches(f), "shipped field " + f.Name);
        foreach (var m in methods) check(MatchesMethod(m), "shipped method " + m.Name);
        var changed = assembly.MainModule.GetType(fields[0].DeclaringType.FullName).Fields.Single(f => f.Name == fields[0].Name);
        changed.Name += "_broken";
        check(!Matches(fields[0]), "negative control detects missing shipped field");
        changed.Name = fields[0].Name;
        changed.IsStatic = false;
        check(!Matches(fields[0]), "negative control detects nonstatic shipped field");
        changed.IsStatic = true;
        var altered = assembly.MainModule.GetType(methods[0].DeclaringType.FullName).Methods.Single(m => m.Name == methods[0].Name);
        var result = altered.ReturnType;
        altered.ReturnType = assembly.MainModule.TypeSystem.String;
        check(!MatchesMethod(methods[0]), "negative control detects changed shipped return type");
        altered.ReturnType = result;
    }
}

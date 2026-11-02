using System.Reflection;
using Mono.Cecil;
using Redeploy.Core.Compat;

internal static class PublishedMetadata
{
    internal static void Verify(string path, object contract, string guid, string version, string assemblyVersion, Action<bool, string> check)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(path);
        check(assembly.Name.Version.ToString() == assemblyVersion, "released assembly version " + assembly.Name.Name);
        var plugin = assembly.MainModule.Types.Single(t => t.CustomAttributes.Any(a => a.AttributeType.Name == "BepInPlugin"));
        var attr = plugin.CustomAttributes.Single(a => a.AttributeType.Name == "BepInPlugin");
        check((string)attr.ConstructorArguments[0].Value == guid && (string)attr.ConstructorArguments[2].Value == version, "released BepInPlugin " + guid);
        string TypeName(Type type) => type.IsGenericType
            ? type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">" : type.FullName;
        var fields = contract.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(FieldInfo)).Select(f => (FieldInfo)f.GetValue(contract)).ToArray();
        var methods = contract.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Where(f => f.FieldType == typeof(MethodInfo)).Select(f => (MethodInfo)f.GetValue(contract)).ToArray();
        bool FieldMatches(FieldInfo expected)
        {
            var field = assembly.MainModule.GetType(expected.DeclaringType.FullName)?.Fields.SingleOrDefault(f => f.Name == expected.Name);
            return field != null && field.IsStatic == expected.IsStatic && field.IsInitOnly == expected.IsInitOnly && field.FieldType.FullName == TypeName(expected.FieldType);
        }
        bool MethodMatches(MethodInfo expected)
        {
            var methods = assembly.MainModule.GetType(expected.DeclaringType.FullName)?.Methods.Where(m => m.Name == expected.Name
                && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(expected.GetParameters().Select(p => TypeName(p.ParameterType)))).ToArray();
            return methods?.Length == 1 && methods[0].IsStatic == expected.IsStatic && methods[0].HasBody
                && !methods[0].HasGenericParameters && methods[0].ReturnType.FullName == TypeName(expected.ReturnType);
        }
        foreach (var field in fields) check(FieldMatches(field), "published field " + field.Name);
        foreach (var method in methods) check(MethodMatches(method), "published method " + method.DeclaringType.FullName + "." + method.Name);
        var schema = ((RaidReviewContract)contract).Schema;
        var schemaType = typeof(RaidReviewPacketSchema);
        var shapes = (System.Collections.IDictionary)schemaType.GetField("_shapes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(schema);
        var properties = schemaType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Where(f => f.FieldType == typeof(PropertyInfo))
            .Select(f => (PropertyInfo)f.GetValue(schema)).ToList();
        foreach (var shape in shapes.Values)
            properties.AddRange(shape.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Where(f => f.FieldType == typeof(PropertyInfo))
                .Select(f => (PropertyInfo)f.GetValue(shape)).Where(p => p != null));
        foreach (var property in properties.Distinct())
        {
            var actual = assembly.MainModule.GetType(property.DeclaringType.FullName).Properties.SingleOrDefault(p => p.Name == property.Name);
            check(actual != null && actual.PropertyType.FullName == TypeName(property.PropertyType) && actual.GetMethod?.IsStatic == false
                && actual.SetMethod?.IsStatic == false, "released packet property " + property.DeclaringType.Name + "." + property.Name);
        }
        var releasedStatus = assembly.MainModule.GetType("RAID_REVIEW.PlayerStatus");
        foreach (var name in Enum.GetNames(typeof(RAID_REVIEW.PlayerStatus)))
            check(Convert.ToInt32(releasedStatus.Fields.Single(f => f.Name == name).Constant) == (int)Enum.Parse(typeof(RAID_REVIEW.PlayerStatus), name), "released player status " + name);
        var mutant = assembly.MainModule.GetType(fields[0].DeclaringType.FullName).Fields.Single(f => f.Name == fields[0].Name);
        mutant.IsStatic = !mutant.IsStatic;
        check(!FieldMatches(fields[0]), "field-static contract mutant detected");
        mutant.IsStatic = !mutant.IsStatic;
        var changed = assembly.MainModule.GetType(methods[0].DeclaringType.FullName).Methods.Single(m => m.Name == methods[0].Name);
        changed.ReturnType = assembly.MainModule.TypeSystem.String;
        check(!MethodMatches(methods[0]), "method return contract mutant detected");
    }
}

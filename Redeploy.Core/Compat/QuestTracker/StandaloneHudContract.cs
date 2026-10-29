using System;
using System.Reflection;
using BepInEx;

namespace Redeploy.Core.Compat;

internal static class StandaloneHudContract
{
    internal static Version Normalize(Version version) => version == null || version.Build < 0 ? null
        : new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));

    internal static Assembly Plugin(Type plugin, string guid, string expectedGuid, Version reported,
        Version reviewed, string typeName, string assemblyName, Version reviewedAssembly)
    {
        var version = Normalize(reported);
        if (guid != expectedGuid || version == null || version < reviewed)
            throw new NotSupportedException(typeName + " requires " + reviewed + " or newer");
        if (plugin == null || plugin.FullName != typeName || plugin.IsAbstract || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin))
            throw new MissingMemberException(typeName + " plugin identity changed");
        var attribute = plugin.GetCustomAttribute<BepInPlugin>(false);
        var assembly = plugin.Assembly;
        if (attribute?.GUID != guid || Normalize(attribute.Version) != version || assembly.GetName().Name != assemblyName
            || Normalize(assembly.GetName().Version) < reviewedAssembly
            || (version == reviewed && Normalize(assembly.GetName().Version) != reviewedAssembly))
            throw new InvalidOperationException(typeName + " plugin/assembly metadata disagree");
        return assembly;
    }

    internal static FieldInfo Field(Type owner, string name, Type type, bool isStatic = false)
    {
        var field = owner.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
            | (isStatic ? BindingFlags.Static : BindingFlags.Instance));
        if (field == null || field.IsStatic != isStatic || field.IsInitOnly || field.FieldType != type)
            throw new MissingFieldException(owner.FullName, name);
        return field;
    }

    internal static MethodInfo Method(Type owner, string name, Type result, bool isStatic, params Type[] args)
    {
        var method = owner.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
            | (isStatic ? BindingFlags.Static : BindingFlags.Instance), null, args, null);
        if (method == null || method.IsStatic != isStatic || method.ReturnType != result
            || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(owner.FullName, name);
        return method;
    }
}

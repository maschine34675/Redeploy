using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using Comfort.Common;
using EFT;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>The private shared human-list contract in ORBIT 3.1.1.</summary>
internal sealed class OrbitContract
{
    internal const string PluginGuid = "com.chazut.orbit";
    internal static readonly Version ReviewedVersion = new(3, 1, 1, 0);
    internal PropertyInfo ManagerInstance;
    internal FieldInfo Bots, Movement, Waypoints, MovementHumans, WaypointHumans, Stuck, HardStuck, HardHumans;

    internal static OrbitContract Resolve(Type plugin, string guid, Version reported)
    {
        var version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("ORBIT 3.1.1 or newer is required");
        if (plugin == null || plugin.FullName != "Orbit.Plugin" || plugin.IsAbstract
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin))
            throw new MissingMemberException("ORBIT plugin identity changed");
        var assembly = plugin.Assembly;
        var metadata = plugin.GetCustomAttribute<BepInPlugin>(false);
        var assemblyVersion = assembly.GetName().Version;
        // ORBIT intentionally stamps AssemblyVersion as OrbitVersion + ".*".
        if (assembly.GetName().Name != "ORBIT" || assemblyVersion == null
            || assemblyVersion.Major != version.Major || assemblyVersion.Minor != version.Minor
            || assemblyVersion.Build != version.Build || metadata?.GUID != guid || Normalize(metadata.Version) != version)
            throw new InvalidOperationException("ORBIT assembly/plugin metadata disagree");
        var manager = Class(assembly, "Orbit.Core.OrbitManager");
        var movement = Class(assembly, "Orbit.Systems.MovementSystem");
        var waypoints = Class(assembly, "Orbit.Systems.WaypointSystem");
        var stuck = Class(assembly, "Orbit.Systems.MovementSystem+StuckRemediation");
        var hard = Class(assembly, "Orbit.Systems.MovementSystem+HardStuckRemediation");
        var singleton = typeof(Singleton<>).MakeGenericType(manager);
        var instance = singleton.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        if (instance?.GetGetMethod() == null || instance.PropertyType != manager || instance.GetIndexParameters().Length != 0)
            throw new MissingMemberException("ORBIT manager singleton changed");
        return new OrbitContract
        {
            ManagerInstance = instance,
            Bots = Field(manager, "_botsController", typeof(BotsController), true),
            Movement = Field(manager, "MovementSystem", movement, true),
            Waypoints = Field(manager, "WaypointSystem", waypoints, true),
            MovementHumans = Field(movement, "_humanPlayers", typeof(List<Player>), true),
            WaypointHumans = Field(waypoints, "_humanPlayers", typeof(List<Player>), true),
            Stuck = Field(movement, "_stuckRemediation", stuck, true),
            HardStuck = Field(stuck, "_hardRemediation", hard, true),
            HardHumans = Field(hard, "<humanPlayers>P", typeof(List<Player>), false)
        };
    }

    private static Type Class(Assembly assembly, string name)
    {
        var type = assembly.GetType(name, true);
        if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters)
            throw new MissingMemberException(name);
        return type;
    }

    private static FieldInfo Field(Type type, string name, Type value, bool readOnly)
    {
        var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (field == null || field.IsStatic || field.IsInitOnly != readOnly || field.FieldType != value)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }

    internal static Version Normalize(Version version) => version == null || version.Build < 0 ? null
        : new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
}

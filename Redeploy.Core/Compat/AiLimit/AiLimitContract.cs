using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using EFT;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>AI Limit 1.9.2 caches its human players once in Start/GetPlayers.</summary>
internal sealed class AiLimitContract
{
    internal const string PluginGuid = "com.dvize.ailimit";
    internal static readonly Version ReviewedVersion = new(1, 9, 2, 0);
    internal Type ComponentType;
    internal FieldInfo Players, World;

    internal static AiLimitContract Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("AI Limit 1.9.2 or newer is required");
        if (plugin == null || plugin.FullName != "AIlimit.AILimitPlugin" || plugin.IsAbstract
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin))
            throw new MissingMemberException("AI Limit plugin identity changed");
        var assembly = plugin.Assembly;
        var metadata = plugin.GetCustomAttribute<BepInPlugin>(false);
        if (assembly.GetName().Name != "dvize.AILimit" || Normalize(assembly.GetName().Version) != version
            || metadata?.GUID != guid || Normalize(metadata.Version) != version)
            throw new InvalidOperationException("AI Limit assembly/plugin metadata disagree");
        var component = assembly.GetType("AILimit.AILimitComponent", true);
        if (component.IsAbstract || !typeof(MonoBehaviour).IsAssignableFrom(component))
            throw new MissingMemberException("AI Limit component identity changed");
        var contract = new AiLimitContract
        {
            ComponentType = component,
            Players = Field(component, "players", false, typeof(List<Player>)),
            World = Field(component, "gameWorld", true, typeof(GameWorld))
        };
        Method(component, "Start", false, typeof(void));
        Method(component, "GetPlayers", false, typeof(void));
        Method(component, "getMinDistanceToBot", false, typeof(float), typeof(Player));
        Method(component, "Enable", true, typeof(void));
        return contract;
    }

    private static FieldInfo Field(Type type, string name, bool isStatic, Type value)
    {
        var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static
            | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (field == null || field.IsStatic != isStatic || field.FieldType != value || field.IsInitOnly)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }
    private static void Method(Type type, string name, bool isStatic, Type result, params Type[] args)
    {
        var method = type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static
            | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.IsStatic != isStatic || method.ReturnType != result
            || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
    }
    internal static Version Normalize(Version version) => version == null || version.Build < 0 ? null
        : new Version(version.Major, version.Minor, version.Build, Math.Max(0, version.Revision));
}

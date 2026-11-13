using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal sealed class DynamicExternalResolutionContract
{
    internal const string Guid = "com.Shibatsu.DynamicExternalResolution";
    internal static readonly Version Reviewed = new(1, 1, 2, 0);
    internal FieldInfo Player;
    internal MethodInfo Getter;
    internal MethodInfo[] Callbacks, Native;

    internal static DynamicExternalResolutionContract Resolve(Type plugin, string guid, Version version)
    {
        var metadata = plugin?.GetCustomAttribute<BepInPlugin>();
        if (Normalize(version) == null || Normalize(version) < Reviewed)
            throw new NotSupportedException("DERP 1.1.2 or newer is required");
        // Published 1.1.2 still has AssemblyVersion 4.0.0.0. Its BepInPlugin
        // version is authoritative; the source tag accidentally says 1.1.1.
        if (guid != Guid || metadata?.GUID != Guid || Normalize(metadata.Version) != Normalize(version)
            || plugin.FullName != "DynamicExternalResolution.DynamicExternalResolution"
            || plugin.Assembly.GetName().Name != "DynamicExternalResolution"
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin))
            throw new InvalidOperationException("DERP plugin identity changed");
        var field = plugin.GetField("_localPlayer", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (field == null || field.FieldType != typeof(Player) || field.IsInitOnly)
            throw new MissingFieldException("DERP local-player cache changed");
        string prefix = "DynamicExternalResolution.DynamicExternalResolutionPatches+";
        var result = new DynamicExternalResolutionContract
        {
            Player = field,
            Getter = Method(plugin, "getPlayerInstance", true, typeof(Player)),
            Callbacks = new[]
            {
                Method(plugin.Assembly.GetType(prefix + "OpticSightOnEnablePath", true), "PatchPostfix", true, typeof(void)),
                Method(plugin.Assembly.GetType(prefix + "OpticSightOnDisablePath", true), "PatchPostfix", true, typeof(void)),
                Method(plugin.Assembly.GetType(prefix + "ClientFirearmControllerChangeAimingModePath", true), "PatchPostfix", true, typeof(void))
            },
            Native = new[]
            {
                Method(typeof(OpticSight), "OnEnable", false, typeof(void)),
                Method(typeof(OpticSight), "OnDisable", false, typeof(void)),
                Method(typeof(Player.FirearmController), "ChangeAimingMode", false, typeof(void))
            }
        };
        result.Verify();
        return result;
    }

    internal void Verify()
    {
        for (int i = 0; i < Native.Length; i++)
            if (Harmony.GetPatchInfo(Native[i])?.Postfixes.Count(p => p.PatchMethod == Callbacks[i]) != 1)
                throw new MissingMethodException("DERP native callback is missing: " + Native[i].Name);
    }

    private static MethodInfo Method(Type type, string name, bool stat, Type returns)
    {
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
            | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
        if (method == null || method.IsStatic != stat || method.ReturnType != returns
            || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }
    internal static Version Normalize(Version value) => value == null || value.Build < 0 ? null
        : new Version(value.Major, value.Minor, value.Build, Math.Max(0, value.Revision));
}

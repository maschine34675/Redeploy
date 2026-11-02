using System;
using System.Reflection;
using EFT;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal sealed class HollywoodFxContract
{
    internal const string PluginGuid = "com.janky.hollywoodfx";
    internal static readonly Version ReviewedVersion = new(3, 0, 0, 0);
    internal FieldInfo LocalPlayer;

    internal static HollywoodFxContract Resolve(Type plugin, string guid, Version version)
    {
        var assembly = StandaloneHudContract.Plugin(plugin, guid, PluginGuid, version, ReviewedVersion,
            "HollywoodFX.Plugin", "HollywoodFX", new Version(3, 0, 0, 37095));
        var state = assembly.GetType("HollywoodFX.ImpactStatic", true);
        return new HollywoodFxContract
        {
            LocalPlayer = StandaloneHudContract.Field(state, "LocalPlayer", typeof(Player), true)
        };
    }
}

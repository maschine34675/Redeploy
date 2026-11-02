using System;
using System.Reflection;
using EFT;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal sealed class HandsAreNotBusyContract
{
    internal const string PluginGuid = "com.lacyway.hanb";
    internal static readonly Version ReviewedVersion = new(1, 7, 1, 0);
    internal Type Component;
    internal FieldInfo Player;
    internal MethodInfo FixHands;

    internal static HandsAreNotBusyContract Resolve(Type plugin, string guid, Version version)
    {
        // The released 1.7.1 plugin still has assembly 1.0.0.0. The source tag's older
        // plugin literal is not the metadata of the shipped DLL.
        var assembly = StandaloneHudContract.Plugin(plugin, guid, PluginGuid, version, ReviewedVersion,
            "HandsAreNotBusy.HANB_Plugin", "HandsAreNotBusy", new Version(1, 0, 0, 0));
        var component = assembly.GetType("HandsAreNotBusy.HANB_Component", true);
        if (!typeof(MonoBehaviour).IsAssignableFrom(component))
            throw new MissingMemberException("Hands Are Not Busy component inheritance changed");
        return new HandsAreNotBusyContract
        {
            Component = component,
            Player = StandaloneHudContract.Field(component, "_player", typeof(LocalPlayer)),
            FixHands = StandaloneHudContract.Method(component, "FixHandsController", typeof(void), false, typeof(Player))
        };
    }
}

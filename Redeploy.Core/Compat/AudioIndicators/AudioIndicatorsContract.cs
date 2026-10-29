using System;
using System.Reflection;
using EFT;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal sealed class AudioIndicatorsContract
{
    internal const string PluginGuid = "com.acidphantasm.accessibilityindicators";
    internal static readonly Version ReviewedVersion = new(2, 1, 0, 0);
    internal FieldInfo Hud, Rotation, Active;
    internal MethodInfo Unregister, Update, Dispose;
    internal static AudioIndicatorsContract Resolve(Type plugin, string guid, Version reported)
    {
        // The published 2.1.0 plugin deliberately still carries assembly version 1.5.0.0.
        var assembly = StandaloneHudContract.Plugin(plugin, guid, PluginGuid, reported, ReviewedVersion,
            "AccessibilityIndicators.Plugin", "acidphantasm-accessibilityindicators", new Version(1, 5, 0, 0));
        var panel = assembly.GetType("AccessibilityIndicators.IndicatorUI.Panel", true);
        var rotation = assembly.GetType("AccessibilityIndicators.Scripts.KeepNorthRotation", true);
        if (!typeof(MonoBehaviour).IsAssignableFrom(panel) || !typeof(MonoBehaviour).IsAssignableFrom(rotation))
            throw new MissingMemberException("Audio Indicators component inheritance changed");
        return new AudioIndicatorsContract
        {
            Hud = StandaloneHudContract.Field(panel, "IndicatorHUD", typeof(GameObject), true),
            Rotation = StandaloneHudContract.Field(panel, "keepNorthRotationScript", rotation, true),
            Active = StandaloneHudContract.Field(rotation, "isActuallyActive", typeof(bool)),
            Update = StandaloneHudContract.Method(rotation, "Update", typeof(void), false),
            Dispose = StandaloneHudContract.Method(panel, "Dispose", typeof(void), true),
            Unregister = StandaloneHudContract.Method(assembly.GetType("AccessibilityIndicators.Patches.GameWorldUnregisterPlayerPatch", true),
                "PatchPostFix", typeof(void), true, typeof(IPlayer))
        };
    }
}

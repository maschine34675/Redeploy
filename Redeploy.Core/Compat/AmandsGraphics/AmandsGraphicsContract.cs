using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using EFT;
using HarmonyLib;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal sealed class AmandsGraphicsContract
{
    internal const string PluginGuid = "com.Amanda.Graphics";
    internal static readonly Version Reviewed = new(1, 8, 0, 0);
    internal PropertyInfo Instance;
    internal FieldInfo Player, CameraObject, Camera, Prism, GraphicsMode, Level, Scene, Lights, Volumetrics;
    internal FieldInfo[] WorldDefaults;
    internal MethodInfo Activate, Update, Reset;
    internal MethodInfo PrismPostfix => _prismHook;
    private MethodInfo _playerInit, _playerHook, _prismEnable, _prismHook;

    internal static AmandsGraphicsContract Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        var metadata = plugin?.GetCustomAttribute<BepInPlugin>(false);
        if (version == null || version < Reviewed) throw new NotSupportedException("Amand's Graphics 1.8.0 or newer is required");
        if (guid != PluginGuid || plugin?.FullName != "AmandsGraphics.AmandsGraphicsPlugin"
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin) || plugin.Assembly.GetName().Name != "AmandsGraphics"
            || metadata?.GUID != guid || Normalize(metadata.Version) != version
            || Normalize(plugin.Assembly.GetName().Version) != version)
            throw new InvalidOperationException("Amand's Graphics plugin/assembly identity changed");
        var type = plugin.Assembly.GetType("AmandsGraphics.AmandsGraphicsClass", true);
        if (type.BaseType != typeof(MonoBehaviour)) throw new MissingMemberException("Amand graphics component changed");
        var instance = plugin.GetProperty("AmandsGraphicsClass", BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);
        if (instance?.PropertyType != type || instance.GetGetMethod() == null || !instance.GetGetMethod().IsStatic)
            throw new MissingMemberException("Amand graphics instance changed");
        var result = new AmandsGraphicsContract
        {
            Instance = instance,
            Player = Field(type, "Player", typeof(Player)),
            CameraObject = Field(type, "FPSCamera", typeof(GameObject)),
            Camera = Field(type, "FPSCameraCamera", typeof(Camera)),
            Prism = Field(type, "FPSCameraPrismEffects", typeof(PrismEffects)),
            GraphicsMode = Field(type, "GraphicsMode", typeof(bool), false),
            Level = Field(type, "levelSettings", typeof(LevelSettings)),
            Scene = Field(type, "scene", typeof(string)),
            Lights = Field(type, "registeredLights", typeof(Dictionary<Light, float>)),
            Volumetrics = Field(type, "registeredVolumetricLights", typeof(Dictionary<VolumetricLight, float>)),
            Activate = Method(type, "ActivateAmandsGraphics", false, typeof(GameObject), typeof(PrismEffects)),
            Update = Method(type, "UpdateAmandsGraphics", false),
            Reset = Method(type, "ResetGraphics", false),
            _playerInit = typeof(Player).GetMethod(nameof(EFT.Player.Init)),
            _playerHook = Method(plugin.Assembly.GetType("AmandsGraphics.Patches.AmandsPlayerPatch", true), "PatchPostFix", true, typeof(Player).MakeByRefType()),
            _prismEnable = Method(typeof(PrismEffects), nameof(PrismEffects.OnEnable), false),
            _prismHook = Method(plugin.Assembly.GetType("AmandsGraphics.Patches.AmandsGraphicsPrismEffectsPatch", true), "PatchPostFix", true, typeof(PrismEffects).MakeByRefType()),
            // Camera/component defaults are freshly read from the new vanilla camera.
            // Only persistent scene/weather baselines must survive same-world activation.
            WorldDefaults = new[]
            {
                Field(type, "defaultLightsUseLinearIntensity", typeof(bool)),
                Field(type, "defaultZeroLevel", typeof(float)),
                Field(type, "defaultSkyColor", typeof(Color)), Field(type, "defaultEquatorColor", typeof(Color)),
                Field(type, "defaultGroundColor", typeof(Color)), Field(type, "defaultNightVisionSkyColor", typeof(Color)),
                Field(type, "defaultNightVisionEquatorColor", typeof(Color)), Field(type, "defaultNightVisionGroundColor", typeof(Color)),
                Field(type, "defaultGradientColorKeys", typeof(GradientColorKey[])),
                Field(type, "defaultAmbientBrightness", typeof(AnimationCurve)), Field(type, "defaultAmbientContrast", typeof(AnimationCurve)),
                Field(type, "defaultLightIntensity", typeof(float)), Field(type, "defaultSunMeshBrightness", typeof(float)),
                Field(type, "defaultScatteringBrightnessMultiplier", typeof(float))
            }
        };
        result.Verify();
        return result;
    }

    internal void Verify()
    {
        if (_playerInit == null || Harmony.GetPatchInfo(_playerInit)?.Postfixes.Count(p => p.PatchMethod == _playerHook) != 1
            || Harmony.GetPatchInfo(_prismEnable)?.Postfixes.Count(p => p.PatchMethod == _prismHook) != 1)
            throw new MissingMethodException("Amand's Graphics native player/camera hooks are missing");
    }
    internal object[] CaptureDefaults() => WorldDefaults.Select(field => field.GetValue(null)).ToArray();
    internal void RestoreDefaults(object[] values)
    {
        for (int i = 0; i < WorldDefaults.Length; i++) WorldDefaults[i].SetValue(null, values[i]);
    }
    private static FieldInfo Field(Type type, string name, Type expected, bool stat = true)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (field == null || field.IsStatic != stat || field.IsInitOnly || field.FieldType != expected)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }
    private static MethodInfo Method(Type type, string name, bool stat, params Type[] parameters)
    {
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, parameters, null);
        if (method == null || method.IsStatic != stat || method.ReturnType != typeof(void) || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }
    internal static Version Normalize(Version value) => value == null || value.Build < 0 ? null
        : new Version(value.Major, value.Minor, value.Build, Math.Max(0, value.Revision));
}

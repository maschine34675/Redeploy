using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using EFT;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal sealed class MagazineCheckContract
{
    internal const string PluginGuid = "com.ozen.magcheckinterrupt";
    internal static readonly Version ReviewedVersion = new(1, 1, 1, 0);
    internal Type PanelType;
    internal FieldInfo Instance, Camera, Magazine, Finish;
    internal MethodInfo Create, Show, Hide, LateUpdate, AnimationFinished, ShowUtility;
    private MethodInfo _nativeCheck, _foreignCheck;

    internal static MagazineCheckContract Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("Magazine Check Interrupt 1.1.1 or newer is required");
        if (plugin == null || plugin.FullName != "MagCheckInterrupt.MagCheckInterrupt" || plugin.IsAbstract
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin))
            throw new MissingMemberException("Magazine Check Interrupt plugin identity changed");
        var assembly = plugin.Assembly;
        var attribute = plugin.GetCustomAttribute<BepInPlugin>(false);
        if (assembly.GetName().Name != "ozen.MagCheckInterrupt" || Normalize(assembly.GetName().Version) != version
            || attribute?.GUID != guid || Normalize(attribute.Version) != version)
            throw new InvalidOperationException("Magazine Check Interrupt assembly/plugin metadata disagree");
        var type = assembly.GetType("MagCheckInterrupt.Components.FloatingPanelController", true);
        if (type.IsAbstract || type.BaseType != typeof(MonoBehaviour))
            throw new MissingMemberException("Magazine Check Interrupt floating panel type changed");
        var contract = new MagazineCheckContract
        {
            PanelType = type,
            Instance = Field(type, "<Instance>k__BackingField", true, type),
            Camera = Field(type, "_cameraTransform", false, typeof(Transform)),
            Magazine = Field(type, "_magazineTransform", false, typeof(Transform)),
            Finish = Field(type, "_onFinish", false, typeof(Action<Task>)),
            Create = Method(type, "Create", true, type),
            Show = Method(type, "ShowAmmoDetails", false, typeof(void), typeof(Player.FirearmController)),
            Hide = Method(type, "Hide", false, typeof(void)),
            LateUpdate = Method(type, "LateUpdate", false, typeof(void)),
            AnimationFinished = Method(type, "AnimationFinished", false, typeof(void), typeof(Task)),
            ShowUtility = Method(assembly.GetType("MagCheckInterrupt.Utils.AnimationUtil", true), "ShowAmmoDetails", true,
                typeof(void), typeof(Player.FirearmController)),
            _nativeCheck = typeof(Player.FirearmController.Idling).GetMethod("RunUtilityOperation",
                new[] { typeof(Player.FirearmController.UtilityOperation.EUtilityType) }),
            _foreignCheck = Method(assembly.GetType("MagCheckInterrupt.Patches.RunUtilityOpPatch", true), "Prefix", true,
                typeof(bool), typeof(Player.FirearmController.Idling), typeof(Player.FirearmController.UtilityOperation.EUtilityType))
        };
        Field(type, "_panel", false, typeof(AmmoCountPanel));
        Field(type, "_panelTransform", false, typeof(Transform));
        contract.Verify();
        return contract;
    }
    internal void Verify()
    {
        var patches = _nativeCheck == null ? null : Harmony.GetPatchInfo(_nativeCheck);
        if (patches?.Prefixes.Count(p => p.PatchMethod == _foreignCheck) != 1)
            throw new MissingMethodException("Magazine Check Interrupt's native check hook is missing");
    }
    private static FieldInfo Field(Type type, string name, bool stat, Type value)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (field == null || field.IsStatic != stat || field.IsInitOnly || field.FieldType != value)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }
    private static MethodInfo Method(Type type, string name, bool stat, Type result, params Type[] args)
    {
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.IsStatic != stat || method.ReturnType != result || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }
    internal static Version Normalize(Version value) => value == null || value.Build < 0 ? null
        : new Version(value.Major, value.Minor, value.Build, Math.Max(0, value.Revision));
}

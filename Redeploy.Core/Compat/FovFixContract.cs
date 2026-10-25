using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using EFT;
using EFT.Animations;
using EFT.InventoryLogic;
using HarmonyLib;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal sealed class FovFixContract
{
    internal const string PluginGuid = "com.fontaine.fovfix";
    internal static readonly Version ReviewedVersion = new(4, 1, 0, 0);
    internal PropertyInfo Controller, Weapon, WeaponId, Pistol, ToggleZoom, ScopeFov;
    internal FieldInfo PlayerField, WasAiming, PwaHands;
    internal FieldInfo[] Watchers, Values;
    internal MethodInfo Update, ChangeFov, PwaPostfix, ScalePrefix;

    internal static FovFixContract Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("Fontaine FOV Fix 4.1.0 or newer is required");
        if (plugin == null || plugin.FullName != "FOVFix.Plugin" || plugin.IsAbstract
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin))
            throw new MissingMemberException("Fontaine FOV Fix plugin identity changed");
        var asm = plugin.Assembly;
        var attr = plugin.GetCustomAttribute<BepInPlugin>(false);
        // Upstream 4.1.0 intentionally uses assembly version 1.0.0.0.
        if (asm.GetName().Name != "FOVFix" || attr?.GUID != guid || Normalize(attr.Version) != version)
            throw new InvalidOperationException("Fontaine FOV Fix plugin metadata disagrees");
        var type = asm.GetType("FOVFix.FovController", true);
        var c = new FovFixContract
        {
            Controller = Property(plugin, "FovController", true, type),
            PlayerField = Field(type, "_player", false, typeof(Player)),
            WasAiming = Field(type, "_wasAiming", false, typeof(bool)),
            Weapon = Property(type, "CurrentWeapon", false, typeof(Weapon)),
            WeaponId = Property(type, "WeapId", false, typeof(string)),
            Pistol = Property(type, "IsPistol", false, typeof(bool)),
            ToggleZoom = Property(type, "IsToggleZoom", false, typeof(bool)),
            ScopeFov = Property(type, "CurrentScopeFOV", false, typeof(float)),
            Update = Method(type, "ControllerUpdate", false, typeof(void)),
            ChangeFov = Method(type, "ChangeMainCamFOV", false, typeof(void)),
            PwaPostfix = Method(asm.GetType("FOVFix.PwaWeaponParamsPatch", true), "PatchPostfix", true,
                typeof(void), typeof(ProceduralWeaponAnimation).MakeByRefType()),
            ScalePrefix = Method(asm.GetType("FOVFix.CalculateScaleValueByFovPatch", true), "Prefix", true,
                typeof(bool), typeof(Player)),
            PwaHands = Field(typeof(ProceduralWeaponAnimation), "_firearmController", false, typeof(Player.FirearmController))
        };
        var watcher = asm.GetType("FOVFix.ValueWatcher`1", true);
        var names = new[] { "ADSWatcher", "ScopeFOVWatcher", "ToggleZoomWatcher", "OpticWatcher" };
        var values = new[] { typeof(bool), typeof(float), typeof(bool), typeof(bool) };
        c.Watchers = names.Select((name, i) => Field(type, name, false, watcher.MakeGenericType(values[i]))).ToArray();
        c.Values = values.Select(value => Field(watcher.MakeGenericType(value), "_watchedValue", false, value)).ToArray();
        c.Verify();
        return c;
    }

    internal void Verify()
    {
        Receipt(typeof(ProceduralWeaponAnimation).GetMethod("OnAimOrPoseChanged", BindingFlags.Public | BindingFlags.Instance), PwaPostfix, false);
        Receipt(typeof(Player).GetMethod("SetCompensationScale", new[] { typeof(bool) }), ScalePrefix, true);
        var controller = Controller.GetValue(null) ?? throw new InvalidOperationException("FOV controller has not initialized");
        foreach (var field in Watchers)
            if (field.GetValue(controller) == null) throw new InvalidOperationException("FOV watcher has not initialized: " + field.Name);
    }

    internal void Reset(object controller, Player body)
    {
        // Write watcher storage directly: firing events halfway through a handoff mixes two bodies.
        PlayerField.SetValue(controller, body);
        WasAiming.SetValue(controller, false);
        ToggleZoom.SetValue(controller, false);
        ScopeFov.SetValue(controller, 0f);
        for (int i = 0; i < Watchers.Length; i++)
            Values[i].SetValue(Watchers[i].GetValue(controller), i == 1 ? (object)0f : false);
        var weapon = body?.HandsController is Player.FirearmController hands ? hands.Weapon : null;
        Weapon.SetValue(controller, weapon);
        WeaponId.SetValue(controller, weapon?.Id.ToString());
        Pistol.SetValue(controller, weapon?.WeapClass == "pistol");
    }

    private static void Receipt(MethodInfo native, MethodInfo foreign, bool prefix)
    {
        var info = native == null ? null : Harmony.GetPatchInfo(native);
        var entries = prefix ? info?.Prefixes : info?.Postfixes;
        if (entries == null || entries.Count(p => p.PatchMethod == foreign) != 1
            || info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers).Concat(info.ILManipulators)
                .Count(p => p.PatchMethod.DeclaringType?.Assembly == foreign.DeclaringType.Assembly) != 1)
            throw new MissingMethodException("Fontaine FOV Fix native hook changed: " + native?.Name);
    }
    private static FieldInfo Field(Type type, string name, bool stat, Type value)
    {
        var f = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (f == null || f.IsStatic != stat || f.IsInitOnly || f.FieldType != value) throw new MissingFieldException(type.FullName, name);
        return f;
    }
    private static PropertyInfo Property(Type type, string name, bool stat, Type value)
    {
        var p = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (p?.PropertyType != value || p.GetGetMethod(true)?.IsStatic != stat || p.GetSetMethod(true)?.IsStatic != stat)
            throw new MissingMemberException(type.FullName, name);
        return p;
    }
    private static MethodInfo Method(Type type, string name, bool stat, Type result, params Type[] args)
    {
        var m = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
        if (m == null || m.IsStatic != stat || m.ReturnType != result || m.ContainsGenericParameters || m.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return m;
    }
    internal static Version Normalize(Version v) => v == null || v.Build < 0 ? null : new(v.Major, v.Minor, v.Build, Math.Max(0, v.Revision));
}

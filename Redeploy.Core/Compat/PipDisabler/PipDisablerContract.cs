using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using EFT;
using EFT.CameraControl;
using EFT.InventoryLogic;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal sealed class PipDisablerContract
{
    internal const string Guid = "com.fiodor.pipdisabler";
    internal static readonly Version Reviewed = new(2, 1, 0, 0);
    internal MethodInfo Enable, Disable, Bypass, Check, Tick, ForceExit, ClearRestore, ForceCameraRestore, Unsubscribe, Unbind;
    internal MethodInfo GetCache, Apply, RestoreMeshes, DestroyMeshes;
    internal FieldInfo Active, Last, Movement, ReticleOptic, Hidden, HiddenRenderer;
    internal FieldInfo Caches, Profiles, Root, Entries, Filter, Original, Cut, Applied, Current, CurrentId, Inventory;
    internal FieldInfo Lights, Spheres, LightWasActive, LightDisabled, SphereWasActive, SphereDisabled;

    internal static PipDisablerContract Resolve(Type plugin, string guid, Version version)
    {
        version = Normalize(version);
        if (guid != Guid || version == null || version < Reviewed)
            throw new NotSupportedException("PiP Disabler 2.1.0 or newer is required");
        if (plugin?.FullName != "PiPDisabler.PiPDisablerPlugin" || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin))
            throw new MissingMemberException("PiP Disabler plugin identity changed");
        var attr = plugin.GetCustomAttribute<BepInPlugin>(false);
        // The released plugin intentionally has AssemblyVersion 1.0.0.0, not 2.1.0.
        if (plugin.Assembly.GetName().Name != "PiP-Disabler" || attr?.GUID != Guid || Normalize(attr.Version) != version)
            throw new InvalidOperationException("PiP Disabler metadata disagree");
        Type T(string name) => plugin.Assembly.GetType("PiPDisabler." + name, true);
        var scope = T("ScopeLifecycle"); var mesh = T("MeshSurgeryManager");
        var profile = mesh.GetNestedType("CutProfileCache", BindingFlags.NonPublic);
        var weapon = mesh.GetNestedType("RaidWeaponCache", BindingFlags.NonPublic);
        var entry = mesh.GetNestedType("CutMeshEntry", BindingFlags.NonPublic);
        var light = mesh.GetNestedType("LightFxState", BindingFlags.NonPublic);
        var sphere = mesh.GetNestedType("SphereState", BindingFlags.NonPublic);
        var lens = T("LensTransparency"); var hidden = lens.GetNestedType("HiddenEntry", BindingFlags.NonPublic);
        if (profile == null || weapon == null || entry == null || light == null || sphere == null || hidden == null)
            throw new MissingMemberException("PiP Disabler cache types changed");
        return new PipDisablerContract
        {
            Enable = Method(scope, "OnOpticEnabled", typeof(void), typeof(OpticSight)),
            Disable = Method(scope, "OnOpticDisabled", typeof(void), typeof(OpticSight)),
            Bypass = Method(scope, "RestoreBypassedOpticState", typeof(void), typeof(OpticSight), typeof(string), typeof(bool)),
            Check = Method(scope, "CheckAndUpdate", typeof(void), typeof(string)), Tick = Method(scope, "Tick", typeof(void)),
            ForceExit = Method(scope, "ForceExit", typeof(void)), ClearRestore = Method(scope, "ClearPostExitRestore", typeof(void)),
            ForceCameraRestore = Method(T("CameraSettingsManager"), "ForceRestore", typeof(void)),
            Unsubscribe = Method(scope, "UnsubscribeMovementState", typeof(void)), Unbind = Method(mesh, "UnbindInventoryEvents", typeof(void)),
            GetCache = Method(mesh, "GetOrCreateCurrentWeaponCache", profile, typeof(Transform), typeof(Transform)),
            Apply = Method(mesh, "ApplyForOptic", typeof(void), typeof(OpticSight)),
            RestoreMeshes = Method(mesh, "RestoreOriginalMeshes", typeof(void), profile),
            DestroyMeshes = Method(mesh, "DestroyCutMeshes", typeof(void), profile),
            Active = Field(scope, "_activeOptic", typeof(OpticSight), true), Last = Field(scope, "_lastEnabledOptic", typeof(OpticSight), true),
            Movement = Field(scope, "_subscribedMovementContext", typeof(MovementContext), true),
            ReticleOptic = Field(T("ReticleRenderer"), "_opticTransform", typeof(Transform), true),
            Hidden = Field(lens, "_hidden", typeof(List<>).MakeGenericType(hidden), true, true),
            HiddenRenderer = Field(hidden, "Renderer", typeof(Renderer), false),
            Caches = Field(mesh, "_raidCaches", typeof(Dictionary<,>).MakeGenericType(typeof(string), weapon), true, true),
            Profiles = Field(weapon, "Profiles", typeof(Dictionary<,>).MakeGenericType(typeof(string), profile), false, true),
            Root = Field(profile, "WeaponRoot", typeof(GameObject), false),
            Entries = Field(profile, "Entries", typeof(List<>).MakeGenericType(entry), false, true),
            Filter = Field(entry, "Filter", typeof(MeshFilter), false), Original = Field(entry, "OriginalMesh", typeof(Mesh), false),
            Cut = Field(entry, "CutMesh", typeof(Mesh), false), Applied = Field(entry, "Applied", typeof(bool), false),
            Current = Field(mesh, "_currentWeaponCache", profile, true), CurrentId = Field(mesh, "_currentWeaponId", typeof(string), true),
            Inventory = Field(mesh, "_inventoryEventSource", typeof(object), true),
            Lights = Field(mesh, "_disabledLightFx", typeof(Dictionary<,>).MakeGenericType(typeof(GameObject), light), true, true),
            Spheres = Field(mesh, "_disabledWeaponSpheres", typeof(Dictionary<,>).MakeGenericType(typeof(GameObject), sphere), true, true),
            LightWasActive = Field(light, "WasActiveSelf", typeof(bool), false), LightDisabled = Field(light, "DisabledByUs", typeof(bool), false),
            SphereWasActive = Field(sphere, "WasActiveSelf", typeof(bool), false), SphereDisabled = Field(sphere, "DisabledByUs", typeof(bool), false)
        };
    }
    private static FieldInfo Field(Type owner, string name, Type type, bool stat, bool readOnly = false)
    {
        var value = owner.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (value == null || value.FieldType != type || value.IsStatic != stat || value.IsInitOnly != readOnly)
            throw new MissingFieldException(owner.FullName, name);
        return value;
    }
    private static MethodInfo Method(Type owner, string name, Type returns, params Type[] args)
    {
        var value = owner.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
        if (value == null || value.ReturnType != returns || value.ContainsGenericParameters || value.GetMethodBody() == null)
            throw new MissingMethodException(owner.FullName, name);
        return value;
    }
    internal static Version Normalize(Version value) => value == null || value.Build < 0 ? null
        : new Version(value.Major, value.Minor, value.Build, Math.Max(0, value.Revision));
}

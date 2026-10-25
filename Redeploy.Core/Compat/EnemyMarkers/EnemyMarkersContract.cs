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

internal sealed class EnemyMarkersContract
{
    internal const string PluginGuid = "flir.enemymarkers";
    internal static readonly Version ReviewedVersion = new(1, 4, 0, 0);
    internal Type ComponentType, BillboardType;
    internal FieldInfo World, Markers, Marker, BillboardCamera;
    internal MethodInfo PersonAdded, PersonGone, Initialize, Destroy, BillboardUpdate, Unregister;
    private MethodInfo _gameStart, _gameStartHook;

    internal static EnemyMarkersContract Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("Enemy Markers 1.4.0 or newer is required");
        if (plugin?.FullName != "flir.enemymarkers.Plugin" || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin))
            throw new MissingMemberException("Enemy Markers plugin identity changed");
        var assembly = plugin.Assembly;
        var attribute = plugin.GetCustomAttribute<BepInPlugin>(false);
        if (assembly.GetName().Name != "flir.enemymarkers" || Normalize(assembly.GetName().Version) != version
            || attribute?.GUID != guid || Normalize(attribute.Version) != version)
            throw new InvalidOperationException("Enemy Markers assembly/plugin metadata disagree");
        var component = assembly.GetType("flir.enemymarkers.EmComponent", true);
        var billboard = component.GetNestedType("Billboard", BindingFlags.NonPublic);
        var info = assembly.GetType("flir.enemymarkers.PlayerInfo", true);
        if (component.BaseType != typeof(MonoBehaviour) || billboard?.BaseType != typeof(MonoBehaviour))
            throw new MissingMemberException("Enemy Markers component inheritance changed");
        var result = new EnemyMarkersContract
        {
            ComponentType = component, BillboardType = billboard,
            World = Field(component, "_gameWorld", typeof(GameWorld)),
            Markers = Field(component, "_markersDict", typeof(Dictionary<,>).MakeGenericType(typeof(IPlayer), info)),
            Marker = Field(info, "Marker", typeof(GameObject)),
            BillboardCamera = Field(billboard, "_mainCamera", typeof(Camera)),
            PersonAdded = Method(component, "OnPersonAdd", false, typeof(IPlayer)),
            PersonGone = Method(component, "PlayerDeadOrUnspawn", false, typeof(IPlayer)),
            Initialize = Method(component, "InitializeMarkers", false),
            Destroy = Method(component, "OnDestroy", false),
            BillboardUpdate = Method(billboard, "LateUpdate", false),
            Unregister = Method(typeof(GameWorld), "UnregisterPlayer", false, typeof(IPlayer)),
            _gameStart = Method(typeof(GameWorld), "OnGameStarted", false),
            _gameStartHook = Method(assembly.GetType("NewGamePatch", true), "PatchPrefix", true)
        };
        result.Verify();
        return result;
    }
    internal void Verify()
    {
        if (Harmony.GetPatchInfo(_gameStart)?.Prefixes.Count(p => p.PatchMethod == _gameStartHook) != 1)
            throw new MissingMethodException("Enemy Markers GameWorld start hook is missing");
    }
    private static FieldInfo Field(Type owner, string name, Type type)
    {
        var field = owner.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field == null || field.IsStatic || field.IsInitOnly || field.FieldType != type)
            throw new MissingFieldException(owner.FullName, name);
        return field;
    }
    private static MethodInfo Method(Type owner, string name, bool stat, params Type[] args)
    {
        var method = owner.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.IsStatic != stat || method.ReturnType != typeof(void)
            || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(owner.FullName, name);
        return method;
    }
    internal static Version Normalize(Version value) => value == null || value.Build < 0 ? null
        : new Version(value.Major, value.Minor, value.Build, Math.Max(0, value.Revision));
}

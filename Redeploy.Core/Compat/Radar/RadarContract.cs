using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal sealed class RadarContract
{
    internal const string PluginGuid = "com.leonana69.radar";
    internal static readonly Version ReviewedVersion = new(1, 3, 4, 0);
    internal Type ManagerType, HaloType;
    internal FieldInfo Hud, World, Player, Layout, Loot, Exfil, Ready, Players, ScanTime;
    internal FieldInfo Camera, Glass, CompassVisible, Canvas, LootPlayer, Wishlist, ExfilPlayer, ContactPlayer;
    internal MethodInfo Update, OnEnable, Scan, SettingChanged, Status, LayoutApply, ApplySettings, ExfilRebuild, DestroyBlip, TickCompass;
    internal ConstructorInfo NewContact;
    private MethodInfo _gameStartHook, _gameStart;

    internal static RadarContract Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("Accurate Circular Radar 1.3.4 or newer is required");
        if (plugin?.FullName != "Radar.RadarPlugin" || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin))
            throw new MissingMemberException("Radar plugin identity changed");
        var assembly = plugin.Assembly;
        var attribute = plugin.GetCustomAttribute<BepInPlugin>(false);
        if (assembly.GetName().Name != "Tyrian-Radar" || Normalize(assembly.GetName().Version) != version
            || attribute?.GUID != guid || Normalize(attribute.Version) != version)
            throw new InvalidOperationException("Radar assembly/plugin metadata disagree");
        Type Type(string name) => assembly.GetType("Radar." + name, true);
        var manager = Type("InRaidRadarManager"); var halo = Type("HaloRadar");
        var layout = Type("RadarHudLayout"); var loot = Type("LootTracker"); var exfil = Type("ExfilTracker");
        var contact = Type("BlipPlayer");
        if (manager.BaseType != typeof(MonoBehaviour) || halo.BaseType != typeof(MonoBehaviour))
            throw new MissingMemberException("Radar component inheritance changed");
        var c = new RadarContract
        {
            ManagerType = manager, HaloType = halo,
            Hud = Field(manager, "_radarObject", typeof(GameObject), true),
            World = Field(halo, "_gameWorld", typeof(GameWorld)), Player = Field(halo, "_player", typeof(Player)),
            Layout = Field(halo, "_layout", layout), Loot = Field(halo, "_loot", loot), Exfil = Field(halo, "_exfil", exfil),
            Ready = Field(halo, "_ready", typeof(bool)),
            Players = Field(halo, "_players", typeof(Dictionary<,>).MakeGenericType(typeof(string), contact), readOnly: true),
            ScanTime = Field(halo, "_lastPlayerScanTime", typeof(float)),
            Camera = Field(layout, "_fpsCamera", typeof(GameObject), readOnly: true),
            Glass = Field(layout, "_compassGlass", typeof(GameObject)), CompassVisible = Field(layout, "_compassVisible", typeof(bool)),
            Canvas = Field(layout, "_canvas", typeof(Canvas), readOnly: true),
            LootPlayer = Field(loot, "_player", typeof(Player), readOnly: true),
            Wishlist = Field(loot, "_wishlist", typeof(IReadOnlyDictionary<MongoID, EWishlistGroup>), readOnly: true),
            ExfilPlayer = Field(exfil, "_player", typeof(Player), readOnly: true),
            ContactPlayer = Field(contact, "_enemyPlayer", typeof(Player), readOnly: true),
            Update = Method(halo, "Update", false, typeof(void)), Scan = Method(halo, "ScanPlayers", false, typeof(bool)),
            OnEnable = Method(halo, "OnEnable", false, typeof(void)),
            SettingChanged = Method(halo, "OnSettingChanged", false, typeof(void), typeof(object), typeof(BepInEx.Configuration.SettingChangedEventArgs)),
            Status = Method(manager, "UpdateRadarStatus", false, typeof(void)),
            LayoutApply = Method(layout, "Apply", false, typeof(void)), ApplySettings = Method(halo, "ApplyAllSettings", false, typeof(void)),
            TickCompass = Method(layout, "TickCompass", false, typeof(void)),
            ExfilRebuild = Method(exfil, "Rebuild", false, typeof(void)),
            DestroyBlip = Method(Type("Target"), "DestroyBlip", false, typeof(void)),
            NewContact = contact.GetConstructor(new[] { typeof(Player) }),
            _gameStart = AccessTools.Method(typeof(GameWorld), "OnGameStarted"),
            _gameStartHook = Method(assembly.GetType("Radar.Patches.GameStartPatch", true), "Postfix", true, typeof(void), typeof(GameWorld))
        };
        if (c.NewContact == null) throw new MissingMethodException("Radar.BlipPlayer constructor changed");
        c.Verify();
        return c;
    }
    internal void Verify()
    {
        if (Harmony.GetPatchInfo(_gameStart)?.Postfixes.Count(p => p.PatchMethod == _gameStartHook) != 1)
            throw new MissingMethodException("Radar's GameWorld start hook is missing");
    }
    private static FieldInfo Field(Type type, string name, Type value, bool stat = false, bool readOnly = false)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (field == null || field.IsStatic != stat || field.IsInitOnly != readOnly || field.FieldType != value)
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

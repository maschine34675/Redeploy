using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using EFT;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal sealed class SimpleCrosshairContract
{
    internal const string PluginGuid = "com.mpstark.SimpleCrosshair";
    internal static readonly Version ReviewedVersion = new(1, 4, 0, 0);
    internal Type PanelType;
    internal FieldInfo Panel, CachedPlayer, CachedFirearm, CachedCamera;
    internal MethodInfo RegisterPatch, UnregisterPatch, LookPatch, MovementPatch, BattlePatch;
    internal MethodInfo Register, Unregister, HandsChanged, SenseChanged, InteractionsChanged, AimingChanged;
    internal MethodInfo CalculateDynamic, SetReason, Attach, Destroy;
    internal EventInfo HandsEvent, SenseEvent, InteractionsEvent, AimingEvent;
    private MethodInfo[] _native, _foreign;

    internal static SimpleCrosshairContract Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("Simple Crosshair 1.4.0 or newer is required");
        if (plugin == null || plugin.FullName != "SimpleCrosshair.Plugin" || plugin.IsAbstract
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin))
            throw new MissingMemberException("Simple Crosshair plugin identity changed");
        var assembly = plugin.Assembly;
        var attribute = plugin.GetCustomAttribute<BepInPlugin>(false);
        if (assembly.GetName().Name != "SimpleCrosshair" || Normalize(assembly.GetName().Version) != version
            || attribute?.GUID != guid || Normalize(attribute.Version) != version)
            throw new InvalidOperationException("Simple Crosshair assembly/plugin metadata disagree");
        var panel = assembly.GetType("SimpleCrosshair.SimpleCrosshairComponent", true);
        if (panel.IsAbstract || panel.BaseType != typeof(MonoBehaviour))
            throw new MissingMemberException("Simple Crosshair component identity changed");
        MethodInfo Patch(string name, params Type[] args) => Method(assembly.GetType("SimpleCrosshair.Patches." + name, true),
            "PatchPostfix", true, typeof(void), args);
        var c = new SimpleCrosshairContract
        {
            PanelType = panel,
            Panel = Field(plugin, "<SimpleCrosshairComponent>k__BackingField", panel),
            CachedPlayer = Field(panel, "_cachedPlayer", typeof(Player)),
            CachedFirearm = Field(panel, "_cachedFirearmController", typeof(Player.FirearmController)),
            CachedCamera = Field(panel, "_cachedCamera", typeof(Camera)),
            RegisterPatch = Patch("GameWorldRegisterPlayerPatch", typeof(IPlayer)),
            UnregisterPatch = Patch("GameWorldUnregisterPlayerPatch", typeof(IPlayer)),
            LookPatch = Patch("PlayerLookPatch", typeof(Player)),
            MovementPatch = Patch("MovementContextProcessStateEnterPatch", typeof(MovementContext)),
            BattlePatch = Patch("BattleUIScreenShowPatch", typeof(object)),
            Register = Method(panel, "OnRegisterMainPlayer", false, typeof(void)),
            Unregister = Method(panel, "OnUnregisterMainPlayer", false, typeof(void)),
            HandsChanged = Method(panel, "OnHandControllerChanged", false, typeof(void), typeof(Player.AbstractHandsController), typeof(Player.AbstractHandsController)),
            SenseChanged = Method(panel, "OnSenseChanged", false, typeof(void), typeof(bool)),
            InteractionsChanged = Method(panel, "PossibleInteractionsChanged", false, typeof(void)),
            AimingChanged = Method(panel, "OnAimingChanged", false, typeof(void), typeof(bool)),
            CalculateDynamic = Method(panel, "CalculateDynamicAimPoint", false, typeof(void), typeof(bool)),
            SetReason = Method(panel, "SetReasonToHide", false, typeof(void), typeof(string), typeof(bool)),
            Attach = Method(plugin, "TryAttachToBattleUIScreen", false, typeof(void), typeof(EftBattleUIScreen)),
            Destroy = Method(plugin, "DestroyCrosshair", false, typeof(void)),
            HandsEvent = Event(typeof(Player), "OnHandsControllerChanged"),
            SenseEvent = Event(typeof(Player), "OnSenseChanged"),
            InteractionsEvent = Event(typeof(Player), "PossibleInteractionsChanged"),
            AimingEvent = Event(typeof(Player.AbstractHandsController), "OnAimingChanged")
        };
        c._native = new[] {
            typeof(GameWorld).GetMethod(nameof(GameWorld.RegisterPlayer), new[] { typeof(IPlayer) }),
            typeof(GameWorld).GetMethod(nameof(GameWorld.UnregisterPlayer), new[] { typeof(IPlayer) }),
            AccessTools.Method(typeof(Player), nameof(Player.Look)),
            AccessTools.Method(typeof(MovementContext), nameof(MovementContext.ProcessStateEnter)),
            AccessTools.Method(typeof(EftBattleUIScreen).BaseType, "Show", new[] { typeof(GamePlayerOwner) }) };
        c._foreign = new[] { c.RegisterPatch, c.UnregisterPatch, c.LookPatch, c.MovementPatch, c.BattlePatch };
        c.Verify();
        return c;
    }
    internal void Verify()
    {
        for (int i = 0; i < _native.Length; i++)
            if (_native[i] == null || Harmony.GetPatchInfo(_native[i])?.Postfixes.Count(p => p.PatchMethod == _foreign[i]) != 1)
                throw new MissingMethodException("Simple Crosshair native hook missing: " + _foreign[i].DeclaringType?.Name);
    }
    internal static Delegate Handler(EventInfo @event, MethodInfo method, object panel)
        => Delegate.CreateDelegate(@event.EventHandlerType, panel, method, true);
    private static EventInfo Event(Type type, string name)
    {
        var value = type.GetEvent(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (value?.GetAddMethod(true) == null || value.GetRemoveMethod(true) == null)
            throw new MissingMemberException(type.FullName, name);
        return value;
    }
    private static FieldInfo Field(Type type, string name, Type value)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (field == null || field.IsStatic || field.IsInitOnly || field.FieldType != value)
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

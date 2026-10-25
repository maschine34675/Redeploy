using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using EFT;
using EFT.InventoryLogic;
using EFT.Vehicle;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>The shipped 2.0.3 source/DLL contract, not its older repository tag.</summary>
internal sealed class ImpactCueContract
{
    internal const string PluginGuid = "com.dizzlow.impactcue";
    internal static readonly Version ReviewedVersion = new(2, 0, 3, 0);
    internal FieldInfo RaidPlayer, RaidWorld, RaidActive, CombatPlayer, NextBreath;
    internal MethodInfo Ready, RenderGate, Restore, ForceRefresh, HudChanged, QuestChanged, AttachQuest, DetachQuest;
    internal MethodInfo CacheLocal, GetLocal, IsLocalActor, BtrAttached, BtrDetached;
    internal MethodInfo ClearWeapon, ResetTarget, ResetLowAmmo;
    internal readonly List<FieldInfo> BodyCaches = new();

    internal static ImpactCueContract Resolve(Type plugin, string guid, Version reported, out Version version)
    {
        version = Normalize(reported);
        if (guid != PluginGuid || version == null || version < ReviewedVersion)
            throw new NotSupportedException("ImpactCue 2.0.3 or newer is required");
        if (plugin == null || plugin.FullName != "Dizzlow.ImpactCue.Plugin" || plugin.IsAbstract
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin))
            throw new MissingMemberException("ImpactCue plugin identity changed");
        var assembly = plugin.Assembly;
        var attribute = plugin.GetCustomAttribute<BepInPlugin>(false);
        if (assembly.GetName().Name != "ImpactCue-UI-MOD" || Normalize(assembly.GetName().Version) != version
            || attribute?.GUID != guid || Normalize(attribute.Version) != version)
            throw new InvalidOperationException("ImpactCue assembly/plugin metadata disagree");
        var hud = assembly.GetType("Dizzlow.ImpactCue.HudTelemetryRuntime", true);
        var combat = assembly.GetType("Dizzlow.ImpactCue.CombatIntrospection", true);
        if (!hud.IsAbstract || !hud.IsSealed || !combat.IsAbstract || !combat.IsSealed)
            throw new MissingMemberException("ImpactCue runtime identity changed");
        var c = new ImpactCueContract
        {
            RaidPlayer = Field(hud, "_raidPlayer", typeof(Player)),
            RaidWorld = Field(hud, "_raidWorld", typeof(GameWorld)),
            RaidActive = Field(hud, "_raidActive", typeof(bool)),
            CombatPlayer = Field(combat, "_cachedLocalPlayer", typeof(Player)),
            NextBreath = Field(hud, "_nextBreathRefresh", typeof(float)),
            Ready = Method(hud, "EnsureRaidReady", typeof(bool), typeof(float)),
            RenderGate = Method(hud, "get_CanRenderTacticalHudNow", typeof(bool)),
            Restore = Method(hud, "TryRestoreRaidContext", typeof(bool), typeof(Player), typeof(float), typeof(string)),
            ForceRefresh = Method(hud, "ForceImmediateTelemetryRefresh", typeof(void)),
            HudChanged = Method(hud, "NotifyCompatibilityLocalPlayerHudStateChanged", typeof(void)),
            QuestChanged = Method(hud, "NotifyCompatibilityQuestRuntimeChanged", typeof(void)),
            AttachQuest = Method(hud, "TryAttachQuestEventHooks", typeof(bool), typeof(Player), typeof(GameWorld), typeof(bool)),
            DetachQuest = Method(hud, "DetachQuestEventHooks", typeof(void)),
            CacheLocal = Method(combat, "CacheLocalPlayer", typeof(void), typeof(Player)),
            GetLocal = Method(combat, "GetLocalPlayer", typeof(Player)),
            IsLocalActor = Method(combat, "IsLocalActor", typeof(bool), typeof(object)),
            BtrAttached = Method(hud, "NotifyLocalBtrPassengerAttached", typeof(void), typeof(Player), typeof(BTRSide), typeof(int)),
            BtrDetached = Method(hud, "NotifyLocalBtrPassengerDetached", typeof(void), typeof(Player)),
            ClearWeapon = Method(hud, "ClearWeaponSnapshot", typeof(void), typeof(bool)),
            ResetTarget = Method(assembly.GetType("Dizzlow.ImpactCue.CrosshairTargetDetector", true), "Reset", typeof(void)),
            ResetLowAmmo = Method(assembly.GetType("Dizzlow.ImpactCue.LowAmmoWarningManager", true), "ResetObservation", typeof(void))
        };
        c.BodyCaches.Add(Field(hud, "_cachedWeapon", typeof(Weapon)));
        foreach (var name in new[] { "_cachedMagazineSlot", "_cachedMagazineInventoryController", "_cachedAmmoSource",
                     "_cachedHandsItem", "_cachedCurrentWeaponHudItem", "_cachedMovementContext", "_cachedPhysical", "_cachedStamina" })
            c.BodyCaches.Add(Field(hud, name, typeof(object)));
        return c;
    }
    private static FieldInfo Field(Type type, string name, Type value)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (field == null || !field.IsStatic || field.IsInitOnly || field.FieldType != value)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }
    private static MethodInfo Method(Type type, string name, Type result, params Type[] args)
    {
        var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || !method.IsStatic || method.ReturnType != result || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }
    internal static Version Normalize(Version value) => value == null || value.Build < 0 ? null
        : new Version(value.Major, value.Minor, value.Build, Math.Max(0, value.Revision));
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using EFT;
using EFT.Animations;
using HarmonyLib;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>The reviewed CommonLib 0.1.0 physical-controller contract. Resolved at admission only.</summary>
internal sealed class RealismCommonLibContract
{
    internal const string PluginGuid = "com.fontaine.spt-realism-common-lib";
    internal static readonly Version Reviewed = new(0, 1, 0, 0);
    internal readonly FieldInfo[] Globals, Events;
    internal readonly FieldInfo[][] InstanceEvents;
    internal readonly MethodInfo Init;
    internal readonly List<MethodInfo> InstanceCallbacks = new();
    internal readonly List<MethodInfo> DestroyCallbacks = new();
    internal readonly List<MethodInfo> BodyCallbacks = new();
    internal readonly PropertyInfo PlayerProperty;
    private readonly AccessTools.FieldRef<Player.ItemHandsController, Player> _handsOwner;
    private readonly AccessTools.FieldRef<ProceduralWeaponAnimation, Player.FirearmController> _pwaHands;
    private readonly AccessTools.FieldRef<Physical, Player> _physicalOwner;
    private readonly AccessTools.FieldRef<MovementContext, Player> _movementOwner;

    internal RealismCommonLibContract(Type plugin, string guid, Version version)
    {
        var attr = plugin?.GetCustomAttribute<BepInPlugin>(false);
        if (guid != PluginGuid || plugin?.FullName != "RealismCommonLib.Plugin"
            || plugin.Assembly.GetName().Name != "spt-realism-common-lib"
            || !typeof(BaseUnityPlugin).IsAssignableFrom(plugin) || attr?.GUID != guid
            || Normalize(attr.Version) != version || version == null || version < Reviewed)
            throw new NotSupportedException("Realism CommonLib 0.1.0 or newer with matching plugin metadata is required");
        var assembly = plugin.Assembly;
        string[] names = { "PlayerState", "WeaponState", "AimingState", "HealthState", "HazardsState",
            "GearState", "FiringState", "SkillState", "ReloadState" };
        string[] slots = { "PlayerStateInstance", "WeaponStateInstance", "AimStateInstance", "HealthStateInstance",
            "HazardsStateInstance", "GearStateInstance", "FiringStateInstance", "SkillStateInstance", "ReloadStateInstance" };
        Globals = new FieldInfo[names.Length];
        InstanceEvents = new FieldInfo[names.Length][];
        for (int i = 0; i < names.Length; i++)
        {
            var type = assembly.GetType("RealismCommonLib.StateControllers.InstanceState." + names[i], true);
            if (!typeof(MonoBehaviour).IsAssignableFrom(type)) throw new MissingMemberException(names[i] + " is not a controller");
            Globals[i] = plugin.GetField(slots[i], BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (Globals[i]?.FieldType != type || Globals[i].IsInitOnly) throw new MissingFieldException(slots[i]);
            InstanceEvents[i] = DelegateFields(type, false);
            // Only the finite reviewed controller types are inspected, never all loaded methods.
            // Property accessors are ordinary data; all actual instance callbacks are void.
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.IsSpecialName || method.Name == "Awake") continue;
                if (method.ReturnType != typeof(void) || method.GetMethodBody() == null)
                    throw new MissingMethodException("Unreviewed CommonLib controller callback: " + method.Name);
                (method.Name == "OnDestroy" ? DestroyCallbacks : InstanceCallbacks).Add(method);
            }
        }
        PlayerProperty = Globals[0].FieldType.GetProperty("Player", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (PlayerProperty?.PropertyType != typeof(Player) || PlayerProperty.GetGetMethod() == null)
            throw new MissingMemberException("CommonLib PlayerState.Player changed");
        Init = plugin.GetMethod("InitPlayerComponents", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null, new[] { typeof(Player) }, null);
        if (Init?.ReturnType != typeof(void)) throw new MissingMethodException("CommonLib InitPlayerComponents changed");
        Events = new[] { "PlayerEvents", "InputEvents", "PWAEvents" }
            .SelectMany(name => DelegateFields(assembly.GetType("RealismCommonLib.Events." + name, true), true)).ToArray();
        // These source callbacks accept a physical owner; the animator callbacks already compare
        // their exact animator against PlayerState.Player. Guard event sources, not native EFT calls.
        string[] patches = { "InventoryOpenPatch", "OnWeightUpdatedPatch", "OnItemAddedOrRemovedPatch", "PlayerInitPatch",
            "ApplyMotionPatch", "ClampedSpeedPatch", "NVGTogglePatch", "ThermalTogglePatch", "FaceShieldTogglePatch",
            "HandsControllerChangedPatch", "ToggleAimPatch", "AimingChangedPatch", "ShootPatch", "UpdateWeaponVariablesPatch",
            "PwaWeaponParamsPatch", "SetAnimatorAndProceduralValuesPatch", "CheckAmmoPatch", "CheckChamberPatch",
            "BoltActionReloadPatch", "CanStartReloadPatch", "ReloadMagPatch", "QuickReloadMagPatch", "OnMagInsertedPatch",
            "ReloadCylinderMagazinePatch", "ReloadWithAmmoPatch", "ReloadBarrelsPatch" };
        foreach (string name in patches)
        {
            var type = assembly.GetType("RealismCommonLib.Patches." + name, true);
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes(false).Any(a => a.GetType().Name is "PatchPrefix" or "PatchPostfix" or "PatchPrefixAttribute" or "PatchPostfixAttribute")).ToArray();
            if (methods.Length == 0) throw new MissingMethodException(name + " has no native callbacks");
            foreach (var method in methods)
            {
                var first = method.GetParameters().FirstOrDefault()?.ParameterType;
                if (first?.IsByRef == true) first = first.GetElementType();
                if (first != typeof(Player) && first != typeof(Player.FirearmController)
                    && first != typeof(Player.AbstractHandsController) && first != typeof(Physical)
                    && first != typeof(MovementContext) && first != typeof(MovementState) && first != typeof(ProceduralWeaponAnimation))
                    throw new MissingMethodException(name + " physical owner changed");
                if (method.ReturnType != typeof(void) && method.ReturnType != typeof(bool))
                    throw new MissingMethodException(name + " return contract changed");
                BodyCallbacks.Add(method);
            }
        }
        _handsOwner = AccessTools.FieldRefAccess<Player.ItemHandsController, Player>("_player");
        _pwaHands = AccessTools.FieldRefAccess<ProceduralWeaponAnimation, Player.FirearmController>("_firearmController");
        _physicalOwner = AccessTools.FieldRefAccess<Physical, Player>("_player");
        _movementOwner = AccessTools.FieldRefAccess<MovementContext, Player>("_player");
    }

    internal Player Owner(object source)
    {
        if (source is Player player) return player;
        if (source is Player.ItemHandsController hands) return _handsOwner(hands);
        if (source is ProceduralWeaponAnimation pwa) { var fc = _pwaHands(pwa); return fc == null ? null : _handsOwner(fc); }
        if (source is Physical physical) return _physicalOwner(physical);
        if (source is MovementContext movement) return _movementOwner(movement);
        if (source is MovementState state) return state.MovementContext == null ? null : _movementOwner(state.MovementContext);
        return null;
    }

    internal Behaviour[] Capture(Player body, bool partial = false)
    {
        var result = Globals.Select(f => f.GetValue(null) as Behaviour).ToArray();
        if (partial) return result.Select(c => c != null && ReferenceEquals(c.gameObject, body.gameObject) ? c : null).ToArray();
        if (result.Any(c => c == null || !ReferenceEquals(c.gameObject, body.gameObject))
            || !ReferenceEquals(PlayerProperty.GetValue(result[0]), body))
            throw new InvalidOperationException("CommonLib controllers do not belong to the exact physical player");
        return result;
    }
    internal void Restore(Behaviour[] controllers)
    { for (int i = 0; i < Globals.Length; i++) Globals[i].SetValue(null, controllers[i]); }
    internal void Detach(Behaviour[] controllers)
    {
        foreach (var field in Events) RemoveTargets(field, null, controllers);
        for (int i = 0; i < controllers.Length; i++)
        {
            if (ReferenceEquals(controllers[i], null)) continue;
            foreach (var field in InstanceEvents[i]) RemoveTargets(field, controllers[i], controllers);
        }
    }
    internal void ClearOwnedGlobals(Behaviour[] controllers)
    {
        for (int i = 0; i < Globals.Length; i++)
            if (ReferenceEquals(Globals[i].GetValue(null), controllers[i])) Globals[i].SetValue(null, null);
    }
    private static void RemoveTargets(FieldInfo field, object publisher, Behaviour[] controllers)
    {
        var current = field.GetValue(publisher) as Delegate;
        if (current == null) return;
        var updated = current;
        foreach (var handler in current.GetInvocationList())
            if (controllers.Any(c => ReferenceEquals(c, handler.Target))) updated = Delegate.Remove(updated, handler);
        if (!ReferenceEquals(current, updated)) field.SetValue(publisher, updated);
    }
    private static FieldInfo[] DelegateFields(Type type, bool isStatic)
        => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly
            | (isStatic ? BindingFlags.Static : BindingFlags.Instance))
            .Where(f => typeof(Delegate).IsAssignableFrom(f.FieldType) && !f.IsInitOnly).ToArray();
    internal static Version Normalize(Version v) => v == null || v.Build < 0 ? null
        : new Version(v.Major, v.Minor, v.Build, Math.Max(0, v.Revision));
}

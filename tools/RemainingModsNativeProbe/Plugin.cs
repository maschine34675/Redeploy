using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Newtonsoft.Json;
using UnityEngine;

namespace RemainingModsNativeProbe;

[AttributeUsage(AttributeTargets.Method)]
public sealed class UiBridgeActionAttribute : Attribute
{
    public UiBridgeActionAttribute(string name) { Name = name; }
    public string Name { get; }
    public string Description { get; set; }
    public bool Control { get; set; }
}

// No automatic deployment, foreign state writes, repair calls or game events.
[BepInPlugin("com.maschine.RemainingModsNativeProbe", "Remaining Mods Native Probe", "1.0.0")]
public sealed class Plugin : BaseUnityPlugin
{
    private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Player _previous;
    private static Camera _previousCamera;
    private static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);
    private static object Read(Type type, string name, object instance = null)
    {
        var field = type?.GetField(name, All);
        if (field != null) return field.IsStatic || instance != null ? field.GetValue(instance) : null;
        var property = type?.GetProperty(name, All);
        return property?.GetMethod != null && (property.GetMethod.IsStatic || instance != null) ? property.GetValue(instance) : null;
    }
    private static int Id(object value) => value is UnityEngine.Object o && !ReferenceEquals(o, null) ? o.GetInstanceID() : 0;
    private static object Small(object value)
    {
        if (value is UnityEngine.Object o) return new { id = Id(o), alive = o != null };
        if (value == null || value is string || value is bool || value is int || value is float || value is double || value.GetType().IsEnum) return value;
        return new { type = value.GetType().FullName, count = Read(value.GetType(), "Count", value) };
    }
    private static Dictionary<string, object> Fields(Type type, object instance, params string[] names)
        => names.ToDictionary(n => n, n => Small(Read(type, n, instance)));

    [UiBridgeAction("observe", Description = "Read current player/camera and foreign HUD/graphics/action references; remember stores only probe comparison references.")]
    public static string Observe(bool remember = false)
    {
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        var player = world?.MainPlayer;
        var camera = Camera.main;
        var result = new Dictionary<string, object>
        {
            ["frame"] = Time.frameCount, ["body"] = Id(player), ["camera"] = Id(camera), ["world"] = Id(world),
            ["cameraName"] = camera != null ? camera.gameObject.name : null,
            ["cameraActive"] = camera != null && camera.enabled && camera.gameObject.activeInHierarchy,
            ["previousBody"] = Id(_previous), ["bodyChanged"] = !ReferenceEquals(_previous, null) && !ReferenceEquals(_previous, player),
            ["cameraChanged"] = !ReferenceEquals(_previousCamera, null) && !ReferenceEquals(_previousCamera, camera),
            ["oldBodyRetained"] = _previous != null, ["alive"] = player?.HealthController?.IsAlive,
            ["hands"] = player?.HandsController?.GetType().FullName
        };
        var inventory = player?.InventoryController;
        var selected = inventory?.Inventory?.Equipment?.TopPriorityGrenade;
        result["grenadePriority"] = selected == null ? null : new { id = selected.Id.ToString(), template = selected.TemplateId.ToString() };
        if (inventory != null)
            result["grenades"] = InventoryExtension.GetThrowablePriorityGrenadesList(inventory)
                .Select(item => new { id = item.Id.ToString(), template = item.TemplateId.ToString() }).ToArray();
        var amand = Find("AmandsGraphics.AmandsGraphicsClass");
        if (amand != null)
        {
            var instance = Read(Find("AmandsGraphics.AmandsGraphicsPlugin"), "AmandsGraphicsClass");
            var fields = Fields(amand, instance, "Player", "FPSCamera", "FPSCameraCamera", "GraphicsMode", "scene", "levelSettings", "backLens", "sightComponent", "ChromaticAberrationAnimation");
            fields["bodyMatches"] = player != null && ReferenceEquals(player, Read(amand, "Player"));
            fields["cameraMatches"] = camera != null && ReferenceEquals(camera, Read(amand, "FPSCameraCamera"));
            foreach (var name in new[] { "registeredLights", "registeredVolumetricLights" })
                if (Read(amand, name) is IDictionary lights)
                    fields[name] = new { count = lights.Count, destroyed = lights.Keys.Cast<object>().Count(k => k is UnityEngine.Object o && o == null) };
            result["amand"] = fields;
        }
        var hud = Find("HudBreakout.HudRoot");
        if (hud != null)
        {
            var canvas = Read(hud, "Canvas") as Component;
            var instance = canvas != null ? canvas.GetComponent(hud) : null;
            var fields = Fields(hud, instance, "MainPlayer", "Canvas", "IsInventoryOpen", "_wasInRaid", "_escapeMenu");
            fields["bodyMatches"] = player != null && ReferenceEquals(player, Read(hud, "MainPlayer"));
            foreach (var key in new[] { "_group", "_contentGroup" })
            {
                var group = Read(hud, key, instance) as CanvasGroup;
                fields[key] = group != null ? (object)group.alpha : null;
            }
            var equipment = Read(hud, "_equipment", instance);
            if (equipment != null)
            {
                var et = equipment.GetType();
                fields["equipment"] = Fields(et, equipment, "_lastAmmoCurrent", "_lastAmmoMax", "_lastGrenadeCount", "_lastGrenadePlayer");
                foreach (var key in new[] { "_weaponName", "_ammo", "_grenades" })
                {
                    var text = Read(et, key, equipment);
                    fields[key] = text == null ? null : Read(text.GetType(), "text", text);
                }
            }
            var vitals = Read(hud, "_vitals", instance);
            if (vitals != null && Read(vitals.GetType(), "_stats", vitals) is IEnumerable stats)
                fields["stats"] = stats.Cast<object>().Select(s => Fields(s.GetType(), s, "StatId", "LastDisplayedCurrent", "LastDisplayedMaximum")).ToArray();
            result["hud"] = fields;
        }
        var quick = Find("Manimal.QuickCancel.QuickCancelState");
        if (quick != null) result["quickCancel"] = Small(Read(quick, "PendingControllers"));
        var pip = Find("PiPDisabler.ScopeLifecycle");
        if (pip != null)
        {
            result["pip"] = Fields(pip, null, "_activeOptic", "_lastEnabledOptic", "_subscribedMovementContext");
            result["pipMeshes"] = Fields(Find("PiPDisabler.MeshSurgeryManager"), null, "_raidCaches", "_currentWeaponCache", "_currentWeaponId", "_inventoryEventSource");
        }
        var fast = Find("FastGrenadeThrow.FastGrenadeThrowPlugin");
        if (fast != null) result["fastGrenade"] = Fields(fast, null, "_throwInProgress", "ForceLowThrow");
        if (remember) { _previous = player; _previousCamera = camera; }
        return JsonConvert.SerializeObject(result);
    }
}

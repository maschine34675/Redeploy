using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using UnityEngine;

namespace StandaloneCompatNativeProbe;

[AttributeUsage(AttributeTargets.Method)]
public sealed class UiBridgeActionAttribute : Attribute
{
    public UiBridgeActionAttribute(string name) { Name = name; }
    public string Name { get; }
    public string Description { get; set; }
    public bool Control { get; set; }
}

// Read-only observation. No foreign setters, repair methods or lifecycle events.
[BepInPlugin("com.maschine.StandaloneCompatNativeProbe", "Standalone Compat Native Probe", "1.0.0")]
public sealed class Plugin : BaseUnityPlugin
{
    private const BindingFlags All = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static Player _previous;
    private static object _previousHealth, _previousQuest;
    private static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);
    private static object Read(Type type, string name, object instance = null)
    {
        var field = type?.GetField(name, All);
        if (field != null) return field.IsStatic || instance != null ? field.GetValue(instance) : null;
        var property = type?.GetProperty(name, All);
        return property?.GetMethod != null && (property.GetMethod.IsStatic || instance != null) ? property.GetValue(instance) : null;
    }
    private static int Id(object value) => value == null ? 0 : value is UnityEngine.Object o ? o.GetInstanceID() : RuntimeHelpers.GetHashCode(value);
    private static int Count(object value) => value is ICollection items ? items.Count : -1;
    private static Component Component(GameWorld world, Type type) => world != null && type != null ? world.GetComponent(type) : null;

    [UiBridgeAction("observe", Description = "Read physical player ownership and AI/medical/quest/audio bindings; remember only stores probe comparison references.")]
    public static string Observe(bool remember = false)
    {
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        var player = world?.MainPlayer;
        var result = new Dictionary<string, object>
        {
            ["frame"] = Time.frameCount, ["world"] = Id(world), ["body"] = Id(player),
            ["profile"] = player?.ProfileId, ["alive"] = player?.HealthController?.IsAlive,
            ["previousBody"] = Id(_previous), ["bodyChanged"] = !ReferenceEquals(_previous, null) && !ReferenceEquals(_previous, player),
            ["oldBodyRetained"] = _previous != null, ["hands"] = player?.HandsController?.GetType().FullName,
            ["health"] = Id(player?.ActiveHealthController), ["quest"] = Id(player?.QuestController),
            ["healthChanged"] = _previousHealth != null && !ReferenceEquals(_previousHealth, player?.ActiveHealthController),
            ["questChanged"] = _previousQuest != null && !ReferenceEquals(_previousQuest, player?.QuestController)
        };
        var aiType = Find("AILimit.AILimitComponent");
        var ai = Component(world, aiType);
        var players = Read(aiType, "players", ai) as IEnumerable;
        result["ai"] = new { component = Id(ai), players = players?.Cast<Player>().Select(p => new { body = Id(p), current = ReferenceEquals(p, player), previous = ReferenceEquals(p, _previous), position = p != null ? p.Position.ToString() : null }).ToArray() };
        var globals = Find("ImprovedSelfcare.Globals");
        var hp = Read(globals, "player");
        var hc = Read(globals, "activeHealthController");
        result["healing"] = new { player = Id(hp), health = Id(hc), playerMatches = player != null && ReferenceEquals(hp, player), healthMatches = hc != null && ReferenceEquals(hc, player?.ActiveHealthController) };
        var qt = Find("DrakiaXYZ.QuestTracker.Components.QuestTrackerComponent");
        var tracker = Component(world, qt);
        var qp = Read(qt, "player", tracker);
        var qc = Read(qt, "questController", tracker);
        var panel = Read(qt, "panel", tracker);
        result["quests"] = new { component = Id(tracker), player = Id(qp), controller = Id(qc), playerMatches = player != null && ReferenceEquals(qp, player), controllerMatches = qc != null && ReferenceEquals(qc, player?.QuestController), mapCount = Count(Read(qt, "mapQuests", tracker)), trackedCount = Count(Read(qt, "trackedQuests", tracker)), panel = Id(panel), visible = Read(panel?.GetType(), "Visible", panel) };
        var audioType = Find("AccessibilityIndicators.IndicatorUI.Panel");
        var hud = Read(audioType, "IndicatorHUD") as GameObject;
        var center = Read(audioType, "HUDCenterPoint") as GameObject;
        var rotation = Read(audioType, "keepNorthRotationScript");
        result["audio"] = new { hud = Id(hud), active = hud != null && hud.activeInHierarchy, center = Id(center), rotationActive = Read(rotation?.GetType(), "isActuallyActive", rotation), yaw = player?.CameraPosition?.rotation.eulerAngles.y, angle = center != null ? (float?)center.transform.rotation.eulerAngles.z : null, north = Read(audioType, "NorthDirection") };
        if (remember) { _previous = player; _previousHealth = player?.ActiveHealthController; _previousQuest = player?.QuestController; }
        return JsonConvert.SerializeObject(result);
    }
}

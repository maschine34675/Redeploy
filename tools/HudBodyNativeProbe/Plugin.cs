using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using Comfort.Common;
using EFT;
using Newtonsoft.Json;
using UnityEngine;

namespace HudBodyNativeProbe;

[AttributeUsage(AttributeTargets.Method)]
public sealed class UiBridgeActionAttribute : Attribute
{
    public UiBridgeActionAttribute(string name) { Name = name; }
    public string Name { get; }
    public string Description { get; set; }
    public bool Control { get; set; }
}

// Read-only native acceptance evidence. Never packaged or automatically deployed.
[BepInPlugin("com.maschine.HudBodyNativeProbe", "HUD Body Native Probe", "1.0.0")]
public sealed class Plugin : BaseUnityPlugin
{
    private static readonly BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
    private static Player _previousBody;
    private static Camera _previousCamera;
    private static Type Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);
    private static object Field(Type type, string name, object instance = null) => type?.GetField(name, All)?.GetValue(instance);
    private static int Id(object item) => item is UnityEngine.Object o && !ReferenceEquals(o, null) ? o.GetInstanceID() : 0;

    [UiBridgeAction("observe", Description = "Read physical HUD/marker identity and displayed telemetry; remember=true records a baseline for later comparisons.")]
    public static string Observe(bool remember = false)
    {
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        var body = world?.MainPlayer;
        var camera = Camera.main;
        var report = new Dictionary<string, object>
        {
            ["frame"] = Time.frameCount, ["world"] = Id(world), ["body"] = Id(body), ["camera"] = Id(camera),
            ["previousBody"] = Id(_previousBody), ["bodyChanged"] = !ReferenceEquals(_previousBody, null) && !ReferenceEquals(_previousBody, body),
            ["cameraChanged"] = !ReferenceEquals(_previousCamera, null) && !ReferenceEquals(_previousCamera, camera),
            ["previousBodyRetained"] = _previousBody != null, ["alive"] = body?.HealthController?.IsAlive
        };
        var hud = Find("Dizzlow.ImpactCue.HudTelemetryRuntime");
        var combat = Find("Dizzlow.ImpactCue.CombatIntrospection");
        report["impactBody"] = Id(Field(hud, "_raidPlayer"));
        report["impactMatches"] = body != null && ReferenceEquals(body, Field(hud, "_raidPlayer"));
        report["combatMatches"] = body != null && ReferenceEquals(body, Field(combat, "_cachedLocalPlayer"));
        report["impactActive"] = Field(hud, "_raidActive");
        if (hud != null)
        {
            var values = new Dictionary<string, object>();
            var snapshot = Field(hud, "Snapshot");
            if (snapshot != null)
                foreach (var name in new[] { "Valid", "HealthCurrent", "HealthMaximum", "WeaponValid", "MagazineAmmo", "MagazineCapacity", "GrenadeCount", "WeaponName" })
                    values[name] = Field(snapshot.GetType(), name, snapshot);
            report["impactTelemetry"] = values;
        }
        var componentType = Find("flir.enemymarkers.EmComponent");
        var markers = new List<object>();
        if (world != null && componentType != null)
        foreach (var component in world.gameObject.GetComponents(componentType))
        {
            if (!(Field(componentType, "_markersDict", component) is IDictionary table)) continue;
            foreach (DictionaryEntry entry in table)
            {
                var marker = Field(entry.Value.GetType(), "Marker", entry.Value) as GameObject;
                var billboard = marker != null ? marker.GetComponent(componentType.GetNestedType("Billboard", BindingFlags.NonPublic)) : null;
                var bound = billboard == null ? null : Field(billboard.GetType(), "_mainCamera", billboard);
                markers.Add(new { player = Id(entry.Key), local = ReferenceEquals(entry.Key, body) || ReferenceEquals(entry.Key, _previousBody),
                    marker = Id(marker), camera = Id(bound), matches = camera != null && ReferenceEquals(camera, bound),
                    active = marker != null && marker.activeInHierarchy, rotation = marker == null ? "none" : marker.transform.rotation.ToString(),
                    cameraAngle = marker != null && camera != null ? Quaternion.Angle(marker.transform.rotation, camera.transform.rotation) : -1f });
            }
        }
        report["markers"] = markers;
        var counter = Find("ArenaMode.Hud.InfoPanels.MagazineInventoryCounter");
        var weapon = (body?.HandsController as Player.FirearmController)?.Item;
        if (counter != null && weapon != null)
        {
            var args = new object[] { body, weapon, null };
            report["arenaMagAvailable"] = counter.GetMethod("TryGetSpareMagSummary", All).Invoke(null, args);
            report["arenaMagFull"] = Field(args[2].GetType(), "FullCount", args[2]);
            report["arenaMagPartial"] = Field(args[2].GetType(), "PartialCount", args[2]);
        }
        if (remember) { _previousBody = body; _previousCamera = camera; }
        return JsonConvert.SerializeObject(report);
    }
}

// Deliberately minimal foreign boundary fixture. Adapter logic is linked from production.
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;
namespace PiPDisabler;
[BepInPlugin("com.fiodor.pipdisabler", "PiP-Disabler", "2.1.0")]
public sealed class PiPDisablerPlugin : BaseUnityPlugin { }
public static class ScopeLifecycle
{
    private static OpticSight _activeOptic, _lastEnabledOptic;
    private static MovementContext _subscribedMovementContext;
    public static OpticSight Active => _activeOptic;
    public static int HideCalls, UpdateCalls, ExitCalls;
    [MethodImpl(MethodImplOptions.NoInlining)] public static void OnOpticEnabled(OpticSight optic)
    { _activeOptic = optic; _lastEnabledOptic = optic; ReticleRenderer.PointAt(optic?.transform); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void OnOpticDisabled(OpticSight optic) { HideCalls++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void RestoreBypassedOpticState(OpticSight optic, string reason, bool restoreFov) { OnOpticEnabled(optic); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void CheckAndUpdate(string caller) { UpdateCalls++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Tick() { UpdateCalls++; }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void ForceExit()
    { ExitCalls++; _activeOptic = _lastEnabledOptic = null; ReticleRenderer.PointAt(null); LensTransparency.RestoreAll(); UnsubscribeMovementState(); }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void ClearPostExitRestore() { }
    [MethodImpl(MethodImplOptions.NoInlining)] private static void UnsubscribeMovementState() { _subscribedMovementContext = null; }
    public static void Subscribe(Player player) => _subscribedMovementContext = player.MovementContext;
}
public static class ReticleRenderer { private static Transform _opticTransform; public static void PointAt(Transform value) => _opticTransform = value; }
public static class CameraSettingsManager { public static int RestoreCalls; [MethodImpl(MethodImplOptions.NoInlining)] public static void ForceRestore() { RestoreCalls++; } }
public static class LensTransparency
{
    private struct HiddenEntry { public Renderer Renderer; }
    private static readonly List<HiddenEntry> _hidden = new();
    public static void Hide(Renderer value) { _hidden.Add(new HiddenEntry { Renderer = value }); }
    public static void RestoreAll() => _hidden.Clear();
}
public static class MeshSurgeryManager
{
    private sealed class CutMeshEntry { public MeshFilter Filter; public Mesh OriginalMesh, CutMesh; public bool Applied; }
    private sealed class CutProfileCache { public GameObject WeaponRoot; public readonly List<CutMeshEntry> Entries = new(); }
    private sealed class RaidWeaponCache { public readonly Dictionary<string, CutProfileCache> Profiles = new(); }
    private sealed class LightFxState { public bool WasActiveSelf = false, DisabledByUs = false; }
    private sealed class SphereState { public bool WasActiveSelf = false, DisabledByUs = false; }
    private static readonly Dictionary<string, RaidWeaponCache> _raidCaches = new();
    private static readonly Dictionary<GameObject, LightFxState> _disabledLightFx = new();
    private static readonly Dictionary<GameObject, SphereState> _disabledWeaponSpheres = new();
    private static CutProfileCache _currentWeaponCache;
    private static string _currentWeaponId;
    private static object _inventoryEventSource;
    public static bool Subscribed => _inventoryEventSource != null;
    public static int Count => _raidCaches.Values.Sum(w => w.Profiles.Count);
    public static bool Contains(string id) => _raidCaches.ContainsKey(id);
    public static void EmptyForeign(string id) => _raidCaches[id] = new RaidWeaponCache();
    public static void Subscribe(Player body) => _inventoryEventSource = body.InventoryController;
    [MethodImpl(MethodImplOptions.NoInlining)] private static void UnbindInventoryEvents() => _inventoryEventSource = null;
    private static GameObject _nextRoot;
    private static MeshFilter _nextFilter;
    [MethodImpl(MethodImplOptions.NoInlining)] private static CutProfileCache GetOrCreateCurrentWeaponCache(Transform scopeRoot, Transform activeMode)
    {
        if (_nextRoot != null) _currentWeaponCache.WeaponRoot = _nextRoot;
        return _currentWeaponCache;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] public static void ApplyForOptic(OpticSight optic)
    {
        var cache = GetOrCreateCurrentWeaponCache(optic.transform, optic.transform);
        if (cache == null) return;
        // Exact upstream order: cache root changes first, old Entries rebind later.
        if (_nextFilter != null)
        {
            foreach (var e in cache.Entries) { e.Filter = _nextFilter; e.Filter.sharedMesh = e.CutMesh; e.Applied = true; }
            _nextRoot = null; _nextFilter = null;
        }
    }
    public static void Observe(Transform root) => ApplyForOptic((OpticSight)root.gameObject.GetComponent(typeof(OpticSight)));
    public static void RebindNext(GameObject root, MeshFilter filter) { _nextRoot = root; _nextFilter = filter; }
    public static object Add(string id, GameObject root, MeshFilter filter, Mesh original, Mesh cut)
    {
        var profile = new CutProfileCache { WeaponRoot = root };
        profile.Entries.Add(new CutMeshEntry { Filter = filter, OriginalMesh = original, CutMesh = cut, Applied = true });
        filter.sharedMesh = cut;
        _raidCaches[id] = new RaidWeaponCache(); _raidCaches[id].Profiles["same-key"] = profile;
        _currentWeaponCache = profile; _currentWeaponId = id; return profile;
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static void RestoreOriginalMeshes(CutProfileCache cache)
    { foreach (var e in cache.Entries) if (e.Filter != null && e.Applied) { e.Filter.sharedMesh = e.OriginalMesh; e.Applied = false; } }
    [MethodImpl(MethodImplOptions.NoInlining)] private static void DestroyCutMeshes(CutProfileCache cache)
    { foreach (var e in cache.Entries) if (e.CutMesh != null) UnityEngine.Object.Destroy(e.CutMesh); }
}
public static class ForeignOpticHooks
{
    public static void Install()
    {
        var h = new Harmony("fixture.pip.original");
        h.Patch(typeof(OpticSight).GetMethod("OnEnable"), postfix: new HarmonyMethod(typeof(ForeignOpticHooks), nameof(Enabled)));
        h.Patch(typeof(OpticSight).GetMethod("OnDisable"), postfix: new HarmonyMethod(typeof(ForeignOpticHooks), nameof(Disabled)));
    }
    public static void Enabled(OpticSight __instance) => ScopeLifecycle.OnOpticEnabled(__instance);
    public static void Disabled(OpticSight __instance) => ScopeLifecycle.OnOpticDisabled(__instance);
}

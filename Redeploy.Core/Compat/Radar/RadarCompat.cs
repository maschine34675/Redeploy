using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Keep Radar's existing HUD and subscribed trackers across Core's camera/body replacement.</summary>
internal static class RadarCompat
{
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.RadarCompat");
    private static readonly RadarScope Scope = new();
    private static readonly Dictionary<ConfigEntryBase, SettingChangedEventArgs> PendingSettings = new();
    private static RadarContract _contract;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static MonoBehaviour _halo;
    private static Transform _savedParent;
    private static Vector3 _savedPosition, _savedScale;
    private static Quaternion _savedRotation;
    private static GameObject _boundCamera;
    private static bool _saved, _patched, _fault, _newerWarned;
    private static string _failure, _warning;
    private static LocalGame Game => Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
    private static GameWorld World => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;

    internal static string GetRejectionReason()
    {
        if (_fault) return _failure;
        if (!Chainloader.PluginInfos.TryGetValue(RadarContract.PluginGuid, out var info)) return null;
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Accurate Circular Radar has not finished loading");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance) && _version == RadarContract.Normalize(info.Metadata?.Version))
            { _contract.Verify(); VerifyPatches(); return _failure; }
            if (Scope.Enabled) throw new InvalidOperationException("Radar plugin changed during an owned raid");
            ClearBinding();
            _contract = RadarContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version, out _version);
            _plugin = info.Instance;
            _patched = true;
            Patch(_contract.Status, nameof(BeforeStatus));
            Patch(_contract.Update, nameof(BeforeUpdate));
            Patch(_contract.OnEnable, nameof(BeforeEnable));
            Patch(_contract.SettingChanged, nameof(BeforeSettingChanged));
            Patch(_contract.Scan, postfix: nameof(AfterScan));
            Patch(_contract.TickCompass, postfix: nameof(AfterCompass));
            VerifyPatches();
            _failure = null;
            CoreLog.LogDebug("Accurate Circular Radar: physical HUD/camera handover ready.");
            if (_version > RadarContract.ReviewedVersion && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("Accurate Circular Radar " + _version + " is newer than reviewed 1.3.4; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            if (!Scope.Enabled) ClearBinding();
            Reject(ex);
        }
        return _failure;
    }
    private static void Patch(MethodInfo target, string prefix = null, string postfix = null)
        => Patches.Patch(target, prefix == null ? null : Hook(prefix), postfix == null ? null : Hook(postfix));
    private static HarmonyMethod Hook(string name) => new(typeof(RadarCompat), name) { priority = Priority.First };
    private static void VerifyPatches()
    {
        void Check(MethodInfo target, string hook, bool prefix)
        {
            var info = Harmony.GetPatchInfo(target);
            var entries = prefix ? info?.Prefixes : info?.Postfixes;
            var method = typeof(RadarCompat).GetMethod(hook, BindingFlags.Static | BindingFlags.NonPublic);
            if (entries?.Count(p => p.owner == Patches.Id && p.PatchMethod == method) != 1)
                throw new MissingMethodException("Radar compatibility hook is missing: " + hook);
        }
        Check(_contract.Status, nameof(BeforeStatus), true);
        Check(_contract.Update, nameof(BeforeUpdate), true);
        Check(_contract.OnEnable, nameof(BeforeEnable), true);
        Check(_contract.SettingChanged, nameof(BeforeSettingChanged), true);
        Check(_contract.Scan, nameof(AfterScan), false);
        Check(_contract.TickCompass, nameof(AfterCompass), false);
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        if (_contract == null) return;
        Scope.Prepare(game, game?.GameWorld, oldBody,
            BodyReplacementContext.OwnsRaid(game, game?.GameWorld) && BodyReplacementContext.OwnsBody(oldBody));
        try { Capture(oldBody); Detach(); }
        catch (Exception ex) { AbortPreparation(); throw new InvalidOperationException("Radar handover could not be prepared: " + ex.GetBaseException().Message, ex); }
    }
    internal static void AbortPreparation()
    {
        var manager = Scope.Manager as Component;
        var preserved = Scope.Hud as GameObject;
        if (_saved && Scope.Hud is GameObject hud && hud != null && _savedParent != null)
        {
            hud.transform.SetParent(_savedParent, false);
            hud.transform.localPosition = _savedPosition;
            hud.transform.localRotation = _savedRotation;
            hud.transform.localScale = _savedScale;
        }
        _saved = false; _savedParent = null;
        Scope.AbortPreparation();
        if (manager != null && preserved != null && _contract != null && ReferenceEquals(_contract.Hud.GetValue(null), preserved))
            _contract.Status.Invoke(manager, null);
        ReplaySettings();
        if (!Scope.Enabled) { _halo = null; _boundCamera = null; }
    }
    internal static void BeforeCameraChange(LocalPlayer body)
    {
        if (_contract == null || !Scope.Enabled || !BodyReplacementContext.OwnsBody(body)
            || !BodyReplacementContext.OwnsRaid(Game, World)) return;
        try
        {
            Scope.Freeze(Game, World, body, ExactCurrent(body));
            if (Scope.Hud == null) Capture(Scope.Body as Player);
            Detach();
        }
        catch (Exception ex) { Fault(ex); throw; }
    }
    internal static void AfterCameraChange(LocalPlayer body)
    {
        if (_contract == null || !Scope.Enabled || !ExactCurrent(body)) return;
        try { Rebind(body); }
        catch (Exception ex) { Fault(ex); throw; }
    }
    internal static void Tick()
    {
        if (_contract == null || !Scope.Enabled || _fault) return;
        try
        {
            if (!BodyReplacementContext.OwnsRaid(Game, World)) { RetireWorld(Scope.World as GameWorld); return; }
            var body = World.MainPlayer as LocalPlayer;
            if (!ExactCurrent(body) || !CameraOwns(body) || BodyReplacementContext.IsCreating) return;
            if (Scope.Hud == null) Capture(body); // Initial kit handover can precede OnGameStarted.
            var camera = CameraManager.Instance?.Camera;
            if (Scope.Frozen || !ReferenceEquals(Scope.Body, body) || (camera != null && !ReferenceEquals(_boundCamera, camera.gameObject)))
                Rebind(body);
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static bool ExactCurrent(Player body)
        => body != null && body.IsYourPlayer && BodyReplacementContext.OwnsRaid(Game, World)
            && BodyReplacementContext.OwnsBody(body) && ReferenceEquals(body, Game?.LocalPlayer)
            && ReferenceEquals(body, World?.MainPlayer);
    private static bool CameraOwns(Player body)
        => Singleton<PlayerCameraController>.Instantiated
            && ReferenceEquals(Singleton<PlayerCameraController>.Instance.Player, body);

    private static void Capture(Player owner)
    {
        if (Scope.Hud != null) return;
        var world = Scope.World as GameWorld;
        if (world == null) throw new InvalidOperationException("Radar world disappeared");
        var managers = world.gameObject.GetComponents(_contract.ManagerType);
        if (managers.Length == 0) return;
        if (managers.Length != 1) throw new InvalidOperationException("Radar has multiple managers for this world");
        var manager = managers[0];
        var hud = _contract.Hud.GetValue(null) as GameObject;
        if (hud == null) throw new InvalidOperationException("Radar HUD is missing before replacement");
        var halo = hud.GetComponent(_contract.HaloType) as MonoBehaviour;
        if (halo == null || !(bool)_contract.Ready.GetValue(halo)
            || !ReferenceEquals(_contract.World.GetValue(halo), world) || !ReferenceEquals(_contract.Player.GetValue(halo), owner))
            throw new InvalidOperationException("Radar HUD belongs to a different physical player/world");
        Scope.Adopt(hud, manager, Scope.Game, world, owner, BodyReplacementContext.OwnsBody(owner));
        _halo = halo;
        _boundCamera = _contract.Camera.GetValue(_contract.Layout.GetValue(halo)) as GameObject;
    }
    private static void Detach()
    {
        if (Scope.Hud == null) return;
        var hud = Scope.Hud as GameObject;
        if (hud == null || _halo == null) throw new InvalidOperationException("Owned Radar HUD was destroyed");
        if (!_saved)
        {
            _saved = true; _savedParent = hud.transform.parent;
            _savedPosition = hud.transform.localPosition; _savedRotation = hud.transform.localRotation; _savedScale = hud.transform.localScale;
        }
        // Keep component/coroutines and container subscriptions alive. Only remove the parent
        // which Core is about to destroy; no foreign Awake/OnEnable or GameWorld start replay.
        hud.transform.SetParent(((GameWorld)Scope.World).transform, true);
    }
    private static void Rebind(LocalPlayer body)
    {
        var camera = CameraManager.Instance?.Camera;
        if (camera == null || !CameraOwns(body)) throw new InvalidOperationException("Replacement FPS camera does not belong to the current body");
        if (Scope.Hud == null)
        {
            Scope.Bind(Game, World, body, ExactCurrent(body));
            Capture(body);
            if (Scope.Hud == null) { _boundCamera = camera.gameObject; return; }
        }
        var hud = Scope.Hud as GameObject;
        if (hud == null || _halo == null || !ReferenceEquals(_contract.Hud.GetValue(null), hud))
            throw new InvalidOperationException("Radar HUD changed during replacement");
        if (!Scope.Frozen && ReferenceEquals(Scope.Body, body) && ReferenceEquals(_boundCamera, camera.gameObject)) return;
        var layout = _contract.Layout.GetValue(_halo);
        var loot = _contract.Loot.GetValue(_halo);
        var exfil = _contract.Exfil.GetValue(_halo);
        _contract.Player.SetValue(_halo, body);
        _contract.LootPlayer.SetValue(loot, body);
        _contract.Wishlist.SetValue(loot, body.Profile?.WishlistManager.GetWishlist());
        _contract.ExfilPlayer.SetValue(exfil, body);
        _contract.Camera.SetValue(layout, camera.gameObject);
        _contract.Glass.SetValue(layout, null);
        _contract.CompassVisible.SetValue(layout, false);
        _contract.LayoutApply.Invoke(layout, null);
        if (_contract.Canvas.GetValue(layout) is Canvas canvas && canvas.renderMode == RenderMode.WorldSpace) canvas.worldCamera = camera;
        _contract.ExfilRebuild.Invoke(exfil, null);
        _contract.ScanTime.SetValue(_halo, float.NegativeInfinity);
        // A disabled radar keeps its existing inactive HUD. Starting its pulse coroutine
        // while inactive is a Unity error; native OnEnable applies settings if Status
        // enables it after the physical handover has committed below.
        if (_halo.isActiveAndEnabled) _contract.ApplySettings.Invoke(_halo, null);
        Scope.Bind(Game, World, body, ExactCurrent(body));
        _boundCamera = camera.gameObject;
        _saved = false; _savedParent = null;
        _contract.Status.Invoke(Scope.Manager, null); // Applies any enable toggle deferred during handover.
        ReplaySettings();
        ReconcileContacts();
        CoreLog.LogDebug("Accurate Circular Radar: existing HUD rebound to the replacement player/camera.");
    }

    private static bool OwnsHalo(object halo) => _halo != null && ReferenceEquals(_halo, halo) && Scope.ActiveHud(_halo.gameObject);
    private static bool KnownHalo(object halo) => halo is MonoBehaviour component && component != null && Scope.OwnsHud(component.gameObject);
    private static bool BeforeUpdate(object __instance)
        => !KnownHalo(__instance) || (OwnsHalo(__instance) && !_fault && !Scope.Frozen && ExactCurrent(Scope.Body as Player));
    private static bool BeforeEnable(object __instance)
        => !KnownHalo(__instance) || (OwnsHalo(__instance) && !_fault && !Scope.Frozen);
    private static bool BeforeSettingChanged(object __instance, SettingChangedEventArgs __1)
    {
        if (!KnownHalo(__instance)) return true;
        if (!OwnsHalo(__instance) || _fault) return false;
        if (!Scope.Frozen) return true;
        if (__1?.ChangedSetting != null) PendingSettings[__1.ChangedSetting] = __1;
        return false;
    }
    private static void ReplaySettings()
    {
        var pending = PendingSettings.Values.ToArray(); PendingSettings.Clear();
        if (_halo == null || !_halo.isActiveAndEnabled || _fault || Scope.Frozen) return;
        foreach (var changed in pending) _contract.SettingChanged.Invoke(_halo, new object[] { _plugin, changed });
    }
    private static bool BeforeStatus(object __instance)
    {
        if (!Scope.OwnsManager(__instance)) return true;
        // The upstream missing-HUD fallback destroys manager.gameObject, i.e. GameWorld.
        // Also fence old-world callbacks from toggling a newer world's static HUD.
        if (_fault || Scope.Frozen || !Scope.ActiveManager(__instance)) return false;
        var hud = Scope.Hud as GameObject;
        if (hud != null && ReferenceEquals(_contract.Hud.GetValue(null), hud)) return true;
        Fault(new InvalidOperationException("Owned Radar HUD was lost; world destruction was prevented"));
        return false;
    }
    private static void AfterScan(object __instance, bool __result)
    {
        if (!__result || !OwnsHalo(__instance) || Scope.Frozen || _fault || !ExactCurrent(Scope.Body as Player)) return;
        try { ReconcileContacts(); } catch (Exception ex) { Fault(ex); }
    }
    private static void ReconcileContacts()
    {
        if (_halo == null || Scope.Frozen || !ExactCurrent(Scope.Body as Player)) return;
        var current = new Dictionary<string, Player>();
        foreach (var player in World.AllPlayersEverExisted)
            if (player != null && !player.IsYourPlayer && !ReferenceEquals(player, Scope.Body) && !string.IsNullOrEmpty(player.ProfileId)) current[player.ProfileId] = player;
        var contacts = (IDictionary)_contract.Players.GetValue(_halo);
        foreach (var id in contacts.Keys.Cast<string>().ToArray())
        {
            var contact = contacts[id]; var tracked = _contract.ContactPlayer.GetValue(contact) as Player;
            if (!current.TryGetValue(id, out var now) || tracked == null || RadarScope.ReplaceContact(tracked, now))
            {
                _contract.DestroyBlip.Invoke(contact, null); contacts.Remove(id);
            }
        }
        foreach (var pair in current)
            if (!contacts.Contains(pair.Key)) contacts[pair.Key] = _contract.NewContact.Invoke(new object[] { pair.Value });
    }
    private static void AfterCompass(object __instance)
    {
        if (_halo == null || Scope.Frozen || _fault || !ExactCurrent(Scope.Body as Player)) return;
        try
        {
            if (!ReferenceEquals(__instance, _contract.Layout.GetValue(_halo))) return;
            var glass = _contract.Glass.GetValue(__instance) as GameObject;
            if (glass == null) return;
            var body = (Player)Scope.Body;
            var firearm = body.HandsController as Player.FirearmController;
            if (glass.transform.IsChildOf(((Component)body).transform) || (firearm?.GunBaseTransform != null && glass.transform.IsChildOf(firearm.GunBaseTransform))) return;
            // Global Find may still return the retired body's compass until deferred destruction.
            _contract.Glass.SetValue(__instance, null); _contract.CompassVisible.SetValue(__instance, false);
            if (_boundCamera != null) _halo.transform.SetParent(_boundCamera.transform, false);
        }
        catch (Exception ex) { Fault(ex); }
    }
    internal static void RetireWorld(GameWorld world)
    {
        if (ReferenceEquals(world, null) || !ReferenceEquals(Scope.World, world)) return;
        // A frozen preserved root is parented under this world and dies with it. Do not touch
        // static Radar roots from a newer world, or replay foreign lifecycle methods.
        Scope.RetireWorld(world);
        PendingSettings.Clear();
        _halo = null; _boundCamera = null; _saved = false; _savedParent = null;
    }
    private static void Fault(Exception ex) { _fault = true; Reject(ex); }
    private static void Reject(Exception ex)
    {
        _failure = "Accurate Circular Radar body replacement is unavailable: " + ex.GetBaseException().Message;
        if (_warning == _failure) return;
        _warning = _failure; CoreLog.LogWarning(_failure);
    }
    private static void ClearBinding()
    {
        if (_patched) Patches.UnpatchSelf();
        _patched = false; _contract = null; _plugin = null; _version = null;
    }
}

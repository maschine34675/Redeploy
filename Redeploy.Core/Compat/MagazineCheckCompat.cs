using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using Redeploy.Core.Api;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Retire Magazine Check Interrupt's UI only when Core replaces its physical owner.</summary>
internal static class MagazineCheckCompat
{
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.MagazineCheckCompat");
    private static readonly MagazineCheckScope Scope = new();
    private static readonly object CompletionLock = new();
    private static readonly Queue<Completion> Completions = new();
    private const int CompletionLimit = 64;
    private sealed class Completion { internal MagazineCheckScope.Ticket Ticket; internal Task Task; }
    private sealed class Creation { internal Player Body; internal LocalGame Game; internal GameWorld World; }
    [ThreadStatic] private static MagazineCheckScope.Ticket _finishing;
    private static MagazineCheckContract _contract;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static bool _initialized, _patchAttempted, _fault, _newerWarned, _queueOverflow;
    private static string _failure, _lastWarning;
    private static LocalGame Game => Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
    private static GameWorld World => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;

    internal static string GetRejectionReason()
    {
        if (!_initialized) { _initialized = true; RespawnApi.Replaced += Replaced; }
        if (_fault) return _failure;
        if (!Chainloader.PluginInfos.TryGetValue(MagazineCheckContract.PluginGuid, out var info))
        { ClearBinding(); return _failure = null; }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Magazine Check Interrupt has not finished loading");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance)
                && _version == MagazineCheckContract.Normalize(info.Metadata?.Version))
            { _contract.Verify(); VerifyPatches(); return _failure; }
            ClearBinding();
            _contract = MagazineCheckContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version, out _version);
            _plugin = info.Instance;
            _patchAttempted = true;
            // Hook delegate/factory targets before patching their callers can JIT them.
            Patch(_contract.AnimationFinished, nameof(BeforeFinished));
            Patch(_contract.Hide, nameof(BeforeHide));
            Patch(_contract.LateUpdate, nameof(BeforeLateUpdate));
            Patch(_contract.Create, nameof(BeforeCreate), nameof(AfterCreate));
            Patch(_contract.Show, nameof(BeforeShow), nameof(AfterShow));
            Patch(_contract.ShowUtility, nameof(BeforeShowUtility));
            VerifyPatches();
            _failure = null;
            CoreLog.LogDebug("Magazine Check Interrupt: exact-body floating-panel lifecycle ready.");
            if (_version > MagazineCheckContract.ReviewedVersion && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("Magazine Check Interrupt " + _version
                    + " is newer than reviewed 1.1.1; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex) { ClearBinding(); Reject(ex); }
        return _failure;
    }
    private static void Patch(MethodInfo target, string prefix, string postfix = null)
        => Patches.Patch(target, prefix == null ? null : Hook(prefix), postfix == null ? null : Hook(postfix));
    private static HarmonyMethod Hook(string name) => new(typeof(MagazineCheckCompat), name) { priority = Priority.First };
    private static void VerifyPatches()
    {
        void Check(MethodInfo target, string name, bool prefix = true)
        {
            var info = Harmony.GetPatchInfo(target);
            var entries = prefix ? info?.Prefixes : info?.Postfixes;
            var hook = typeof(MagazineCheckCompat).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            if (entries?.Count(p => p.owner == Patches.Id && p.PatchMethod == hook) != 1)
                throw new MissingMethodException("Magazine Check compatibility hook missing: " + name);
        }
        Check(_contract.AnimationFinished, nameof(BeforeFinished)); Check(_contract.Hide, nameof(BeforeHide));
        Check(_contract.LateUpdate, nameof(BeforeLateUpdate)); Check(_contract.Create, nameof(BeforeCreate));
        Check(_contract.Create, nameof(AfterCreate), false); Check(_contract.Show, nameof(BeforeShow));
        Check(_contract.Show, nameof(AfterShow), false); Check(_contract.ShowUtility, nameof(BeforeShowUtility));
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        var refusal = GetRejectionReason();
        if (refusal != null) throw new InvalidOperationException(refusal);
        if (_contract == null) return;
        var world = game?.GameWorld;
        if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, world)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, oldBody) || !BodyReplacementContext.OwnsBody(oldBody)
            || !ReferenceEquals(world?.MainPlayer, oldBody) || !ReferenceEquals(game.LocalPlayer, oldBody))
            throw new InvalidOperationException("Magazine check handover requires Core's exact old body");
        var panel = CurrentPanel();
        if (panel != null && !ParentMatches(panel, oldBody))
            throw new InvalidOperationException("Magazine check floating panel belongs to another physical body");
        Scope.Prepare(game, world, oldBody, panel);
    }
    internal static void CompletePreparation()
    {
        if (_contract == null) return;
        var panel = CurrentPanel();
        if (panel != null && Scope.PreparedBody is Player body && !ParentMatches(panel, body))
            throw new InvalidOperationException("Magazine check panel owner changed during preparation");
        Scope.CompletePreparation(panel);
    }
    internal static void AbortPreparation() => Scope.AbortPreparation();
    private static void Replaced(Player oldBody, Player newBody, string reason)
    {
        if (!Scope.Enabled || !ReferenceEquals(Scope.Body, oldBody)) return;
        try { Synchronize(newBody, true); }
        catch (Exception ex) { Fault(ex); }
    }
    internal static void Tick()
    {
        if (_contract == null) return;
        try
        {
            bool overflow;
            lock (CompletionLock) { overflow = _queueOverflow; _queueOverflow = false; }
            if (overflow) throw new InvalidOperationException("Magazine check completion queue exceeded its bounded capacity");
            if (Scope.Enabled && !ReferenceEquals(Scope.World, World)) RetireWorld(Scope.World as GameWorld);
            if (Scope.Enabled && !_fault) Synchronize(World?.MainPlayer, false);
            DrainCompletions();
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static void Synchronize(Player body, bool published)
    {
        var game = Game; var world = World;
        if (!Scope.Enabled || (BodyReplacementContext.IsCreating && !published) || !ExactCurrent(body, game, world)) return;
        if (!Scope.Frozen && ReferenceEquals(Scope.Body, body)) return;
        var panel = CurrentPanel();
        if (panel != null && Scope.Body is Player old && ParentMatches(panel, old))
            Scope.Adopt(panel, old, game, world);
        else if (panel != null && !ParentMatches(panel, body))
            throw new InvalidOperationException("Magazine check panel changed owner during replacement");
        foreach (var retired in Scope.Bind(body, game, world, true)) Cleanup(retired);
        ClearRetiredCompletions();
        // If another callback created a fresh panel on the confirmed replacement,
        // adopt only that exact parent. Never reparent or borrow an ordinary panel.
        panel = CurrentPanel();
        if (panel != null && ParentMatches(panel, body)) Scope.Adopt(panel, body, game, world);
    }
    private static bool ExactCurrent(Player body, LocalGame game, GameWorld world)
        => body != null && body.HealthController?.IsAlive == true && BodyReplacementContext.OwnsRaid(game, world)
            && BodyReplacementContext.OwnsBody(body) && ReferenceEquals(body, world?.MainPlayer) && ReferenceEquals(body, game?.LocalPlayer);
    private static bool AllowedBody(Player body)
    {
        if (!Scope.OwnsBody(body) && !BodyReplacementContext.OwnsBody(body)) return true;
        return !_fault && !Scope.Frozen && ReferenceEquals(Scope.Body, body) && ExactCurrent(body, Game, World);
    }
    private static bool Allowed(MagazineCheckScope.Panel panel)
        => panel?.Owned != true || (!_fault && Scope.Allows(panel, Game, World, World?.MainPlayer, Game?.LocalPlayer,
            panel.Body is Player body && ExactCurrent(body, Game, World)));
    private static MonoBehaviour CurrentPanel()
    {
        var panel = _contract.Instance.GetValue(null) as MonoBehaviour;
        return panel == null ? null : panel;
    }
    private static bool ParentMatches(MonoBehaviour panel, Player body)
        => panel != null && body != null && panel.GetType() == _contract.PanelType && panel.transform.parent == ((Component)body).transform;
    private static bool CameraMatches(MonoBehaviour panel)
    {
        var camera = CameraManager.Instance?.Camera;
        var cached = _contract.Camera.GetValue(panel) as Transform;
        return camera != null && cached != null && cached == camera.transform;
    }

    private static void BeforeCreate(out Creation __state)
        => __state = new Creation { Body = GamePlayerOwner.MyPlayer, Game = Game, World = World };
    private static void AfterCreate(object __result, Creation __state)
    {
        if (_contract == null || __result is not MonoBehaviour panel || panel == null || __state?.Body == null) return;
        try
        {
            if (!ParentMatches(panel, __state.Body)) return;
            Scope.Observe(panel, __state.Body, __state.Game, __state.World);
            if (Scope.Enabled && !Scope.Frozen && ReferenceEquals(Scope.Body, __state.Body)
                && ExactCurrent(__state.Body, __state.Game, __state.World))
                Scope.Adopt(panel, __state.Body, __state.Game, __state.World);
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static bool BeforeShowUtility(Player.FirearmController __0)
    {
        if (_contract == null) return true;
        try { return AllowedBody(__0 == null ? null : __0.GetComponent<Player>()); }
        catch (Exception ex) { Fault(ex); return !Scope.Enabled; }
    }
    private static bool BeforeShow(object __instance, Player.FirearmController __0, out MagazineCheckScope.Panel __state)
    {
        __state = null;
        if (_contract == null) return true;
        try
        {
            var body = __0 == null ? null : __0.GetComponent<Player>();
            var known = Scope.Find(__instance);
            if (!AllowedBody(body) || !Allowed(known)) return false;
            if (!Scope.OwnsBody(body)) return true;
            if (__instance is not MonoBehaviour panel || !ParentMatches(panel, body) || !CameraMatches(panel))
                throw new InvalidOperationException("Magazine check display does not belong to the current body/camera");
            __state = Scope.Adopt(panel, body, Game, World);
            return true;
        }
        catch (Exception ex) { Fault(ex); return Scope.Find(__instance)?.Owned != true && !Scope.Enabled; }
    }
    private static void AfterShow(object __instance, Player.FirearmController __0, MagazineCheckScope.Panel __state)
    {
        if (__state == null || !Allowed(__state)) return;
        try
        {
            var panel = __instance as MonoBehaviour;
            var magazine = _contract.Magazine.GetValue(__instance) as Transform;
            if (panel != null && panel.gameObject.activeSelf && magazine != null && __0?.GunBaseTransform != null
                && magazine.IsChildOf(__0.GunBaseTransform)) Scope.Shown(__state);
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static bool BeforeLateUpdate(object __instance)
    {
        var entry = Scope.Find(__instance);
        if (entry?.Owned != true) return true;
        try
        {
            if (!Allowed(entry)) return false;
            var panel = __instance as MonoBehaviour;
            if (!ParentMatches(panel, entry.Body as Player) || !CameraMatches(panel)
                || _contract.Magazine.GetValue(panel) is not Transform magazine || magazine == null)
            {
                if (Scope.Retire(entry)) Cleanup(entry);
                return false;
            }
            return true;
        }
        catch (Exception ex) { Fault(ex); return false; }
    }
    private static bool BeforeHide(object __instance)
    {
        var entry = Scope.Find(__instance);
        if (entry?.Owned != true) return true;
        try
        {
            if (!Allowed(entry)) return false;
            if (!ParentMatches(__instance as MonoBehaviour, entry.Body as Player))
                throw new InvalidOperationException("Owned magazine check panel was reparented");
            var ticket = Scope.BeginHide(entry);
            // The closure holds a weak lease ticket, not the Unity panel/player.
            // Upstream chooses the scheduler; this callback never touches Unity.
            _contract.Finish.SetValue(__instance, new Action<Task>(task => Enqueue(ticket, task)));
            return true;
        }
        catch (Exception ex) { Fault(ex); return false; }
    }
    private static bool BeforeFinished(object __instance)
    {
        var entry = Scope.Find(__instance);
        if (entry?.Owned != true) return true;
        // Original delegates captured before adoption and all retired generations
        // are refused. Only the main-thread drain may finish an owned panel.
        return ReferenceEquals(_finishing?.Current(), entry) && Allowed(entry);
    }
    private static void Enqueue(MagazineCheckScope.Ticket ticket, Task task)
    {
        lock (CompletionLock)
        {
            if (ticket?.Current() == null) return;
            if (Completions.Count >= CompletionLimit) { _queueOverflow = true; return; }
            Completions.Enqueue(new Completion { Ticket = ticket, Task = task });
        }
    }
    private static void DrainCompletions()
    {
        Completion[] pending;
        lock (CompletionLock) { pending = Completions.ToArray(); Completions.Clear(); }
        foreach (var item in pending)
        {
            var entry = item.Ticket.Current();
            if (entry == null || !Allowed(entry)) continue;
            if (!ParentMatches(entry.Instance as MonoBehaviour, entry.Body as Player)) continue;
            var previous = _finishing;
            try { _finishing = item.Ticket; _contract.AnimationFinished.Invoke(entry.Instance, new object[] { item.Task }); }
            finally { _finishing = previous; }
        }
    }
    private static void ClearRetiredCompletions()
    {
        lock (CompletionLock)
        {
            var keep = Completions.Where(c => c.Ticket.Current() != null).ToArray();
            Completions.Clear();
            foreach (var item in keep) Completions.Enqueue(item);
        }
    }
    internal static void RetireWorld(GameWorld world)
    {
        if (ReferenceEquals(world, null)) return;
        try
        {
            foreach (var retired in Scope.RetireWorld(world))
            {
                try { Cleanup(retired, worldEnding: true); }
                catch (Exception ex)
                {
                    // Refusing an ambiguously reparented old panel must not stop
                    // the other adapters or current-world teardown/reset.
                    CoreLog.LogWarning("Magazine Check Interrupt: retired panel cleanup skipped: " + ex.GetBaseException().Message);
                }
            }
        }
        finally { ClearRetiredCompletions(); }
    }
    private static void Cleanup(MagazineCheckScope.Panel entry, bool worldEnding = false)
    {
        if (entry?.Owned != true || !entry.Retired || _contract == null) return;
        var panel = entry.Instance as MonoBehaviour;
        if (ReferenceEquals(panel, null)) return;
        if (panel == null)
        {
            // Unity already destroyed this exact owned object: no transform may
            // be inspected, but its stale managed singleton can still be released.
            if (ReferenceEquals(_contract.Instance.GetValue(null), panel)) _contract.Instance.SetValue(null, null);
            return;
        }
        var owner = entry.Body as Player;
        if (worldEnding && !ReferenceEquals(owner, null) && owner == null)
        {
            // Native GameEnd returns pooled players before Core's end callback.
            // The Player component may already be destroyed while this child UI
            // still exists. Keep the same non-destructive behavior as a refused
            // cleanup, but this known teardown order is only a developer detail.
            CoreLog.LogDebug("Magazine Check Interrupt: panel owner already disposed during world teardown; leaving UI to native cleanup.");
            return;
        }
        if (!ParentMatches(panel, owner))
            throw new InvalidOperationException("Refusing to destroy a magazine check panel with a changed parent");
        if (ReferenceEquals(_contract.Instance.GetValue(null), panel)) _contract.Instance.SetValue(null, null);
        panel.gameObject.SetActive(false);
        UnityEngine.Object.Destroy(panel.gameObject);
    }
    private static void Fault(Exception ex)
    {
        _fault = true;
        Reject(ex);
        // Do not leave an owned, invalid panel rendering every frame after failure.
        foreach (var panel in Scope.ActivePanels)
        {
            if (!Scope.Retire(panel)) continue;
            try { Cleanup(panel); } catch { /* Ownership ambiguity never authorizes destruction. */ }
        }
        ClearRetiredCompletions();
    }
    private static void Reject(Exception ex)
    {
        _failure = "Core cannot safely replace the player with Magazine Check Interrupt: " + ex.GetBaseException().Message
            + ". Update or remove Magazine Check Interrupt, then restart. Ordinary raids remain available.";
        if (_lastWarning == _failure) return;
        _lastWarning = _failure; CoreLog.LogWarning(_failure);
    }
    private static void ClearBinding()
    {
        if (_patchAttempted) { _patchAttempted = false; Patches.UnpatchSelf(); }
        _contract = null; _plugin = null; _version = null;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.UI;
using HarmonyLib;
using Redeploy.Core.Api;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Preserve the crosshair's UI while Core changes its physical player and FPS camera.</summary>
internal static class SimpleCrosshairCompat
{
    private sealed class HandsIdentity : IEqualityComparer<Player.AbstractHandsController>
    {
        internal static readonly HandsIdentity Instance = new();
        public bool Equals(Player.AbstractHandsController a, Player.AbstractHandsController b) => ReferenceEquals(a, b);
        public int GetHashCode(Player.AbstractHandsController value) => RuntimeHelpers.GetHashCode(value);
    }
    private sealed class Subscription
    {
        internal Player Body;
        internal int Hands, Sense, Interactions;
        internal readonly Dictionary<Player.AbstractHandsController, int> Aiming = new(HandsIdentity.Instance);
        internal bool Frozen, WasEnabled;
    }
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.SimpleCrosshairCompat");
    private static readonly SimpleCrosshairScope Scope = new();
    private static readonly ConditionalWeakTable<object, Subscription> Subscriptions = new();
    private static readonly ConditionalWeakTable<Player, List<WeakReference<object>>> Observed = new();
    private static SimpleCrosshairContract _contract;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static bool _initialized, _patched, _fault, _newerWarned;
    private static object _bindingPanel;
    private static string _failure, _lastWarning;
    private static WeakReference<EftBattleUIScreen> _screen;
    private static WeakReference<GameWorld> _screenWorld;
    private static float _readySince = -1;
    private static LocalGame Game => Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
    private static GameWorld World => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;

    internal static string GetRejectionReason()
    {
        if (!_initialized) { _initialized = true; RespawnApi.Replaced += Replaced; }
        if (_fault) return _failure;
        if (!Chainloader.PluginInfos.TryGetValue(SimpleCrosshairContract.PluginGuid, out var info))
        { ClearBinding(); return _failure = null; }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Simple Crosshair has not finished loading");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance)
                && _version == SimpleCrosshairContract.Normalize(info.Metadata?.Version))
            { _contract.Verify(); VerifyPatches(); return _failure; }
            ClearBinding();
            _contract = SimpleCrosshairContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version, out _version);
            _plugin = info.Instance; _patched = true;
            Patch(_contract.Register, nameof(BeforeRegister), nameof(AfterRegister));
            Patch(_contract.Unregister, nameof(BeforeUnregister), nameof(AfterUnregister));
            Patch(_contract.HandsChanged, nameof(BeforeHands), nameof(AfterHands));
            Patch(_contract.SenseChanged, nameof(BeforePanelEvent));
            Patch(_contract.InteractionsChanged, nameof(BeforePanelEvent));
            Patch(_contract.AimingChanged, nameof(BeforePanelEvent));
            Patch(_contract.CalculateDynamic, nameof(BeforePanelEvent));
            Patch(_contract.UnregisterPatch, nameof(BeforeUnregisterPatch));
            Patch(_contract.RegisterPatch, nameof(BeforeRegisterPatch));
            Patch(_contract.LookPatch, nameof(BeforeLook));
            Patch(_contract.MovementPatch, nameof(BeforeMovement));
            Patch(_contract.BattlePatch, null, nameof(AfterBattle));
            VerifyPatches(); _failure = null;
            CoreLog.LogDebug("Simple Crosshair: physical-body HUD handover ready.");
            if (_version > SimpleCrosshairContract.ReviewedVersion && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("Simple Crosshair " + _version + " is newer than reviewed 1.4.0; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            if (Scope.Enabled) Fault(ex); // Keep old-body fences installed after a runtime contract fault.
            else { ClearBinding(); Reject(ex); }
        }
        return _failure;
    }
    private static void Patch(MethodInfo target, string prefix, string postfix = null)
        => Patches.Patch(target, prefix == null ? null : Hook(prefix), postfix == null ? null : Hook(postfix));
    private static HarmonyMethod Hook(string name) => new(typeof(SimpleCrosshairCompat), name) { priority = Priority.First };
    private static void VerifyPatches()
    {
        void Check(MethodInfo target, string name, bool prefix = true)
        {
            var entries = prefix ? Harmony.GetPatchInfo(target)?.Prefixes : Harmony.GetPatchInfo(target)?.Postfixes;
            var hook = typeof(SimpleCrosshairCompat).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            if (entries?.Count(p => p.owner == Patches.Id && p.PatchMethod == hook) != 1)
                throw new MissingMethodException("Simple Crosshair compatibility hook missing: " + name);
        }
        Check(_contract.Register, nameof(BeforeRegister)); Check(_contract.Register, nameof(AfterRegister), false);
        Check(_contract.Unregister, nameof(BeforeUnregister)); Check(_contract.Unregister, nameof(AfterUnregister), false);
        Check(_contract.HandsChanged, nameof(BeforeHands)); Check(_contract.HandsChanged, nameof(AfterHands), false);
        Check(_contract.SenseChanged, nameof(BeforePanelEvent)); Check(_contract.InteractionsChanged, nameof(BeforePanelEvent));
        Check(_contract.AimingChanged, nameof(BeforePanelEvent)); Check(_contract.CalculateDynamic, nameof(BeforePanelEvent));
        Check(_contract.UnregisterPatch, nameof(BeforeUnregisterPatch)); Check(_contract.RegisterPatch, nameof(BeforeRegisterPatch));
        Check(_contract.LookPatch, nameof(BeforeLook)); Check(_contract.MovementPatch, nameof(BeforeMovement));
        Check(_contract.BattlePatch, nameof(AfterBattle), false);
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        var refusal = GetRejectionReason();
        if (refusal != null) throw new InvalidOperationException(refusal);
        if (_contract == null) return;
        if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, oldBody)
            || !BodyReplacementContext.OwnsBody(oldBody) || !ReferenceEquals(game.LocalPlayer, oldBody)
            || !ReferenceEquals(game.GameWorld.MainPlayer, oldBody))
            throw new InvalidOperationException("Simple Crosshair requires Core's exact old body");
        var panel = CurrentPanel();
        if (panel != null)
        {
            var cached = _contract.CachedPlayer.GetValue(panel);
            if (cached != null && !ReferenceEquals(cached, oldBody))
                throw new InvalidOperationException("Simple Crosshair belongs to another physical player");
            if (cached != null) ObserveExisting(panel, oldBody);
        }
        Scope.Prepare(game, game.GameWorld, oldBody, panel);
    }
    internal static void CompletePreparation() { if (_contract != null) Scope.CompletePreparation(CurrentPanel()); }
    internal static void AbortPreparation() => Scope.AbortPreparation();
    private static MonoBehaviour CurrentPanel()
    {
        var panel = _contract?.Panel.GetValue(_plugin) as MonoBehaviour;
        return panel == null ? null : panel;
    }

    private static bool BeforeUnregisterPatch(IPlayer __0)
    {
        if (__0 is not Player body) return true;
        try
        {
            bool currentWorld = ReferenceEquals(body.GameWorld, World) && BodyReplacementContext.OwnsRaid(Game, World);
            bool start = currentWorld && BodyReplacementContext.IsCreating
                && ReferenceEquals(BodyReplacementContext.PreviousBody, body) && Scope.Start(body);
            if (start || (currentWorld && ReferenceEquals(Scope.World, World) && Scope.Freeze(body)))
            {
                _readySince = -1;
                var panel = CurrentPanel();
                if (panel != null && ReferenceEquals(_contract.CachedPlayer.GetValue(panel), body))
                { ObserveExisting(panel, body); Scope.AdoptPanel(panel); }
                DetachObserved(body);
                FreezePanel(Scope.Panel as MonoBehaviour);
            }
            // The postfix also runs when native UnregisterPlayer early-returns.
            // Suppress only foreign cleanup for bodies this engine actually owned.
            return !Scope.OwnsBody(body);
        }
        catch (Exception ex) { Fault(ex); return !Scope.OwnsBody(body); }
    }
    private static bool BeforeRegisterPatch(IPlayer __0)
    {
        if (__0 is not Player body || !Scope.Enabled || !Scope.Frozen) return true;
        return !BodyReplacementContext.TryOwnCandidate(body);
    }
    private static bool BeforeLook(Player __0)
        => Scope.OwnsBody(__0)
            ? !_fault && Scope.AllowsBody(__0, Game, World, World?.MainPlayer, Game?.LocalPlayer, ExactCurrent(__0))
            : !BodyReplacementContext.OwnsBody(__0);
    private static bool BeforeMovement()
        => !Scope.Enabled || !ReferenceEquals(Scope.World, World) || (!_fault && !Scope.Frozen && ExactCurrent(World?.MainPlayer));

    private static bool BeforeRegister(object __instance, out bool __state)
    {
        __state = true;
        if (ReferenceEquals(_bindingPanel, __instance)) return true;
        try
        {
            if (Scope.Enabled && ReferenceEquals(Scope.World, World) && Scope.Frozen
                && (BodyReplacementContext.OwnsBody(World?.MainPlayer) || BodyReplacementContext.TryOwnCandidate(World?.MainPlayer)))
            {
                var old = Scope.Panel as MonoBehaviour;
                if (old != null && !ReferenceEquals(old, __instance))
                    throw new InvalidOperationException("Simple Crosshair created a second owned overlay");
                Scope.AdoptPanel(__instance); FreezePanel(__instance as MonoBehaviour);
                return __state = false;
            }
            if (Scope.OwnsPanel(__instance)) return __state = false; // Already bound, never double-subscribe.
            return true;
        }
        catch (Exception ex) { Fault(ex); return __state = !Scope.OwnsPanel(__instance); }
    }
    private static void AfterRegister(object __instance, bool __state)
    {
        if (!__state || _contract?.CachedPlayer.GetValue(__instance) is not Player body) return;
        var sub = Observe(__instance, body);
        sub.Hands++; sub.Sense++; sub.Interactions++;
    }
    private static bool BeforeUnregister(object __instance, out bool __state)
    {
        __state = !Scope.OwnsPanel(__instance);
        if (__state) return true;
        try
        {
            if (Subscriptions.TryGetValue(__instance, out var sub) && Scope.OwnsBody(sub.Body)) Detach(__instance, sub);
        }
        catch (Exception ex) { Fault(ex); }
        return false;
    }
    private static void AfterUnregister(object __instance, bool __state)
    {
        // Observation only, including the upstream += typo. Do not repair an
        // ordinary unowned raid; retain exact counts for a later Core takeover.
        if (!__state || Game == null || !Subscriptions.TryGetValue(__instance, out var sub)) return;
        sub.Hands = Math.Max(0, sub.Hands - 1); sub.Sense = Math.Max(0, sub.Sense - 1); sub.Interactions++;
    }
    private static bool BeforeHands(object __instance, out bool __state) => __state = BeforePanelEvent(__instance);
    private static void AfterHands(object __instance, Player.AbstractHandsController __0, Player.AbstractHandsController __1, bool __state)
    {
        if (!__state || _contract?.CachedPlayer.GetValue(__instance) is not Player body) return;
        var sub = Observe(__instance, body);
        if (!ReferenceEquals(__0, null) && sub.Aiming.TryGetValue(__0, out int old)) sub.Aiming[__0] = Math.Max(0, old - 1);
        if (!ReferenceEquals(__1, null)) { sub.Aiming.TryGetValue(__1, out int count); sub.Aiming[__1] = count + 1; }
    }
    private static bool BeforePanelEvent(object __instance)
        => ReferenceEquals(_bindingPanel, __instance) || !Scope.OwnsPanel(__instance) || (!_fault
            && Scope.AllowsPanel(__instance, Game, World, World?.MainPlayer, Game?.LocalPlayer, ExactCurrent(World?.MainPlayer)));
    private static void AfterBattle(object __0)
    {
        if (__0 is not EftBattleUIScreen screen || screen == null) return;
        _screen = new WeakReference<EftBattleUIScreen>(screen);
        _screenWorld = World == null ? null : new WeakReference<GameWorld>(World);
    }
    private static bool ScreenBelongsTo(GameWorld world)
        => _screenWorld?.TryGetTarget(out var captured) == true && ReferenceEquals(captured, world);

    private static Subscription Observe(object panel, Player body)
    {
        var sub = Subscriptions.GetValue(panel, _ => new Subscription());
        if (!ReferenceEquals(sub.Body, body))
        {
            sub.Body = body;
            Observed.GetValue(body, _ => new List<WeakReference<object>>()).Add(new WeakReference<object>(panel));
        }
        return sub;
    }
    private static void ObserveExisting(object panel, Player body)
    {
        if (Subscriptions.TryGetValue(panel, out _)) return;
        var sub = Observe(panel, body); sub.Hands = sub.Sense = sub.Interactions = 1;
        if (body.HandsController != null) sub.Aiming[body.HandsController] = 1;
    }
    private static void DetachObserved(Player body)
    {
        if (!Scope.OwnsBody(body) || !Observed.TryGetValue(body, out var panels)) return;
        foreach (var weak in panels.ToArray())
            if (weak.TryGetTarget(out var panel) && Subscriptions.TryGetValue(panel, out var sub) && ReferenceEquals(sub.Body, body))
                Detach(panel, sub);
        panels.RemoveAll(weak => !weak.TryGetTarget(out _));
    }
    private static void Detach(object panel, Subscription sub)
    {
        var body = sub.Body;
        if (ReferenceEquals(body, null) || !Scope.OwnsBody(body)) return;
        // Event removal is managed and remains valid even if Unity destroyed
        // this panel. No transform/gameObject lookup is used for dead wrappers.
        Remove(_contract.HandsEvent, _contract.HandsChanged, panel, body, ref sub.Hands);
        Remove(_contract.SenseEvent, _contract.SenseChanged, panel, body, ref sub.Sense);
        Remove(_contract.InteractionsEvent, _contract.InteractionsChanged, panel, body, ref sub.Interactions);
        foreach (var item in sub.Aiming.ToArray())
        {
            int count = item.Value;
            Remove(_contract.AimingEvent, _contract.AimingChanged, panel, item.Key, ref count);
            sub.Aiming[item.Key] = count;
        }
        sub.Aiming.Clear();
        if (ReferenceEquals(_contract.CachedPlayer.GetValue(panel), body))
        {
            _contract.CachedPlayer.SetValue(panel, null);
            _contract.CachedFirearm.SetValue(panel, null);
            _contract.CachedCamera.SetValue(panel, null);
        }
    }
    private static void Remove(EventInfo @event, MethodInfo method, object panel, object owner, ref int count)
    {
        if (count <= 0) return;
        var handler = SimpleCrosshairContract.Handler(@event, method, panel);
        while (count > 0) { @event.RemoveEventHandler(owner, handler); count--; }
    }
    private static void FreezePanel(MonoBehaviour panel)
    {
        if (panel == null || !Scope.OwnsPanel(panel)) return;
        var sub = Subscriptions.GetValue(panel, _ => new Subscription());
        if (!sub.Frozen) { sub.WasEnabled = panel.enabled; sub.Frozen = true; }
        _contract.SetReason.Invoke(panel, new object[] { "redeployBodyReplacement", true });
        panel.enabled = false;
    }

    private static bool ExactCurrent(Player body)
        => body != null && body.HealthController?.IsAlive == true && BodyReplacementContext.OwnsRaid(Game, World)
            && BodyReplacementContext.OwnsBody(body) && ReferenceEquals(body, World?.MainPlayer)
            && ReferenceEquals(body, Game?.LocalPlayer) && World.RegisteredPlayers.Contains(body)
            && Singleton<PlayerCameraController>.Instantiated && ReferenceEquals(Singleton<PlayerCameraController>.Instance.Player, body)
            && Camera.main != null && ReferenceEquals(Camera.main, CameraManager.Instance?.Camera);
    private static void Replaced(Player oldBody, Player body, string reason)
    {
        if (!Scope.Enabled || !ReferenceEquals(Scope.Body, oldBody)) return;
        try { Synchronize(body, true); } catch (Exception ex) { Fault(ex); }
    }
    internal static void Tick()
    {
        // Engine failure can happen before the first native unregister. That
        // read-only preparation acquired no HUD/body authority and must not
        // poison a later retry after EndAttempt.
        if (!BodyReplacementContext.IsCreating && Scope.PreparedBody != null) Scope.AbortPreparation();
        if (_contract == null || !Scope.Enabled || _fault) return;
        try
        {
            if (!ReferenceEquals(Scope.World, World)) { RetireWorld(Scope.World as GameWorld); return; }
            Synchronize(World?.MainPlayer, false);
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static void Synchronize(Player body, bool published)
    {
        if (!Scope.Enabled || (BodyReplacementContext.IsCreating && !published) || !ExactCurrent(body)) return;
        var panel = CurrentPanel();
        if (panel == null && ScreenBelongsTo(World) && _screen?.TryGetTarget(out var screen) == true && screen != null)
        {
            _contract.Attach.Invoke(_plugin, new object[] { screen });
            panel = CurrentPanel();
        }
        if (panel == null)
        {
            if (_readySince < 0) _readySince = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - _readySince > 5f)
                throw new InvalidOperationException("Simple Crosshair did not recreate its battle overlay after replacement");
            return;
        }
        _readySince = -1;
        if (!Scope.Frozen && ReferenceEquals(Scope.Body, body) && ReferenceEquals(Scope.Panel, panel)
            && ReferenceEquals(_contract.CachedPlayer.GetValue(panel), body))
        {
            // Core's later camera finalization must not leave dynamic aiming on
            // the previous FPS camera. This changes only the owned panel cache.
            if (!ReferenceEquals(_contract.CachedCamera.GetValue(panel), Camera.main)) _contract.CachedCamera.SetValue(panel, Camera.main);
            return;
        }
        var cached = _contract.CachedPlayer.GetValue(panel) as Player;
        if (!ReferenceEquals(cached, null) && !Scope.OwnsBody(cached) && !ReferenceEquals(cached, body))
            throw new InvalidOperationException("Simple Crosshair acquired an unrelated body during handover");
        if (!ReferenceEquals(cached, null))
        {
            ObserveExisting(panel, cached);
            if (Subscriptions.TryGetValue(panel, out var previous) && Scope.OwnsBody(previous.Body)) Detach(panel, previous);
        }
        Scope.Bind(Game, World, body, panel, true);
        var sub = Subscriptions.GetValue(panel, _ => new Subscription());
        bool enable = !sub.Frozen || sub.WasEnabled;
        _bindingPanel = panel;
        try { _contract.Register.Invoke(panel, null); }
        finally { _bindingPanel = null; }
        if (!ReferenceEquals(_contract.CachedPlayer.GetValue(panel), body) || !ReferenceEquals(_contract.CachedCamera.GetValue(panel), Camera.main))
            throw new InvalidOperationException("Simple Crosshair did not bind the replacement body/camera");
        _contract.SetReason.Invoke(panel, new object[] { "redeployBodyReplacement", false });
        panel.enabled = enable; sub.Frozen = false;
    }
    internal static void RetireWorld(GameWorld world)
    {
        if (!Scope.Enabled || !ReferenceEquals(Scope.World, world)) { Scope.RetireWorld(world); return; }
        try
        {
            if (Scope.Body is Player body) DetachObserved(body);
            var panel = Scope.Panel as MonoBehaviour;
            if (panel != null && ReferenceEquals(CurrentPanel(), panel))
                _contract.Destroy.Invoke(_plugin, null); // Exact old panel only; never a newer world's global.
        }
        catch (Exception ex) { CoreLog.LogWarning("Simple Crosshair cleanup: " + ex.GetBaseException().Message); }
        finally
        {
            Scope.RetireWorld(world);
            if (ScreenBelongsTo(world)) { _screen = null; _screenWorld = null; }
            _readySince = -1;
        }
    }
    private static void Fault(Exception ex)
    {
        _fault = true; Reject(ex);
        try { FreezePanel(Scope.Panel as MonoBehaviour); } catch { }
    }
    private static void Reject(Exception ex)
    {
        _failure = "Core cannot safely replace the player with Simple Crosshair: " + ex.GetBaseException().Message
            + ". Update or remove Simple Crosshair, then restart. Ordinary raids remain available.";
        if (_lastWarning == _failure) return;
        _lastWarning = _failure; CoreLog.LogWarning(_failure);
    }
    private static void ClearBinding()
    {
        if (_patched) { _patched = false; Patches.UnpatchSelf(); }
        _contract = null; _plugin = null; _version = null;
    }
}

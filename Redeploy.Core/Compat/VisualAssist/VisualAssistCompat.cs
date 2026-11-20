using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Redeploy.Core.Api;
using UnityEngine;
using Object = UnityEngine.Object;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Follow confirmed physical replacements without changing upstream throw calculations.</summary>
internal static class VisualAssistCompat
{
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.VisualAssistCompat");
    private static readonly VisualAssistScope Scope = new();
    private sealed class ArcState
    {
        internal Player Body;
        internal VisualAssistResources Resources;
        internal bool Hidden;
    }
    private static readonly ConditionalWeakTable<Component, ArcState> Arcs = new();
    private static readonly ConditionalWeakTable<Player, List<VisualAssistResources>> Captured = new();
    private static readonly List<(MethodInfo Target, MethodInfo Hook, bool Postfix)> Hooks = new();
    private static readonly MethodInfo Unregister = typeof(GameWorld).GetMethod(nameof(GameWorld.UnregisterPlayer), new[] { typeof(IPlayer) });
    private static VisualAssistContract _contract;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static ConfigEntry<bool> _enabled;
    private static object _emptyThrow;
    private static Component _publishedArc;
    // Valid only around our own synchronous AddComponent; not a body/profile heuristic.
    private static Player _creatingBody;
    private static Component _creatingArc;
    private static bool _creationAwakeComplete;
    private static bool _subscribed, _fault, _newerWarned, _binding;
    private static string _failure, _warning;
    private static LocalGame Game => Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
    private static GameWorld World => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;

    internal static string GetRejectionReason()
    {
        if (_fault) return _failure;
        if (!Chainloader.PluginInfos.TryGetValue(VisualAssistContract.PluginGuid, out var info))
        {
            if (Scope.Enabled) { Fault(new InvalidOperationException("VisualAssist disappeared during an owned raid")); return _failure; }
            Clear(); return _failure = null;
        }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("VisualAssist has not finished loading");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance)
                && _version == VisualAssistContract.Normalize(info.Metadata?.Version))
            { _contract.Verify(); VerifyHooks(); return _failure; }
            if (Scope.Enabled) throw new InvalidOperationException("VisualAssist changed during an owned raid");
            Clear();
            _contract = VisualAssistContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version, out _version);
            _plugin = info.Instance;
            _enabled = (ConfigEntry<bool>)_contract.Enabled.GetValue(null);
            _emptyThrow = Activator.CreateInstance(_contract.Throw.FieldType);
            Patch(Unregister, nameof(BeforeUnregister));
            Patch(_contract.Destroy, nameof(BeforeDestroy));
            Patch(_contract.Awake, nameof(BeforeAwake));
            Patch(_contract.Awake, nameof(AfterAwake), postfix: true);
            Patch(_contract.Update, nameof(BeforeUpdate));
            Patch(_contract.DeadHook, nameof(BeforeNativeDispose));
            Patch(_contract.DisposeHook, nameof(BeforeNativeDispose));
            VerifyHooks();
            if (!_subscribed) { _subscribed = true; RespawnApi.Replaced += Replaced; }
            _failure = null;
            if (_version > VisualAssistContract.Reviewed && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("VisualAssist " + _version + " is newer than reviewed 1.2.0; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex) { if (Scope.Enabled) Fault(ex); else { Clear(); Reject(ex); } }
        return _failure;
    }
    private static void Patch(MethodInfo target, string name, bool postfix = false)
    {
        var hook = typeof(VisualAssistCompat).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
        if (Hooks.Any(h => h.Target == target && h.Hook == hook)) return;
        // Record before Patch, so partial installation can still be removed without a global walk.
        Hooks.Add((target, hook, postfix));
        var patch = new HarmonyMethod(hook) { priority = Priority.First };
        Patches.Patch(target, prefix: postfix ? null : patch, postfix: postfix ? patch : null);
    }
    private static void VerifyHooks()
    {
        foreach (var (target, hook, postfix) in Hooks)
            if ((postfix ? Harmony.GetPatchInfo(target)?.Postfixes : Harmony.GetPatchInfo(target)?.Prefixes)
                ?.Count(p => p.owner == Patches.Id && p.PatchMethod == hook) != 1)
                throw new MissingMethodException("VisualAssist compatibility hook missing: " + hook.Name);
    }
    internal static void Prepare(LocalGame game, LocalPlayer body)
    {
        if (_contract == null) return;
        if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, body) || !BodyReplacementContext.OwnsBody(body)
            || !ReferenceEquals(game.LocalPlayer, body) || !ReferenceEquals(game.GameWorld.MainPlayer, body))
            throw new InvalidOperationException("VisualAssist requires Core's exact old body");
        var instance = _contract.Instance.GetValue(null) as Component;
        if (instance != null && (!ReferenceEquals(_contract.Player.GetValue(instance), body)
            || !ReferenceEquals(instance.gameObject, body.gameObject)))
            throw new InvalidOperationException("VisualAssist singleton belongs to another physical body");
        foreach (var arc in body.gameObject.GetComponents(_contract.ArcType))
            _ = new VisualAssistResources(_contract, arc, body); // Read-only validation before teardown.
        var entry = game.GameWorld.GetType().GetMethod(nameof(GameWorld.UnregisterPlayer), new[] { typeof(IPlayer) });
        entry = entry?.DeclaringType?.GetMethod(nameof(GameWorld.UnregisterPlayer), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            null, new[] { typeof(IPlayer) }, null);
        if (entry == null || entry.ReturnType != typeof(void) || entry.IsStatic || entry.GetMethodBody() == null || entry.GetBaseDefinition() != Unregister)
            throw new MissingMethodException("VisualAssist cannot verify this world's unregister entry");
        Patch(entry, nameof(BeforeUnregister)); VerifyHooks();
        Scope.Prepare(game, game.GameWorld, body);
    }
    internal static void AbortPreparation() => Scope.Abort();
    private static void BeforeUnregister(GameWorld __instance, IPlayer __0)
    {
        if (__0 is not Player body || !BodyReplacementContext.IsCreating || !ReferenceEquals(__instance, World)
            || !BodyReplacementContext.OwnsRaid(Game, __instance) || !BodyReplacementContext.OwnsBody(body)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, body)) return;
        try { if (Scope.Start(Game, __instance, body)) CleanupBody(body); }
        catch (Exception ex) { Fault(ex); }
    }
    private static bool InitialLocal(Player body)
        => body != null && body.IsYourPlayer && World != null && World is not HideoutGameWorld && Game != null
            && ReferenceEquals(Game.GameWorld, World) && ReferenceEquals(body.GameWorld, World)
            && ReferenceEquals(Game.LocalPlayer, body) && ReferenceEquals(World.MainPlayer, body);
    private static bool ExactCurrent(Player body, bool requireRegistration = true)
        => InitialLocal(body) && body.HealthController?.IsAlive == true && BodyReplacementContext.OwnsRaid(Game, World)
            && BodyReplacementContext.OwnsBody(body) && ReferenceEquals(GamePlayerOwner.MyPlayer, body)
            && (!requireRegistration || World.RegisteredPlayers.Contains(body));
    private static ArcState Track(Component arc, Player body)
    {
        if (Arcs.TryGetValue(arc, out var state))
        {
            if (!ReferenceEquals(state.Body, body)) throw new InvalidOperationException("VisualAssist arc owner changed");
            return state;
        }
        state = new ArcState { Body = body, Resources = new VisualAssistResources(_contract, arc, body) };
        Arcs.Add(arc, state); return state;
    }
    private static void BeforeDestroy(Component __instance)
    {
        try
        {
            if (ReferenceEquals(__instance, _creatingArc) && _creatingBody != null)
            {
                ReleaseSingleton(__instance);
                new VisualAssistResources(_contract, __instance, _creatingBody, unassignedCreation: true, ownMaterials: _creationAwakeComplete).Release();
                return;
            }
            var body = _contract.Player.GetValue(__instance) as Player;
            if (!Scope.Owns(body) && !InitialLocal(body)) return;
            var resources = Track(__instance, body).Resources;
            if (Scope.Owns(body)) { ReleaseSingleton(__instance); resources.Release(); }
            else
            {
                // Original death destroys the component before Core can prepare. Capture only;
                // a later confirmed unregister adopts the resources. Ordinary raids stay native.
                Captured.GetValue(body, _ => new List<VisualAssistResources>()).Add(resources);
            }
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static void BeforeAwake(Component __instance)
    {
        if (_creatingBody == null || _creatingArc != null || __instance.GetType() != _contract.ArcType
            || !ReferenceEquals(__instance.gameObject, _creatingBody.gameObject)) return;
        // AddComponent may throw (or Unity may swallow an Awake exception) before returning
        // the component to us. Capture that exact allocation before native initialization.
        _creatingArc = __instance;
    }
    private static void AfterAwake(Component __instance, bool __runOriginal)
    {
        if (__runOriginal && ReferenceEquals(__instance, _creatingArc)) _creationAwakeComplete = true;
    }
    private static void CleanupCreation(Player body)
    {
        var arc = _creatingArc;
        if (arc == null || !ReferenceEquals(body, _creatingBody)
            || !ReferenceEquals(arc.gameObject, body.gameObject)) return;
        // Only this exact newly allocated arc may have an unassigned owner. Never relax
        // the regular cleanup path for existing or foreign components.
        var resources = new VisualAssistResources(_contract, arc, body, unassignedCreation: true, ownMaterials: _creationAwakeComplete);
        ReleaseSingleton(arc); resources.Release(); Object.DestroyImmediate(arc);
    }
    private static bool BeforeNativeDispose(Player __0)
    {
        if (!Scope.Owns(__0)) return true;
        try { CleanupBody(__0); } catch (Exception ex) { Fault(ex); }
        // Upstream Release(oldArc) unconditionally clears the shared singleton, even if new.
        return false;
    }
    private static bool BeforeUpdate(Component __instance)
    {
        try
        {
            // A frame update does no reflection, allocation, patch inspection or scene search.
            if (!Arcs.TryGetValue(__instance, out var state) || !Scope.Owns(state.Body)) return true;
            if (!_fault && !Scope.Frozen && ReferenceEquals(Scope.Body, state.Body) && ExactCurrent(state.Body, false)
                && ReferenceEquals(_publishedArc, __instance) && _enabled.Value)
            { state.Hidden = false; return true; }
            Hide(__instance, state.Body); return false;
        }
        catch (Exception ex) { Fault(ex); return false; }
    }
    private static void Hide(Component arc, Player body)
    {
        var state = Track(arc, body);
        if (state.Hidden) return;
        state.Resources.Hide();
        _contract.Throw.SetValue(arc, _emptyThrow);
        state.Hidden = true;
    }
    private static void ReleaseSingleton(Component arc)
    {
        if (ReferenceEquals(_contract.Instance.GetValue(null), arc)) _contract.Instance.SetValue(null, null);
        if (ReferenceEquals(_publishedArc, arc)) _publishedArc = null;
    }
    private static void CleanupBody(Player body)
    {
        if (body != null)
            foreach (var arc in body.gameObject.GetComponents(_contract.ArcType))
            {
                Hide(arc, body); ReleaseSingleton(arc);
                Object.DestroyImmediate(arc); // OnDestroy releases exact resources, then native config handlers.
            }
        if (Captured.TryGetValue(body, out var captured))
        {
            foreach (var resources in captured) resources.Release();
            Captured.Remove(body);
        }
    }
    private static void Replaced(Player oldBody, Player body, string reason)
    {
        if (!Scope.Enabled || !ReferenceEquals(Scope.Body, oldBody)) return;
        try { Synchronize(body, true); } catch (Exception ex) { Fault(ex); }
    }
    internal static void Tick()
    {
        if (!BodyReplacementContext.IsCreating && Scope.PreparedBody != null) Scope.Abort();
        if (_contract == null || !Scope.Enabled || _fault || _binding) return;
        try
        {
            if (!ReferenceEquals(Scope.World, World)) { RetireWorld(Scope.World as GameWorld); return; }
            if (!Scope.Frozen && ReferenceEquals(Scope.Body, World?.MainPlayer)) return;
            Synchronize(World?.MainPlayer, false);
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static void Synchronize(Player body, bool published)
    {
        if ((!published && BodyReplacementContext.IsCreating) || !ExactCurrent(body)
            || !ReferenceEquals(Scope.Game, Game) || !ReferenceEquals(Scope.World, World)) return;
        if (!Scope.Frozen && ReferenceEquals(Scope.Body, body)) return;
        var instance = _contract.Instance.GetValue(null) as Component;
        if (instance != null && (!Scope.Owns(_contract.Player.GetValue(instance))
            || !ReferenceEquals(instance.gameObject, ((Player)_contract.Player.GetValue(instance)).gameObject)))
            throw new InvalidOperationException("VisualAssist singleton changed outside the owned replacement");
        if (body.gameObject.GetComponents(_contract.ArcType).Length != 0)
            throw new InvalidOperationException("VisualAssist replacement already has an unexpected arc");
        _binding = true;
        try
        {
            CleanupBody(Scope.Body as Player);
            // Ownership is committed before Awake/OnDestroy; any partial creation is ours to clean.
            Scope.Bind(Game, World, body, true);
            _creatingBody = body; _creatingArc = null; _creationAwakeComplete = false;
            var arc = body.gameObject.AddComponent(_contract.ArcType);
            if (arc == null || !ReferenceEquals(arc, _creatingArc) || !_creationAwakeComplete
                || _contract.Line.GetValue(arc) is not LineRenderer line || line == null
                || _contract.Sphere.GetValue(arc) is not GameObject sphere || sphere == null
                || _contract.SphereRenderer.GetValue(arc) is not Renderer renderer || renderer == null)
                throw new InvalidOperationException("VisualAssist arc initialization did not complete");
            _contract.Player.SetValue(arc, body);
            Hide(arc, body);
            _contract.Instance.SetValue(null, arc);
            _publishedArc = arc;
            CoreLog.LogDebug("VisualAssist: grenade preview follows the confirmed replacement body.");
        }
        catch
        {
            CleanupCreation(body);
            throw;
        }
        finally { _creatingArc = null; _creatingBody = null; _creationAwakeComplete = false; _binding = false; }
    }
    internal static void RetireWorld(GameWorld world)
    {
        if (Scope.Enabled && ReferenceEquals(Scope.World, world))
        {
            try { CleanupBody(Scope.Body as Player); } catch (Exception ex) { Fault(ex); }
        }
        Scope.Retire(world);
    }
    private static void Fault(Exception ex) { _fault = true; Reject(ex); }
    private static void Reject(Exception ex)
    {
        _failure = "Core cannot safely replace the player with VisualAssist: " + ex.GetBaseException().Message
            + ". Update or remove VisualAssist, then restart. Ordinary raids remain available.";
        if (_warning == _failure) return;
        _warning = _failure; CoreLog.LogWarning(_failure);
    }
    private static void Clear()
    {
        foreach (var (target, hook, _) in Hooks) Patches.Unpatch(target, hook);
        Hooks.Clear(); _contract = null; _plugin = null; _version = null; _enabled = null; _emptyThrow = null;
    }
}

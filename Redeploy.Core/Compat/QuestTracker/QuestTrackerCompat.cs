using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using EFT;
using EFT.Quests;
using HarmonyLib;
using Redeploy.Core.Api;
using UnityEngine;

namespace Redeploy.Core.Compat;

/// <summary>Rebuild only body-bound quest caches; the existing panel and global tracking preferences survive.</summary>
internal static class QuestTrackerCompat
{
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.QuestTrackerCompat");
    private static readonly StandaloneHudScope Scope = new();
    private static readonly ConditionalWeakTable<object, object> RetiredComponents = new();
    private static QuestTrackerContract _contract;
    private static BaseUnityPlugin _plugin;
    private static System.Version _version;
    private static Component _component;
    private static bool _initialized, _fault, _warned, _binding;
    private static string _failure;

    internal static string GetRejectionReason()
    {
        if (!_initialized) { _initialized = true; RespawnApi.Replaced += Replaced; }
        if (_fault) return _failure;
        if (!Chainloader.PluginInfos.TryGetValue(QuestTrackerContract.PluginGuid, out var info))
        {
            if (Scope.Owned) return Reject(new InvalidOperationException("plugin disappeared during an owned raid"), true);
            Clear(); return null;
        }
        try
        {
            var version = StandaloneHudContract.Normalize(info.Metadata?.Version);
            if (info.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance) && _version == version)
            { Verify(); return _failure; }
            if (Scope.Owned) throw new InvalidOperationException("plugin changed during an owned raid");
            Clear();
            _contract = QuestTrackerContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version);
            _plugin = info.Instance; _version = version;
            foreach (var method in new[] { _contract.Update, _contract.Cache, _contract.Changed })
                Patches.Patch(method, new HarmonyMethod(typeof(QuestTrackerCompat), nameof(BeforeCallback)) { priority = Priority.First });
            Verify();
            if (version > QuestTrackerContract.ReviewedVersion && !_warned)
            {
                _warned = true; CoreLog.LogWarning("Quest Tracker " + version + " is newer than reviewed 1.7.0; APIs match, native compatibility is unverified.");
            }
            _failure = null;
        }
        catch (Exception ex) { Reject(ex, Scope.Owned); if (!_fault) Clear(); }
        return _failure;
    }
    private static void Verify()
    {
        var hook = typeof(QuestTrackerCompat).GetMethod(nameof(BeforeCallback), BindingFlags.Static | BindingFlags.NonPublic);
        foreach (var method in new[] { _contract.Update, _contract.Cache, _contract.Changed })
            if (Harmony.GetPatchInfo(method)?.Prefixes.Count(p => p.owner == Patches.Id && p.PatchMethod == hook) != 1)
                throw new MissingMethodException("Quest Tracker callback fence is missing");
    }
    internal static void Prepare(LocalGame game, LocalPlayer body)
    {
        var failure = GetRejectionReason();
        if (failure != null) throw new InvalidOperationException(failure);
        if (_contract == null) return;
        var component = game?.GameWorld?.GetComponent(_contract.Component);
        // Before OnGameStarted the mod has no component; its ordinary initialization will see the new body.
        if (component == null) return;
        if (!ReferenceEquals(_contract.World.GetValue(component), game.GameWorld)
            || !ReferenceEquals(_contract.Game.GetValue(component), game)
            || !ReferenceEquals(_contract.Player.GetValue(component), body)
            || !ReferenceEquals(_contract.Controller.GetValue(component), body.QuestController)
            || body.QuestController == null || body.Profile?.QuestsData == null
            || _contract.Panel.GetValue(component) is not UnityEngine.Object panel || panel == null)
            throw new InvalidOperationException("Quest Tracker caches do not belong to Core's current body");
        Scope.Prepare(game, body); _component = component;
    }
    internal static void AbortPreparation()
    {
        Scope.Abort();
        if (!Scope.Active) _component = null;
    }
    private static bool BeforeCallback(object __instance)
    {
        if (RetiredComponents.TryGetValue(__instance, out _)) return false;
        if (!ReferenceEquals(_component, __instance)) return true;
        if (_binding) return true;
        Tick();
        return !_fault && !Scope.BlockCallbacks;
    }
    private static void Replaced(Player oldBody, Player body, string reason)
    {
        if (!ReferenceEquals(oldBody, Scope.Prepared ?? Scope.Body)) return;
        try { Bind(body, true); } catch (Exception ex) { Reject(ex, true); }
    }
    internal static void Tick()
    {
        if (_contract == null || !Scope.Owned || _binding || _fault) return;
        try
        {
            if (!ReferenceEquals(Scope.World, StandaloneHudScope.CurrentWorld)) { RetireWorld(Scope.World); return; }
            Bind(Scope.World.MainPlayer, false);
            if (!BodyReplacementContext.IsCreating && Scope.Prepared != null && Scope.Exact(Scope.Prepared)) AbortPreparation();
        }
        catch (Exception ex) { Reject(ex, true); }
    }
    private static void Bind(Player player, bool published)
    {
        if (!Scope.CanBind(player, published)) return;
        var body = (LocalPlayer)player;
        if (_component == null || !ReferenceEquals(_component.gameObject, Scope.World.gameObject)
            || !ReferenceEquals(_contract.World.GetValue(_component), Scope.World) || body.QuestController == null
            || body.Profile?.QuestsData == null)
            throw new InvalidOperationException("replacement has no usable quest controller or tracker component");
        var map = new List<Quest>();
        var location = (string)_contract.Location.GetValue(_component);
        foreach (var data in body.Profile.QuestsData)
        {
            if (data?.Template == null || ((int)data.Status != 2 && (int)data.Status != 3 && (int)data.Status != 7)) continue;
            if (!(bool)_contract.SameMap.Invoke(_component, new object[] { data.Template.LocationId, location })) continue;
            var quest = body.QuestController.Quests.GetConditional(data.Id);
            if (quest != null) map.Add(quest);
        }
        _binding = true;
        try
        {
            _contract.Player.SetValue(_component, body);
            _contract.Controller.SetValue(_component, body.QuestController);
            _contract.MapQuests.SetValue(_component, map);
            // CacheQuests reuses the current tracking preferences and the same panel. These
            // are global settings/tracking events, not old-controller subscriptions: leave them attached.
            // In particular never Load(ScavController), which would replace/save the user's PMC preferences.
            _contract.Cache.Invoke(_component, null);
            _contract.Changed.Invoke(_component, null); // Seed progress hash without a false objective-completion popup.
            Scope.Bind(body);
            CoreLog.LogDebug("Quest Tracker: existing panel follows the confirmed replacement quest controller.");
        }
        finally { _binding = false; }
    }
    internal static void RetireWorld(GameWorld world)
    {
        if (!ReferenceEquals(Scope.World, world)) return;
        if (!ReferenceEquals(_component, null)) RetiredComponents.GetValue(_component, _ => new object());
        _component = null; Scope.Retire(world);
        // OnDestroy still runs normally and removes the mod's original global event subscriptions.
    }
    private static string Reject(Exception ex, bool fault)
    {
        _fault |= fault;
        var message = "Core cannot safely replace the player with Quest Tracker: " + ex.GetBaseException().Message
            + ". Update or remove Quest Tracker, then restart. Ordinary raids remain available.";
        if (message != _failure) CoreLog.LogWarning(message);
        return _failure = message;
    }
    private static void Clear() { Patches.UnpatchSelf(); _contract = null; _plugin = null; _version = null; }
}

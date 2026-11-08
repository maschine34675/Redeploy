using System;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using Redeploy.Core.Api;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal static class SkillsExtendedCompat
{
    internal const string PluginGuid = "com.cj.skillsextended";
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.SkillsExtendedCompat");
    private static readonly StandaloneHudScope Scope = new();
    private static SkillsExtendedContract _contract;
    private static object _plugin;
    private static Version _version;
    private static bool _patched, _subscribed, _newerWarned, _recovery;
    private static string _failure;
    private static SkillsExtendedContract.Events _oldEvents;
    private static SkillsExtendedContract.Events _liveEvents;
    private static MonoBehaviour _hacking, _picking;

    internal static string GetRejectionReason()
    {
        if (FikaCompatCheck.IsFikaLoaded || !Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info))
        {
            try { RetireWorld(Scope.World); ClearBinding(); return _failure = null; }
            catch (Exception cleanup) { Fault(cleanup); return _failure; }
        }
        var version = StandaloneHudContract.Normalize(info?.Metadata?.Version);
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            if (ReferenceEquals(info.Instance, _plugin) && version == _version)
            {
                if (_failure != null && _contract == null && _patched) ClearPatches();
                return _failure;
            }
            if (Scope.Owned) throw new InvalidOperationException("plugin changed during an owned replacement");
            ClearBinding();
            _plugin = info.Instance;
            _version = version;
            var contract = new SkillsExtendedContract(info.Instance.GetType(), info.Metadata?.GUID, version);
            _patched = true;
            Patches.Patch(contract.Nato.Method, prefix: Prefix(nameof(BeforeMastery)));
            Patches.Patch(contract.Eastern.Method, prefix: Prefix(nameof(BeforeMastery)));
            Patches.Patch(contract.Medical.Method, prefix: Prefix(nameof(BeforeMedical)));
            Patches.Patch(contract.Started, postfix: new HarmonyMethod(typeof(SkillsExtendedCompat), nameof(AfterStarted)));
            Verify(contract);
            _contract = contract;
            if (!_subscribed)
            {
                RespawnApi.Replaced += Replaced;
                RespawnApi.ReplacementFailed += Failed;
                _subscribed = true;
            }
            _failure = null;
            if (version > SkillsExtendedContract.Reviewed && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("Skills Extended " + version + " is newer than reviewed 3.1.4; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            Fault(ex);
            if (!Scope.Owned)
            {
                _contract = null;
                try { ClearPatches(); } catch (Exception cleanup) { Fault(cleanup); }
                _plugin = info?.Instance;
                _version = version;
            }
        }
        return _failure;
    }

    private static HarmonyMethod Prefix(string name) =>
        new(typeof(SkillsExtendedCompat), name) { priority = Priority.First };

    private static void Verify(SkillsExtendedContract c)
    {
        foreach (var method in new[] { c.Nato.Method, c.Eastern.Method, c.Medical.Method })
        {
            var prefix = typeof(SkillsExtendedCompat).GetMethod(method == c.Medical.Method ? nameof(BeforeMedical) : nameof(BeforeMastery),
                BindingFlags.NonPublic | BindingFlags.Static);
            if (Harmony.GetPatchInfo(method)?.Prefixes.Count(p => p.owner == Patches.Id && p.PatchMethod == prefix) != 1)
                throw new MissingMethodException("Skills Extended XP guard is missing");
        }
        var started = typeof(SkillsExtendedCompat).GetMethod(nameof(AfterStarted), BindingFlags.NonPublic | BindingFlags.Static);
        if (Harmony.GetPatchInfo(c.Started)?.Postfixes.Count(p => p.owner == Patches.Id && p.PatchMethod == started) != 1)
            throw new MissingMethodException("Skills Extended startup observer is missing");
    }

    private static void ClearPatches()
    {
        if (_patched) { Patches.UnpatchSelf(); _patched = false; }
    }

    private static void ClearBinding()
    {
        _contract = null;
        _plugin = null;
        _version = null;
        ClearPatches();
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        if (_contract == null) return;
        Verify(_contract);
        Scope.Prepare(game, oldBody);
        _oldEvents = new SkillsExtendedContract.Events(_contract, oldBody);
        _hacking = _contract.Hacking.OwnedBy(oldBody);
        _picking = _contract.Picking.OwnedBy(oldBody);
    }

    internal static void CompletePreparation()
    {
        if (!Scope.Preparing) return;
        var old = Scope.Prepared;
        var activeHacking = _contract.Hacking.OwnedBy(old);
        var activePicking = _contract.Picking.OwnedBy(old);
        if (activeHacking == null && activePicking == null) return;
        if ((activeHacking != null && !ReferenceEquals(activeHacking, _hacking))
            || (activePicking != null && !ReferenceEquals(activePicking, _picking)))
            throw new InvalidOperationException("Skills Extended opened another minigame during replacement preparation");
        if (!ReferenceEquals(Scope.Game.LocalPlayer, old) || !ReferenceEquals(Scope.World.MainPlayer, old)
            || (GamePlayerOwner.MyPlayer != null && !ReferenceEquals(GamePlayerOwner.MyPlayer, old)))
            throw new InvalidOperationException("Skills Extended minigame owner changed before replacement");
        if (_contract.Hacking.Current != null && _contract.Picking.Current != null)
            throw new InvalidOperationException("Skills Extended has overlapping input owners; close the minigames before replacing the body");
        // Call the foreign cancellation path while the original player still owns input.
        // Each call rechecks the view itself. Practice and successor views are never closed.
        _contract.Hacking.AbortIfStillOwned(_hacking, old);
        _contract.Picking.AbortIfStillOwned(_picking, old);
        _hacking = _picking = null;
    }

    private static bool ControlsCallbacks =>
        Scope.Owned && ReferenceEquals(Scope.World, StandaloneHudScope.CurrentWorld)
            && ReferenceEquals(Scope.Game, StandaloneHudScope.CurrentGame);

    private static void AfterStarted(GameWorld __0)
    {
        // The upstream postfix can run while Arena's initial kit replacement is still
        // creating. Keep the candidate's physical lease even if creation subsequently
        // fails before ValidateCreated, so recovery can move its subscriptions back.
        if (!ReferenceEquals(__0, Scope.World) || _contract == null) return;
        try
        {
            var player = _contract.PlayerField.GetValue(null) as Player;
            if (Scope.Preparing) BodyReplacementContext.TryOwnCandidate(player);
            else if (Scope.Active && ReferenceEquals(Scope.Body, player)) _liveEvents?.KeepOneSubscription();
        }
        catch (Exception ex) { Fault(ex); }
    }

    private static Player CurrentXpPlayer()
    {
        var body = Scope.Body;
        if (!ReferenceEquals(Scope.Prepared, null) || body == null || body.HealthController?.IsAlive != true
            || !ReferenceEquals(body, Scope.World.MainPlayer) || !ReferenceEquals(body, Scope.Game.LocalPlayer)
            || !ReferenceEquals(_contract?.PlayerField.GetValue(null), body)) return null;
        return body;
    }

    private static bool BeforeMastery(Mastering __0)
    {
        if (!ControlsCallbacks) return true;
        try
        {
            var body = CurrentXpPlayer();
            var group = __0?.MasteringGroup?.Id;
            return body != null && group != null && body.Skills.Mastering.TryGetValue(group, out var current)
                && ReferenceEquals(__0, current) && body.HandsController != null && body.HandsController.GetItem() != null;
        }
        catch (Exception ex) { Fault(ex); return false; }
    }

    private static bool BeforeMedical(IHealthEffect __0)
    {
        if (!ControlsCallbacks) return true;
        // A late old health notification cannot award XP on the replacement controller.
        try
        {
            var body = CurrentXpPlayer();
            return body != null && __0 is ActiveHealthController.Effect effect
                && ReferenceEquals(effect.HealthController, body.ActiveHealthController);
        }
        catch (Exception ex) { Fault(ex); return false; }
    }

    private static void Replaced(Player old, Player next, string reason)
    {
        if (ReferenceEquals(old, null) || !ReferenceEquals(old, Scope.Prepared)) return;
        if (!Scope.CanBind(next, true)) { _recovery = true; return; }
        try { Finish(next, false); } catch (Exception ex) { Fault(ex); }
    }

    private static void Failed(Player old, string stage)
    {
        if (!ReferenceEquals(old, null) && ReferenceEquals(old, Scope.Prepared)) _recovery = true;
    }

    internal static void Tick()
    {
        if (!_recovery || _failure != null || BodyReplacementContext.IsCreating) return;
        if (!BodyReplacementContext.OwnsRaid(Scope.Game, Scope.World)) { RetireWorld(Scope.World); return; }
        try
        {
            if (Scope.Exact(Scope.Prepared)) Finish(Scope.Prepared, true);
            else if (Scope.CanBind(Scope.World.MainPlayer, false)) Finish(Scope.World.MainPlayer, false);
        }
        catch (Exception ex) { Fault(ex); }
    }

    private static void Finish(Player next, bool recoveredOriginal)
    {
        var bound = (Player)_contract.PlayerField.GetValue(null);
        if (ReferenceEquals(bound, Scope.Prepared) && !ReferenceEquals(bound, next))
            _oldEvents.Move(next);
        else if (!ReferenceEquals(bound, null) && !ReferenceEquals(bound, next) && ReferenceEquals(bound.GameWorld, Scope.World))
        {
            // The original raid-start postfix may have observed a candidate which later failed.
            if (!BodyReplacementContext.OwnsBody(bound)) throw new InvalidOperationException("Skills Extended XP belongs to a foreign player");
            new SkillsExtendedContract.Events(_contract, bound).Move(next);
        }
        // A null/prior-world field belongs to a not-yet-run upstream game-start postfix.
        // Its normal initialization will see the newly wired MainPlayer.
        if (recoveredOriginal) Scope.RecoverPrepared(); else Scope.Bind((LocalPlayer)next);
        _liveEvents = new SkillsExtendedContract.Events(_contract, next);
        if (ReferenceEquals(bound, next)) _liveEvents.KeepOneSubscription();
        ClearAttempt();
    }

    internal static void AbortPreparation() { Scope.Abort(); ClearAttempt(); }
    internal static void RetireWorld(GameWorld world)
    {
        if (!ReferenceEquals(world, Scope.World)) return;
        try
        {
            // A profile's mastery manager can survive into another raid. Remove only this
            // world's callbacks, and never detach from a manager already bound by a newer world.
            if (!ReferenceEquals(Scope.Body, null) && ReferenceEquals(_contract?.PlayerField.GetValue(null), Scope.Body))
                _liveEvents?.Detach();
        }
        catch (Exception ex) { Fault(ex); }
        finally
        {
            Scope.Retire(world);
            _liveEvents = null;
            ClearAttempt();
        }
    }
    private static void ClearAttempt() { _oldEvents = null; _picking = _hacking = null; _recovery = false; }
    private static void Fault(Exception ex)
    {
        string message = "Skills Extended body replacement compatibility failed: " + ex.GetBaseException().Message;
        if (message != _failure) CoreLog.LogWarning(message);
        _failure = message;
    }
}

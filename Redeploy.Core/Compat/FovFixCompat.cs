using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Animations;
using HarmonyLib;
using Redeploy.Core.Api;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Rebind Fontaine's persistent FOV controller only for a Core-owned replacement.</summary>
internal static class FovFixCompat
{
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.FovFixCompat");
    private static readonly FovFixScope Scope = new();
    private static FovFixContract _contract;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static bool _initialized, _fault, _patchAttempted, _newerWarned;
    private static string _failure, _lastWarning;

    private static LocalGame Game => Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
    private static GameWorld World => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;

    internal static string GetRejectionReason()
    {
        if (!_initialized)
        {
            _initialized = true;
            RespawnApi.Replaced += Replaced;
        }
        if (_fault) return _failure;
        if (!Chainloader.PluginInfos.TryGetValue(FovFixContract.PluginGuid, out var info))
        {
            ClearBinding();
            return _failure = null;
        }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Fontaine FOV Fix has not finished loading");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance)
                && _version == FovFixContract.Normalize(info.Metadata?.Version))
            {
                _contract.Verify();
                VerifyPatches();
                return _failure;
            }
            ClearBinding();
            _contract = FovFixContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version, out _version);
            _plugin = info.Instance;
            _patchAttempted = true;
            Patch(_contract.Update, nameof(BeforeUpdate));
            Patch(_contract.ChangeFov, nameof(BeforeChange));
            Patch(_contract.PwaPostfix, nameof(BeforePwa));
            Patch(_contract.ScalePrefix, nameof(BeforeScale));
            VerifyPatches();
            _failure = null;
            CoreLog.LogDebug("Fontaine FOV Fix: exact-body camera handoff ready.");
            if (_version > FovFixContract.ReviewedVersion && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("Fontaine FOV Fix " + _version
                    + " is newer than reviewed 4.1.0; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex) { ClearBinding(); Reject(ex); }
        return _failure;
    }

    private static void Patch(MethodInfo target, string prefix)
        => Patches.Patch(target, prefix: new HarmonyMethod(typeof(FovFixCompat), prefix) { priority = Priority.First });

    private static void VerifyPatches()
    {
        var methods = new[] { _contract.Update, _contract.ChangeFov, _contract.PwaPostfix, _contract.ScalePrefix };
        var names = new[] { nameof(BeforeUpdate), nameof(BeforeChange), nameof(BeforePwa), nameof(BeforeScale) };
        for (int i = 0; i < methods.Length; i++)
        {
            var prefix = typeof(FovFixCompat).GetMethod(names[i], BindingFlags.NonPublic | BindingFlags.Static);
            if (Harmony.GetPatchInfo(methods[i])?.Prefixes.Count(p => p.owner == Patches.Id && p.PatchMethod == prefix) != 1)
                throw new MissingMethodException("Fontaine FOV Fix compatibility hook missing: " + names[i]);
        }
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        var refusal = GetRejectionReason();
        if (refusal != null) throw new InvalidOperationException(refusal);
        if (_contract == null) return;
        var world = game?.GameWorld;
        if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, world)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, oldBody) || !BodyReplacementContext.OwnsBody(oldBody)
            || !ReferenceEquals(world.MainPlayer, oldBody) || !ReferenceEquals(game.LocalPlayer, oldBody))
            throw new InvalidOperationException("FOV handover requires Core's exact old body");
        var controller = _contract.Controller.GetValue(null);
        var cached = _contract.PlayerField.GetValue(controller);
        if (!ReferenceEquals(cached, null) && !ReferenceEquals(cached, oldBody))
            throw new InvalidOperationException("FOV controller belongs to another physical body before replacement");
        Scope.Prepare(game, world, controller, oldBody);
    }

    internal static void CompletePreparation() => Scope.CompletePreparation();
    internal static void AbortPreparation() => Scope.AbortPreparation();

    private static void Replaced(Player oldBody, Player newBody, string reason)
    {
        if (!Scope.Enabled || !ReferenceEquals(Scope.Active.Body, oldBody)) return;
        try { Synchronize(newBody, published: true); }
        catch (Exception ex) { RuntimeFailure(ex); }
    }

    internal static void Tick()
    {
        if (_contract == null || !Scope.Enabled || _fault) return;
        try
        {
            var world = World;
            if (!ReferenceEquals(Scope.Active.World, world)) { RetireWorld(Scope.Active.World as GameWorld); return; }
            Synchronize(world?.MainPlayer, published: false);
        }
        catch (Exception ex) { RuntimeFailure(ex); }
    }

    private static void Synchronize(Player body, bool published)
    {
        var game = Game; var world = World;
        bool owned = BodyReplacementContext.OwnsBody(body);
        if (!Scope.CanBind(body, game, world, world?.MainPlayer, game?.LocalPlayer,
            BodyReplacementContext.OwnsRaid(game, world), owned, body != null && body.HealthController?.IsAlive == true,
            BodyReplacementContext.IsCreating, published)) return;
        var controller = Scope.Active.Controller;
        if (!ReferenceEquals(_contract.Controller.GetValue(null), controller))
            throw new InvalidOperationException("FOV controller changed during replacement");
        var cached = _contract.PlayerField.GetValue(controller);
        if (!Scope.Active.Frozen && ReferenceEquals(cached, body) && ReferenceEquals(Scope.Active.Body, body)) return;
        if (!ReferenceEquals(cached, null) && !Scope.OwnsBody(cached) && !ReferenceEquals(cached, body))
            throw new InvalidOperationException("FOV cache changed to an unrelated physical body");
        Scope.TrackCandidate(body, game, world, owned);
        _contract.Reset(controller, body);
        Scope.Bind(body);
        // A construction-time foreign scale callback was deliberately left native.
        // Reapply now that the argument and global MainPlayer are the same body.
        body.SetCompensationScale(true);
        // The camera has been rewired by Replaced. Recovering a failed operation waits
        // until the engine stopped creating and both authoritative player references agree.
        if (body.ProceduralWeaponAnimation != null) _contract.ChangeFov.Invoke(controller, null);
        CoreLog.LogDebug("Fontaine FOV Fix: camera controller follows the replacement body.");
    }

    private static bool Allows(Player body)
    {
        var game = Game; var world = World;
        return body != null && Scope.Allows(body, game, world, world?.MainPlayer, game?.LocalPlayer,
            BodyReplacementContext.OwnsRaid(game, world), body.HealthController?.IsAlive == true);
    }

    private static bool BeforeUpdate(object __instance)
    {
        if (_contract == null) return true;
        try
        {
            if (Scope.Enabled)
            {
                if (!ReferenceEquals(__instance, Scope.Active.Controller)) return !Scope.TouchedController(__instance);
                Tick();
                return !_fault && Allows(_contract.PlayerField.GetValue(__instance) as Player);
            }
            // Retired controller may not follow a corpse into the next ordinary raid.
            // Clearing only an owned reference lets upstream perform its own normal bind.
            if (Scope.OwnsBody(_contract.PlayerField.GetValue(__instance))) _contract.Reset(__instance, null);
            return !Scope.OwnsBody(World?.MainPlayer);
        }
        catch (Exception ex) { RuntimeFailure(ex); return !Scope.TouchedController(__instance); }
    }

    private static bool BeforeChange(object __instance)
    {
        if (_contract == null) return true;
        try
        {
            var body = _contract.PlayerField.GetValue(__instance) as Player;
            if (Scope.Enabled && ReferenceEquals(__instance, Scope.Active.Controller)) return !_fault && Allows(body);
            return !Scope.OwnsBody(body) && !(Scope.TouchedController(__instance) && ReferenceEquals(body, null));
        }
        catch (Exception ex) { RuntimeFailure(ex); return !Scope.TouchedController(__instance); }
    }

    private static bool BeforePwa(ref ProceduralWeaponAnimation __0)
    {
        if (_contract == null || __0 == null) return true;
        try
        {
            var hands = _contract.PwaHands.GetValue(__0) as Player.FirearmController;
            return MayRunBodyCallback(hands == null ? null : hands.GetComponent<Player>());
        }
        catch (Exception ex) { RuntimeFailure(ex); return !Scope.Enabled; }
    }

    // This wraps the foreign bool prefix. Skipping it must return true to the native
    // SetCompensationScale, otherwise construction would silently lose native setup.
    private static bool BeforeScale(Player __0, ref bool __result)
    {
        if (_contract == null) return true;
        try
        {
            if (MayRunBodyCallback(__0)) return true;
            __result = true;
            return false;
        }
        catch (Exception ex) { RuntimeFailure(ex); __result = true; return !Scope.Enabled && !Scope.OwnsBody(__0); }
    }

    private static bool MayRunBodyCallback(Player body)
    {
        if (ReferenceEquals(body, null)) return true;
        bool owned = BodyReplacementContext.OwnsBody(body) || BodyReplacementContext.TryOwnCandidate(body);
        Scope.TrackCandidate(body, Game, World, owned);
        if (Scope.OwnsBody(body)) return !_fault && Allows(body);
        // During native creation the scale hook can arrive BEFORE MainPlayer is set.
        // Do not grant that body a lease: merely keep the native implementation rather
        // than let the foreign prefix redirect its work to the old global MainPlayer.
        if (Scope.Enabled && BodyReplacementContext.IsCreating && body.IsYourPlayer
            && BodyReplacementContext.OwnsRaid(Game, World) && ReferenceEquals(body.GameWorld, World)
            && ReplacementProfileRule.CarriesRaidProfile(body.Profile, Game?.Profile, BodyReplacementContext.ExpectedProfile)) return false;
        return true;
    }

    internal static void RetireWorld(GameWorld world)
    {
        var lease = Scope.RetireWorld(world);
        if (lease == null || _contract == null) return;
        try
        {
            if (ReferenceEquals(_contract.Controller.GetValue(null), lease.Controller)
                && Scope.OwnsBody(_contract.PlayerField.GetValue(lease.Controller)))
                _contract.Reset(lease.Controller, null);
        }
        catch (Exception ex) { RuntimeFailure(ex); }
    }

    private static void RuntimeFailure(Exception ex) { _fault = true; Reject(ex); }
    private static void Reject(Exception ex)
    {
        _failure = "Core cannot safely replace the player with Fontaine FOV Fix: " + ex.GetBaseException().Message
            + ". Update or remove Fontaine FOV Fix, then restart. Ordinary raids remain available.";
        if (_lastWarning == _failure) return;
        _lastWarning = _failure;
        CoreLog.LogWarning(_failure);
    }
    private static void ClearBinding()
    {
        if (_patchAttempted) { _patchAttempted = false; Patches.UnpatchSelf(); }
        _contract = null; _plugin = null; _version = null;
    }
}

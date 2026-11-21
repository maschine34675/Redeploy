using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using EFT;
using EFT.Animations;
using HarmonyLib;
using Redeploy.Core.Api;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Follow Core's physical player replacement without retaining Armory's old burst owner.</summary>
internal static class WttArmoryBodyCompatibility
{
    internal const string PluginGuid = "com.wtt.armory";
    private static readonly Version Reviewed = new(2, 0, 5, 0);
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.WttArmoryCompat");
    private static readonly List<MethodInfo> Targets = new();
    private static Contract _contract;
    private static object _plugin;
    private static Version _version;
    private static LocalGame _game;
    private static GameWorld _world;
    private static LocalGame _previousGame;
    private static GameWorld _previousWorld;
    private static LocalPlayer _outgoing, _active;
    private static string _failure, _warning;
    private static bool _subscribed, _newerWarned;

    internal static string GetRejectionReason()
    {
        if (!_subscribed) { _subscribed = true; RespawnApi.Replaced += Replaced; }
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info))
        { Clear(); return _failure = null; }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            var version = Normalize(info.Metadata?.Version);
            if (_contract != null && ReferenceEquals(_plugin, info.Instance) && version == _version)
            { VerifyHooks(); return _failure; }
            if (_outgoing != null || _active != null)
                throw new InvalidOperationException("plugin changed during an owned raid");
            Clear();
            var type = info.Instance.GetType();
            var metadata = type.GetCustomAttribute<BepInPlugin>(false);
            if (version == null || version < Reviewed) throw new NotSupportedException("version 2.0.5 or newer is required");
            if (type.FullName != "WTTArmoryClient.Plugin" || type.Assembly.GetName().Name != "WTT-ArmoryClient"
                || !typeof(BaseUnityPlugin).IsAssignableFrom(type) || info.Metadata.GUID != PluginGuid
                || metadata?.GUID != PluginGuid || Normalize(metadata.Version) != version)
                throw new InvalidOperationException("plugin identity or metadata changed");
            _contract = new Contract(type);
            foreach (var target in _contract.Callbacks)
            {
                Targets.Add(target);
                Patches.Patch(target, prefix: new HarmonyMethod(Hook) { priority = Priority.First });
            }
            _plugin = info.Instance; _version = version;
            VerifyHooks(); _failure = null;
            CoreLog.LogDebug("WTT Armory: physical-player and hyperburst replacement adapter ready.");
            if (version > Reviewed && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("WTT Armory " + version + " is newer than reviewed 2.0.5; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex) { Clear(); Reject(ex); }
        return _failure;
    }

    internal static void Prepare(LocalGame game, LocalPlayer body)
    {
        if (_contract == null) return;
        if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, body) || !BodyReplacementContext.OwnsBody(body)
            || !ReferenceEquals(game.LocalPlayer, body) || !ReferenceEquals(game.GameWorld.MainPlayer, body))
            throw new InvalidOperationException("WTT Armory requires Core's exact old body");
        var cached = _contract.Player(_plugin);
        if (!ReferenceEquals(cached, null) && !ReferenceEquals(cached, body))
            throw new InvalidOperationException("WTT Armory's cached player does not belong to the outgoing body");
        _previousGame = _game; _previousWorld = _world;
        _game = game; _world = game.GameWorld; _outgoing = body;
        // Admission is read-only: another adapter can still cancel the operation.
    }

    internal static void AbortPreparation()
    {
        if (_outgoing == null) return;
        _game = _previousGame; _world = _previousWorld;
        _outgoing = null; _previousGame = null; _previousWorld = null;
    }

    internal static void EndAttempt()
    {
        try
        {
            // A failed rewire can already have committed game.LocalPlayer without publishing Replaced.
            if (_outgoing != null) Commit(_outgoing, _game?.LocalPlayer as LocalPlayer);
        }
        catch (Exception ex) { Reject(ex); }
        finally
        {
            if (_active == null) { _active = null; _game = null; _world = null; }
            _outgoing = null; _previousGame = null; _previousWorld = null;
        }
    }

    private static void Replaced(Player oldBody, Player newBody, string reason)
    {
        if (!ReferenceEquals(oldBody, _outgoing)) return;
        try { Commit(oldBody as LocalPlayer, newBody as LocalPlayer); }
        catch (Exception ex) { Reject(ex); }
    }

    private static void Commit(LocalPlayer oldBody, LocalPlayer newBody)
    {
        if (_contract == null || oldBody == null || newBody == null || ReferenceEquals(oldBody, newBody)
            || ReferenceEquals(_active, newBody) || !ReferenceEquals(oldBody, _outgoing)
            || !BodyReplacementContext.OwnsRaid(_game, _world) || !BodyReplacementContext.OwnsBody(newBody)
            || !ReferenceEquals(newBody.GameWorld, _world) || !newBody.IsYourPlayer
            || !ReferenceEquals(_game.LocalPlayer, newBody) || !ReferenceEquals(_world.MainPlayer, newBody)) return;
        var cached = _contract.Player(_plugin);
        if (!ReferenceEquals(cached, null) && !ReferenceEquals(cached, oldBody) && !ReferenceEquals(cached, newBody))
            throw new InvalidOperationException("a different physical player owns WTT Armory's burst state");
        _contract.Rebind(_plugin, newBody);
        _active = newBody;
        CoreLog.LogDebug("WTT Armory: replaced physical player and reset outgoing hyperburst counters.");
    }

    internal static void RetireWorld(GameWorld world)
    {
        if (ReferenceEquals(world, null) || !ReferenceEquals(world, _world)) return;
        try
        {
            if (_contract != null)
            {
                var cached = _contract.Player(_plugin);
                if (!ReferenceEquals(cached, null)
                    && (ReferenceEquals(cached, _active) || ReferenceEquals(cached, _outgoing)))
                    _contract.Rebind(_plugin, null);
            }
        }
        catch (Exception ex) { Reject(ex); }
        finally
        {
            _game = null; _world = null; _outgoing = null; _active = null;
            _previousGame = null; _previousWorld = null;
        }
    }

    // Armory's three state-writing callbacks are guarded at their source. These delegates are
    // resolved once; a shot/weapon switch does no reflection, registry lookup or scene search.
    private static bool BeforeArmoryCallback(ProceduralWeaponAnimation __0)
    {
        if (_contract == null || __0 == null) return true;
        var body = _contract.Owner(__0);
        if (ReferenceEquals(body, null)) return true;
        // Construction can invoke Armory before the engine reaches ValidateCreated. Claim
        // only the exact candidate already selected by this world's local-player factory;
        // its delayed callbacks must stay fenced if asynchronous creation later fails.
        if (!BodyReplacementContext.OwnsBody(body) && !BodyReplacementContext.TryOwnCandidate(body)) return true;
        var game = BodyReplacementContext.CurrentGame;
        var world = BodyReplacementContext.CurrentWorld;
        return BodyReplacementContext.OwnsRaid(game, world)
            && ReferenceEquals(game.LocalPlayer, body) && ReferenceEquals(world.MainPlayer, body);
    }

    private static MethodInfo Hook => typeof(WttArmoryBodyCompatibility).GetMethod(nameof(BeforeArmoryCallback), BindingFlags.Static | BindingFlags.NonPublic);
    private static void VerifyHooks()
    {
        foreach (var target in Targets)
            if (Harmony.GetPatchInfo(target)?.Prefixes.Count(p => p.owner == Patches.Id && p.PatchMethod == Hook) != 1)
                throw new MissingMethodException("WTT Armory body-owner hook is missing");
    }
    private static void Clear()
    {
        foreach (var target in Targets) Patches.Unpatch(target, Hook);
        Targets.Clear(); _contract = null; _plugin = null; _version = null;
        _game = null; _world = null; _outgoing = null; _active = null;
        _previousGame = null; _previousWorld = null;
    }
    private static void Reject(Exception ex)
    {
        _failure = "WTT Armory body replacement compatibility failed: " + ex.GetBaseException().Message + ".";
        if (_warning != _failure) { _warning = _failure; CoreLog.LogWarning(_failure); }
    }
    private static Version Normalize(Version v) => v == null || v.Build < 0 ? null
        : new Version(v.Major, v.Minor, v.Build, Math.Max(0, v.Revision));

    private sealed class Contract
    {
        private readonly PropertyInfo _you, _gun, _firing, _timer, _recoil, _rof;
        private readonly FieldInfo _gunIds;
        private readonly AccessTools.FieldRef<ProceduralWeaponAnimation, Player.FirearmController> _firearm;
        private readonly AccessTools.FieldRef<Player.FirearmController, Player> _owner;
        internal readonly MethodInfo[] Callbacks;

        internal Contract(Type type)
        {
            _you = Property(type, "You", typeof(Player), false);
            _gun = Property(type, "GunHasHyperburst", typeof(bool), true);
            _firing = Property(type, "IsFiring", typeof(bool), true);
            _timer = Property(type, "ShotTimer", typeof(float), true);
            _recoil = Property(type, "RecoilShotCount", typeof(int), true);
            _rof = Property(type, "ROFShotCount", typeof(int), true);
            _gunIds = type.GetField("GunIDs", BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly);
            if (_gunIds?.FieldType != typeof(string[])) throw new MissingFieldException("WTT Armory GunIDs contract changed");
            _firearm = AccessTools.FieldRefAccess<ProceduralWeaponAnimation, Player.FirearmController>("_firearmController");
            _owner = AccessTools.FieldRefAccess<Player.FirearmController, Player>("_player");
            Callbacks = new[] { Callback(type, "UpdateWeaponVariablesPatch", "PatchPostfix", false),
                Callback(type, "ShootPatch", "PatchPrefix", true), Callback(type, "ShootPatch", "PatchPostFix", true) };
        }

        internal Player Player(object plugin) => (Player)_you.GetValue(plugin);
        internal Player Owner(ProceduralWeaponAnimation pwa)
        { var firearm = _firearm(pwa); return firearm == null ? null : _owner(firearm); }

        internal void Rebind(object plugin, LocalPlayer body)
        {
            var ids = (string[])_gunIds.GetValue(null);
            if (ids == null) throw new InvalidOperationException("WTT Armory hyperburst weapon list is unavailable");
            var firearm = body?.HandsController as Player.FirearmController;
            var template = firearm?.Weapon?.TemplateId.ToString();
            bool hyperburst = template != null && Array.IndexOf(ids, template) >= 0;
            // Compute everything before writes. Never carry the dead body's firing/timer/counters,
            // and never erase a new AN-94's classification merely because Replaced fires late.
            _you.SetValue(plugin, body);
            _gun.SetValue(null, hyperburst);
            _firing.SetValue(null, false); _timer.SetValue(null, 0f);
            _recoil.SetValue(null, 0); _rof.SetValue(null, 0);
        }

        private static PropertyInfo Property(Type type, string name, Type valueType, bool isStatic)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (property?.PropertyType != valueType || property.GetGetMethod()?.IsStatic != isStatic
                || property.GetSetMethod()?.IsStatic != isStatic || property.GetIndexParameters().Length != 0)
                throw new MissingMemberException("WTT Armory " + name + " contract changed");
            return property;
        }
        private static MethodInfo Callback(Type plugin, string typeName, string methodName, bool strength)
        {
            var type = plugin.Assembly.GetType("WTTArmoryClient.Patches." + typeName, true);
            var args = strength ? new[] { typeof(ProceduralWeaponAnimation), typeof(float).MakeByRefType() }
                : new[] { typeof(ProceduralWeaponAnimation) };
            var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly, null, args, null);
            if (method?.ReturnType != typeof(void) || method.GetMethodBody() == null)
                throw new MissingMethodException("WTT Armory " + typeName + "." + methodName + " contract changed");
            return method;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using HarmonyLib;
using Redeploy.Core.Api;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Keep Healing Autocancel's event and static player references on the committed physical body.</summary>
internal static class HealingAutoCancelCompat
{
    internal const string PluginGuid = "HealingAutoCancel";
    // Forge package 5.0.1 carries plugin/assembly version 1.1.0.
    private static readonly Version Reviewed = new(1, 1, 0, 0);
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.HealingAutoCancelCompat");
    private static readonly ConditionalWeakTable<ActiveHealthController, object> Retired = new();
    private static readonly List<MethodInfo> UnregisterTargets = new();
    private sealed class Binding
    {
        internal LocalGame Game;
        internal GameWorld World;
        internal Player Body;
        internal ActiveHealthController Health;
        internal Action<EBodyPart, float, DamageInfo> Callback;
    }
    private static Binding _binding;
    private static LocalGame _game;
    private static GameWorld _world;
    private static Player _retiredBody;
    private static object _plugin;
    private static Version _version;
    private static PropertyInfo _playerProperty, _healthProperty;
    private static MethodInfo _setPlayer, _setHealth, _handler, _started, _changeHealth, _unregister;
    private static Action<EBodyPart, float, DamageInfo> _original;
    private static string _failure, _warning;
    private static bool _newerWarned, _subscribed;
    [ThreadStatic] private static ActiveHealthController _dispatchSource;
    [ThreadStatic] private static Binding _wrapper;

    internal static string GetRejectionReason()
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info))
        {
            if (_plugin == null) { Clear(); return _failure = null; }
            Reject(new InvalidOperationException("loaded plugin disappeared from the registry"));
            return _failure;
        }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            var version = Normalize(info.Metadata?.Version);
            if (ReferenceEquals(_plugin, info.Instance) && version == _version)
            { Verify(); return _failure; }
            if (_plugin != null) throw new InvalidOperationException("loaded plugin identity or version changed during this process");
            Clear();
            var type = info.Instance.GetType();
            var metadata = type.GetCustomAttribute<BepInPlugin>(false);
            if (version == null || version < Reviewed) throw new NotSupportedException("version 1.1.0 or newer is required");
            if (type.FullName != "ImprovedSelfcare.Plugin" || type.Assembly.GetName().Name != "HealingAutoCancel"
                || !typeof(BaseUnityPlugin).IsAssignableFrom(type) || info.Metadata.GUID != PluginGuid
                || metadata?.GUID != PluginGuid || Normalize(metadata.Version) != version)
                throw new InvalidOperationException("plugin identity or metadata changed");
            var globals = type.Assembly.GetType("ImprovedSelfcare.Globals", true);
            _playerProperty = Property(globals, "player", typeof(Player));
            _healthProperty = Property(globals, "activeHealthController", typeof(ActiveHealthController));
            _setPlayer = Method(globals, "SetPlayer", true, typeof(void), typeof(Player));
            _setHealth = Method(globals, "SetPlayerHealthController", true, typeof(void), typeof(ActiveHealthController));
            var patch = type.Assembly.GetType("ImprovedSelfcare.HealingAutoCancelPatch", true);
            _handler = Method(patch, "ActiveHealthController_HealthChangedEvent", true, typeof(void), typeof(EBodyPart), typeof(float), typeof(DamageInfo));
            _started = Method(patch, "PostFix", true, typeof(void));
            _changeHealth = Method(typeof(ActiveHealthController), nameof(ActiveHealthController.ChangeHealth), false,
                typeof(void), typeof(EBodyPart), typeof(float), typeof(DamageInfo));
            _unregister = Method(typeof(GameWorld), nameof(GameWorld.UnregisterPlayer), false, typeof(void), typeof(IPlayer));
            _original = (Action<EBodyPart, float, DamageInfo>)Delegate.CreateDelegate(typeof(Action<EBodyPart, float, DamageInfo>), _handler);
            Patches.Patch(_handler, prefix: Hook(nameof(BeforeHandler)));
            Patches.Patch(_started, postfix: Hook(nameof(AfterStarted)));
            Patches.Patch(_changeHealth, prefix: Hook(nameof(BeforeChangeHealth)), finalizer: Hook(nameof(AfterChangeHealth)));
            AddUnregister(_unregister);
            if (!_subscribed) { RespawnApi.Replaced += Replaced; _subscribed = true; }
            _plugin = info.Instance; _version = version; Verify(); _failure = null;
            if (version > Reviewed && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("Healing Autocancel " + version + " is newer than reviewed 1.1.0 (Forge 5.0.1); APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            // Once adopted, keep all remaining fences and event ownership intact.
            // Refusing the next replacement must not expose an old body callback.
            if (_plugin == null) Clear();
            Reject(ex);
        }
        return _failure;
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        if (_original == null) return;
        if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, oldBody) || !BodyReplacementContext.OwnsBody(oldBody))
            throw new InvalidOperationException("Healing Autocancel requires Core's exact old body");
        var current = PlayerGlobal;
        if (current != null && !AwaitingStart(game.GameWorld)
            && (!ReferenceEquals(current, oldBody) || !ReferenceEquals(HealthGlobal, oldBody.ActiveHealthController)))
            throw new InvalidOperationException("Healing Autocancel's player or health controller is not the current old body");
        if (current == null && HealthGlobal != null) throw new InvalidOperationException("Healing Autocancel has inconsistent uninitialized globals");
        var target = game.GameWorld.GetType().GetMethod(nameof(GameWorld.UnregisterPlayer), new[] { typeof(IPlayer) });
        target = target?.DeclaringType?.GetMethod(nameof(GameWorld.UnregisterPlayer),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new[] { typeof(IPlayer) }, null);
        if (target == null || target.GetBaseDefinition() != _unregister || target.GetMethodBody() == null)
            throw new MissingMethodException("Healing Autocancel cannot verify runtime world unregister");
        AddUnregister(target); Verify();
        // Preparation is read-only. Another adapter may still refuse this attempt.
    }
    internal static void AbortPreparation() { }

    private static void BeforeUnregister(GameWorld __instance, IPlayer __0)
    {
        if (_original == null || __0 is not Player body || !BodyReplacementContext.IsCreating
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, body) || !BodyReplacementContext.OwnsBody(body)
            || !BodyReplacementContext.OwnsRaid(BodyReplacementContext.CurrentGame, __instance)) return;
        _game = BodyReplacementContext.CurrentGame; _world = __instance; _retiredBody = body;
        Retire(body.ActiveHealthController);
        Detach();
    }
    private static void Retire(ActiveHealthController health)
    {
        if (ReferenceEquals(health, null)) return;
        if (!Retired.TryGetValue(health, out _)) Retired.Add(health, new object());
        health.HealthChangedEvent -= _original;
    }
    private static void Replaced(Player previous, Player next, string reason)
    {
        try { if (ReferenceEquals(previous, _retiredBody)) FollowCurrent(next); }
        catch (Exception ex) { Reject(ex); }
    }
    internal static void Tick()
    {
        if (_original == null || _world == null || BodyReplacementContext.IsCreating) return;
        try { FollowCurrent(_world.MainPlayer); }
        catch (Exception ex) { Reject(ex); }
    }
    private static void AfterStarted()
    {
        // An Arena entry-kit replacement can precede the foreign OnGameStarted hook.
        // Let its ordinary initialization run, then adopt only our committed body.
        try { if (_original != null && _world != null) FollowCurrent(_world.MainPlayer, true); }
        catch (Exception ex) { Reject(ex); }
    }
    private static bool Current(Player body)
        => body != null && body is LocalPlayer && body.IsYourPlayer && BodyReplacementContext.OwnsBody(body)
           && BodyReplacementContext.OwnsRaid(_game, _world) && ReferenceEquals(body.GameWorld, _world)
           && ReferenceEquals(_world.MainPlayer, body) && ReferenceEquals(_game.LocalPlayer, body)
           && ReferenceEquals(_game.Profile, body.Profile) && ReferenceEquals(GamePlayerOwner.MyPlayer, body)
           && _world.RegisteredPlayers.Contains(body);

    private static void FollowCurrent(Player body, bool foreignInitialized = false)
    {
        if (!Current(body)) return;
        if (_binding != null && ReferenceEquals(_binding.Body, body))
        {
            if (foreignInitialized) body.ActiveHealthController.HealthChangedEvent -= _original;
            return;
        }
        var global = PlayerGlobal;
        // A pre-OnGameStarted entry-kit swap must not initialize this mod prematurely.
        if (global == null && HealthGlobal == null || AwaitingStart(_world)) return;
        if (!ReferenceEquals(global, body) && !ReferenceEquals(global, _retiredBody))
            throw new InvalidOperationException("Healing Autocancel globals changed to an unrelated body");
        Detach();
        var lease = new Binding { Game = _game, World = _world, Body = body, Health = body.ActiveHealthController };
        if (lease.Health == null) throw new InvalidOperationException("Healing Autocancel replacement health is unavailable");
        lease.Callback = (part, amount, damage) => Dispatch(lease, part, amount, damage);
        _setPlayer.Invoke(null, new object[] { body });
        _setHealth.Invoke(null, new object[] { lease.Health });
        lease.Health.HealthChangedEvent -= _original;
        _binding = lease;
        lease.Health.HealthChangedEvent += lease.Callback;
        CoreLog.LogDebug("Healing Autocancel: medical event bound to the committed replacement body.");
    }
    private static bool Live(Binding lease)
        => ReferenceEquals(lease, _binding) && ReferenceEquals(lease.Game, _game) && ReferenceEquals(lease.World, _world)
           && Current(lease.Body) && ReferenceEquals(lease.Body.ActiveHealthController, lease.Health)
           && ReferenceEquals(PlayerGlobal, lease.Body) && ReferenceEquals(HealthGlobal, lease.Health);
    private static void Dispatch(Binding lease, EBodyPart part, float amount, DamageInfo damage)
    {
        if (!Live(lease)) return;
        var previous = _wrapper;
        try { _wrapper = lease; _original(part, amount, damage); }
        finally { _wrapper = previous; }
    }
    private static bool BeforeHandler()
    {
        if (_wrapper != null) return Live(_wrapper)
            && (_dispatchSource == null || ReferenceEquals(_dispatchSource, _wrapper.Health));
        // EFT ChangeHealth is the event's sole native emitter. Preserve its source
        // across nested dispatch and exceptions: a captured old static delegate has
        // no sender argument and must not read the new body's foreign globals.
        if (_dispatchSource == null || !ReferenceEquals(_dispatchSource, HealthGlobal)) return false;
        if (_dispatchSource != null && Retired.TryGetValue(_dispatchSource, out _)) return false;
        return _binding == null || !ReferenceEquals(PlayerGlobal, _binding.Body);
    }
    private static void BeforeChangeHealth(ActiveHealthController __instance, out ActiveHealthController __state)
    { __state = _dispatchSource; _dispatchSource = __instance; }
    private static Exception AfterChangeHealth(Exception __exception, ActiveHealthController __state)
    { _dispatchSource = __state; return __exception; }
    internal static void RetireWorld(GameWorld world)
    {
        if (!ReferenceEquals(world, _world)) return;
        if (_binding != null) Retire(_binding.Health);
        Detach(); _game = null; _world = null; _retiredBody = null;
    }
    private static void Detach()
    {
        if (_binding != null) _binding.Health.HealthChangedEvent -= _binding.Callback;
        _binding = null;
    }
    private static Player PlayerGlobal => (Player)_playerProperty.GetValue(null);
    private static ActiveHealthController HealthGlobal => (ActiveHealthController)_healthProperty.GetValue(null);
    private static bool AwaitingStart(GameWorld world)
    {
        var player = PlayerGlobal;
        // The released mod never clears its globals on raid exit. A later Arena
        // entry-kit swap can therefore precede initialization with prior-raid refs.
        return player != null && player.GameWorld != null && !ReferenceEquals(player.GameWorld, world)
            && ReferenceEquals(HealthGlobal, player.ActiveHealthController);
    }
    private static void AddUnregister(MethodInfo target)
    {
        if (UnregisterTargets.Contains(target)) return;
        Patches.Patch(target, prefix: Hook(nameof(BeforeUnregister))); UnregisterTargets.Add(target);
    }
    private static HarmonyMethod Hook(string name) => new(typeof(HealingAutoCancelCompat), name) { priority = Priority.First };
    private static void Verify()
    {
        void Check(MethodInfo target, string name, int kind)
        {
            var hook = typeof(HealingAutoCancelCompat).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            var info = Harmony.GetPatchInfo(target);
            var list = kind == 0 ? info?.Prefixes : kind == 1 ? info?.Postfixes : info?.Finalizers;
            if (list?.Count(p => p.owner == Patches.Id && p.PatchMethod == hook) != 1)
                throw new MissingMethodException("Healing Autocancel compatibility hook is missing: " + name);
        }
        Check(_handler, nameof(BeforeHandler), 0); Check(_started, nameof(AfterStarted), 1);
        Check(_changeHealth, nameof(BeforeChangeHealth), 0); Check(_changeHealth, nameof(AfterChangeHealth), 2);
        foreach (var target in UnregisterTargets) Check(target, nameof(BeforeUnregister), 0);
    }
    private static void Clear()
    {
        Detach(); _game = null; _world = null; _retiredBody = null;
        if (_subscribed) { RespawnApi.Replaced -= Replaced; _subscribed = false; }
        Patches.UnpatchSelf(); UnregisterTargets.Clear(); _plugin = null; _version = null; _original = null;
        _playerProperty = _healthProperty = null; _setPlayer = _setHealth = _handler = _started = _changeHealth = _unregister = null;
    }
    private static void Reject(Exception ex)
    {
        _failure = "Healing Autocancel body replacement compatibility failed: " + ex.GetBaseException().Message + ".";
        if (_warning != _failure) { _warning = _failure; CoreLog.LogWarning(_failure); }
    }
    private static Version Normalize(Version value) => value == null || value.Build < 0 ? null
        : new Version(value.Major, value.Minor, value.Build, Math.Max(0, value.Revision));
    private static PropertyInfo Property(Type type, string name, Type result)
    {
        var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        if (property?.PropertyType != result || property.GetIndexParameters().Length != 0 || property.GetGetMethod() == null)
            throw new MissingMemberException(type.FullName, name);
        return property;
    }
    private static MethodInfo Method(Type type, string name, bool stat, Type result, params Type[] args)
    {
        var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.IsStatic != stat || method.ReturnType != result || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }
}

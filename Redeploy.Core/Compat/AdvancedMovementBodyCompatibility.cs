using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using EFT;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Retire only Advanced Movement state belonging to Core's outgoing physical body.</summary>
internal static class AdvancedMovementBodyCompatibility
{
    internal const string PluginGuid = "com.sage.advancedmovement";
    private static readonly Version Reviewed = new(1, 0, 1, 0);
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.AdvancedMovementCompat");
    private static readonly List<MethodInfo> UnregisterTargets = new();
    private static readonly MethodInfo Unregister = typeof(GameWorld).GetMethod(nameof(GameWorld.UnregisterPlayer), new[] { typeof(IPlayer) });
    private static Contract _contract;
    private static object _plugin;
    private static Version _version;
    private static LocalGame _game;
    private static GameWorld _world;
    private static LocalPlayer _body;
    private static string _failure, _warning;
    private static bool _newerWarned;

    internal static string GetRejectionReason()
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info))
        {
            Clear(); return _failure = null;
        }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            var version = Normalize(info.Metadata?.Version);
            if (_contract != null && ReferenceEquals(_plugin, info.Instance) && version == _version)
            { VerifyHooks(); return _failure; }
            if (_body != null) throw new InvalidOperationException("plugin changed during a prepared body replacement");
            Clear();
            var type = info.Instance.GetType();
            var metadata = type.GetCustomAttribute<BepInPlugin>(false);
            if (version == null || version < Reviewed) throw new NotSupportedException("version 1.0.1 or newer is required");
            if (type.FullName != "AdvancedMovement.Plugin" || type.Assembly.GetName().Name != "AdvancedMovement"
                || !typeof(BaseUnityPlugin).IsAssignableFrom(type) || info.Metadata.GUID != PluginGuid
                || metadata?.GUID != PluginGuid || Normalize(metadata.Version) != version)
                throw new InvalidOperationException("plugin identity or metadata changed");
            _contract = new Contract(type.Assembly);
            Patch(Unregister);
            _plugin = info.Instance; _version = version;
            VerifyHooks(); _failure = null;
            CoreLog.LogDebug("Advanced Movement: outgoing-body movement retirement ready.");
            if (version > Reviewed && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("Advanced Movement " + version + " is newer than reviewed 1.0.1; APIs match, native compatibility is unverified.");
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
            throw new InvalidOperationException("Advanced Movement requires Core's exact old body");
        // ClientGameWorld's override can skip base after native death already unregistered it.
        var target = game.GameWorld.GetType().GetMethod(nameof(GameWorld.UnregisterPlayer), new[] { typeof(IPlayer) });
        target = target?.DeclaringType?.GetMethod(nameof(GameWorld.UnregisterPlayer),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new[] { typeof(IPlayer) }, null);
        if (target == null || target.GetBaseDefinition() != Unregister || target.GetMethodBody() == null)
            throw new MissingMethodException("Advanced Movement cannot verify runtime world unregister");
        Patch(target); VerifyHooks();
        _game = game; _world = game.GameWorld; _body = body;
        // Read-only until the engine actually unregisters this body. A later adapter may refuse.
    }

    internal static void AbortPreparation() { _game = null; _world = null; _body = null; }
    internal static void EndAttempt() => AbortPreparation();
    internal static void RetireWorld(GameWorld world)
    {
        if (ReferenceEquals(world, _world)) AbortPreparation();
    }

    private static void BeforeUnregister(GameWorld __instance, IPlayer __0)
    {
        if (_contract == null || _body == null || !ReferenceEquals(__0, _body) || !ReferenceEquals(__instance, _world)
            || !BodyReplacementContext.IsCreating || !ReferenceEquals(BodyReplacementContext.PreviousBody, _body)
            || !BodyReplacementContext.OwnsRaid(_game, _world) || !BodyReplacementContext.OwnsBody(_body)) return;
        var body = _body;
        AbortPreparation(); // A base+override pair or a delayed unregister must not repeat retirement.
        try { _contract.Retire(body); }
        catch (Exception ex) { Reject(ex); }
    }

    private static MethodInfo Hook => typeof(AdvancedMovementBodyCompatibility).GetMethod(nameof(BeforeUnregister), BindingFlags.Static | BindingFlags.NonPublic);
    private static void Patch(MethodInfo target)
    {
        if (UnregisterTargets.Contains(target)) return;
        UnregisterTargets.Add(target); // Record before patch, including partial failure.
        Patches.Patch(target, prefix: new HarmonyMethod(Hook) { priority = Priority.First });
    }
    private static void VerifyHooks()
    {
        foreach (var target in UnregisterTargets)
            if (Harmony.GetPatchInfo(target)?.Prefixes.Count(p => p.owner == Patches.Id && p.PatchMethod == Hook) != 1)
                throw new MissingMethodException("Advanced Movement body-retirement hook is missing");
    }
    private static void Clear()
    {
        // Never walk Harmony's process-wide patch list, including the absent-plugin polling path.
        foreach (var target in UnregisterTargets) Patches.Unpatch(target, Hook);
        UnregisterTargets.Clear(); AbortPreparation(); _contract = null; _plugin = null; _version = null;
    }
    private static void Reject(Exception ex)
    {
        _failure = "Advanced Movement body replacement compatibility failed: " + ex.GetBaseException().Message + ".";
        if (_warning != _failure) { _warning = _failure; CoreLog.LogWarning(_failure); }
    }
    private static Version Normalize(Version v) => v == null || v.Build < 0 ? null
        : new Version(v.Major, v.Minor, v.Build, Math.Max(0, v.Revision));

    /// <summary>Resolved once, then a fixed amount of work per outgoing body; no frame hooks or scene searches.</summary>
    private sealed class Contract
    {
        private readonly FieldInfo _movePlayer, _move, _dive, _hold, _settleUntil, _settleLogged, _steadyTime;
        private readonly FieldInfo _airPlayer, _airQueued, _airWasInAir, _airVelocity, _airQueuedAt;
        private readonly FieldInfo _jumpContext, _jumpArmed, _jumpVelocity;
        private readonly FieldInfo _gesturePlayer, _bodyLocal, _pinFirearm;
        private readonly FieldInfo _audioPlayer, _audioGo, _audioLoop, _audioShots, _audioActive, _audioEasing;
        private readonly MethodInfo _gestureRestore, _gestureStop, _bodyStop, _pinRestore, _pinClear;
        private readonly PropertyInfo _bodyOwner;

        internal Contract(Assembly assembly)
        {
            Type T(string name) => assembly.GetType("AdvancedMovement." + name, true);
            var moves = T("Moves.MoveController");
            _movePlayer = Field(moves, "_player", typeof(Player));
            _move = Field(moves, "_move", T("States.ProfileMoveState"));
            _dive = Field(moves, "_dive", T("States.DiveState"));
            if (!typeof(BaseMovementState).IsAssignableFrom(_move.FieldType) || !typeof(BaseMovementState).IsAssignableFrom(_dive.FieldType))
                throw new InvalidOperationException("move state inheritance changed");
            _hold = Field(moves, "HoldStill", typeof(bool));
            _settleUntil = Field(moves, "_settleUntil", typeof(float));
            _settleLogged = Field(moves, "_settleLogged", typeof(bool));
            _steadyTime = Field(T("Moves.SteadyWeapon"), "_lastMoveTime", typeof(float));
            var air = T("Moves.AirSlide");
            _airPlayer = Field(air, "_player", typeof(Player));
            _airQueued = Field(air, "_queued", typeof(bool)); _airWasInAir = Field(air, "_wasInAir", typeof(bool));
            _airVelocity = Field(air, "_airVelocity", typeof(Vector3)); _airQueuedAt = Field(air, "_queuedAt", typeof(float));
            var jump = T("Moves.SlideJump");
            _jumpContext = Field(jump, "_context", typeof(MovementContext));
            _jumpArmed = Field(jump, "_armedAt", typeof(float)); _jumpVelocity = Field(jump, "_velocity", typeof(Vector3));
            var gesture = T("Viewmodel.GesturePlayer");
            _gesturePlayer = Field(gesture, "_player", typeof(Player));
            _gestureRestore = Method(gesture, "RestoreWeaponRoot"); _gestureStop = Method(gesture, "Stop");
            var animator = T("Body.BodyAnimator");
            _bodyLocal = Field(T("Body.BodyPlayer"), "_local", animator);
            _bodyOwner = animator.GetProperty("Player", BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
            if (_bodyOwner?.PropertyType != typeof(Player) || _bodyOwner.GetGetMethod()?.IsStatic != false)
                throw new MissingMemberException("Advanced Movement body animator owner changed");
            _bodyStop = Method(animator, "Stop", false);
            var pin = T("Viewmodel.WeaponPin");
            _pinFirearm = Field(pin, "_firearm", typeof(Player.FirearmController));
            _pinRestore = Method(pin, "Restore"); _pinClear = Method(pin, "Clear");
            var audio = T("Audio.SlideAudio");
            _audioPlayer = Field(audio, "_player", typeof(Player));
            _audioGo = Field(audio, "_go", typeof(GameObject));
            _audioLoop = Field(audio, "_loop", typeof(AudioSource)); _audioShots = Field(audio, "_shots", typeof(AudioSource));
            _audioActive = Field(audio, "_active", typeof(bool)); _audioEasing = Field(audio, "_easing", typeof(bool));
        }

        internal void Retire(Player body)
        {
            Exception failure = null;
            void Try(Action action) { try { action(); } catch (Exception ex) { failure ??= ex; } }
            var context = body.MovementContext;
            // Clear global Busy first, before invoking old-body native cleanup which may dispatch events.
            Try(() =>
            {
                if (!ReferenceEquals(_movePlayer.GetValue(null), body)) return;
                var move = _move.GetValue(null) as BaseMovementState;
                var dive = _dive.GetValue(null) as BaseMovementState;
                _move.SetValue(null, null); _dive.SetValue(null, null); _movePlayer.SetValue(null, null);
                _hold.SetValue(null, false); _settleUntil.SetValue(null, -100f); _settleLogged.SetValue(null, true);
                var animator = _bodyLocal.GetValue(null);
                if (animator == null || ReferenceEquals(_bodyOwner.GetValue(animator), body)) _steadyTime.SetValue(null, -100f);
                // ExitOverridenState calls the move's Exit and restores its saved speed limit.
                // Never cancel a newer/foreign override or run ExitMove/ExitDive, which may jump or go prone.
                if (context != null && context.OverridenState != null
                    && (ReferenceEquals(context.OverridenState, move) || ReferenceEquals(context.OverridenState, dive))
                    && ReferenceEquals(context.CurrentState, context.OverridenState))
                    context.ExitOverridenState();
            });
            Try(() =>
            {
                if (!ReferenceEquals(_airPlayer.GetValue(null), body)) return;
                _airQueued.SetValue(null, false); _airWasInAir.SetValue(null, false);
                _airVelocity.SetValue(null, Vector3.zero); _airQueuedAt.SetValue(null, 0f); _airPlayer.SetValue(null, null);
            });
            Try(() =>
            {
                if (context == null || !ReferenceEquals(_jumpContext.GetValue(null), context)) return;
                _jumpArmed.SetValue(null, -1f); _jumpVelocity.SetValue(null, Vector3.zero); _jumpContext.SetValue(null, null);
            });
            Try(() =>
            {
                if (!ReferenceEquals(_gesturePlayer.GetValue(null), body)) return;
                try { _gestureRestore.Invoke(null, null); }
                finally { _gestureStop.Invoke(null, null); }
            });
            Try(() =>
            {
                var local = _bodyLocal.GetValue(null);
                if (local == null || !ReferenceEquals(_bodyOwner.GetValue(local), body)) return;
                // Reset() would also clear the remote animator dictionary; stop just this local lease.
                try { _bodyStop.Invoke(local, null); }
                finally { if (ReferenceEquals(_bodyLocal.GetValue(null), local)) _bodyLocal.SetValue(null, null); }
            });
            Try(() =>
            {
                var firearm = _pinFirearm.GetValue(null) as Player.FirearmController;
                if (firearm == null || !ReferenceEquals(firearm._player, body)) return;
                try { _pinRestore.Invoke(null, null); }
                finally { _pinClear.Invoke(null, null); }
            });
            Try(() =>
            {
                if (!ReferenceEquals(_audioPlayer.GetValue(null), body)) return;
                var loop = _audioLoop.GetValue(null) as AudioSource;
                var shots = _audioShots.GetValue(null) as AudioSource;
                var go = _audioGo.GetValue(null) as GameObject;
                _audioActive.SetValue(null, false); _audioEasing.SetValue(null, false);
                _audioPlayer.SetValue(null, null); _audioGo.SetValue(null, null);
                _audioLoop.SetValue(null, null); _audioShots.SetValue(null, null);
                // Stop both sources synchronously; Destroy alone waits until the end of this frame.
                Try(() => { if (loop != null) loop.Stop(); });
                Try(() => { if (shots != null) shots.Stop(); });
                if (go != null) Object.Destroy(go);
            });
            if (failure != null) throw new InvalidOperationException("outgoing movement cleanup was incomplete", failure);
        }

        private static FieldInfo Field(Type type, string name, Type fieldType)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != fieldType || field.IsInitOnly || field.IsLiteral)
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
        private static MethodInfo Method(Type type, string name, bool isStatic = true)
        {
            var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly,
                null, Type.EmptyTypes, null);
            if (method == null || method.IsStatic != isStatic || method.ReturnType != typeof(void) || method.ContainsGenericParameters || method.GetMethodBody() == null)
                throw new MissingMethodException(type.FullName, name);
            return method;
        }
    }
}

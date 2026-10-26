using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Release only retired Core bodies from Quick Cancel's strong pending-controller set.</summary>
internal static class QuickCancelCompat
{
    internal const string PluginGuid = "com.manimal.quickcancel";
    private static readonly Version Reviewed = new(2, 0, 0, 0);
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.QuickCancelCompat");
    private static readonly ConditionalWeakTable<Player, object> Retired = new();
    private static readonly List<MethodInfo> UnregisterTargets = new();
    private static object _instance;
    private static Version _version;
    private static FieldInfo _pending;
    private static MethodInfo _mark, _unregister;
    private static string _failure, _warning;
    private static bool _newerWarned;

    internal static string GetRejectionReason()
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info))
        { Clear(); return _failure = null; }
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            var version = Normalize(info.Metadata?.Version);
            if (ReferenceEquals(_instance, info.Instance) && version == _version)
            { Verify(); return _failure; }
            Clear();
            var type = info.Instance.GetType();
            var metadata = type.GetCustomAttribute<BepInPlugin>(false);
            if (version == null || version < Reviewed) throw new NotSupportedException("version 2.0.0 or newer is required");
            if (type.FullName != "Manimal.QuickCancel.Plugin" || type.Assembly.GetName().Name != "QuickCancelClient"
                || !typeof(BaseUnityPlugin).IsAssignableFrom(type) || info.Metadata.GUID != PluginGuid
                || metadata?.GUID != PluginGuid || Normalize(metadata.Version) != version)
                throw new InvalidOperationException("plugin identity or metadata changed");
            var state = type.Assembly.GetType("Manimal.QuickCancel.QuickCancelState", true);
            _pending = state.GetField("PendingControllers", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (_pending == null || !_pending.IsInitOnly || _pending.FieldType != typeof(HashSet<Player.MedsController>))
                throw new MissingFieldException("Quick Cancel pending-controller set changed");
            _ = Pending;
            _mark = Method(state, "Mark", true, typeof(bool), typeof(Player.MedsController));
            _unregister = Method(typeof(GameWorld), nameof(GameWorld.UnregisterPlayer), false, typeof(void), typeof(IPlayer));
            Patches.Patch(_mark, prefix: Hook(nameof(BeforeMark)));
            AddUnregister(_unregister);
            _instance = info.Instance; _version = version;
            Verify(); _failure = null;
            CoreLog.LogDebug("Quick Cancel: retired Core-body pending-controller cleanup ready.");
            if (version > Reviewed && !_newerWarned)
            {
                _newerWarned = true;
                CoreLog.LogWarning("Quick Cancel " + version + " is newer than reviewed 2.0.0; APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex) { Clear(); Reject(ex); }
        return _failure;
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        if (_pending == null) return;
        if (!BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, oldBody) || !BodyReplacementContext.OwnsBody(oldBody))
            throw new InvalidOperationException("Quick Cancel requires Core's exact old body");
        // The concrete client override can return without calling the base method
        // when native death has already unregistered this player. Hook its entry.
        var target = game.GameWorld.GetType().GetMethod(nameof(GameWorld.UnregisterPlayer), new[] { typeof(IPlayer) });
        target = target?.DeclaringType?.GetMethod(nameof(GameWorld.UnregisterPlayer),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, new[] { typeof(IPlayer) }, null);
        if (target == null || target.GetBaseDefinition() != _unregister || target.GetMethodBody() == null)
            throw new MissingMethodException("Quick Cancel cannot verify runtime world unregister");
        AddUnregister(target); Verify();
        // Preparation is read-only. A later adapter can still refuse replacement.
    }
    private static HashSet<Player.MedsController> Pending => _pending?.GetValue(null) as HashSet<Player.MedsController>
        ?? throw new InvalidOperationException("Quick Cancel pending-controller set is unavailable");
    private static void BeforeUnregister(GameWorld __instance, IPlayer __0)
    {
        if (_pending == null || __0 is not Player body || !BodyReplacementContext.IsCreating
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, body)
            || !BodyReplacementContext.OwnsBody(body)
            || !BodyReplacementContext.OwnsRaid(Singleton<AbstractGame>.Instance as LocalGame, __instance)) return;
        try { Retire(body); }
        catch (Exception ex) { Reject(ex); }
    }
    private static void Retire(Player body)
    {
        // Reference ownership survives Unity's destroyed-object equality. Do not
        // clear another raid's, a bot's, or the replacement body's pending work.
        if (!Retired.TryGetValue(body, out _)) Retired.Add(body, new object());
        Pending.RemoveWhere(controller => !ReferenceEquals(controller, null) && ReferenceEquals(controller._player, body));
    }
    private static bool BeforeMark(Player.MedsController __0, ref bool __result)
    {
        if (ReferenceEquals(__0, null) || ReferenceEquals(__0._player, null)
            || !Retired.TryGetValue(__0._player, out _)) return true;
        __result = false;
        return false;
    }
    internal static void RetireWorld(GameWorld world)
    {
        if (_pending == null || ReferenceEquals(world, null)) return;
        try
        {
            foreach (var controller in Pending.ToArray())
            {
                var body = ReferenceEquals(controller, null) ? null : controller._player;
                if (BodyReplacementContext.OwnsBody(body) && ReferenceEquals(body.GameWorld, world)) Retire(body);
            }
        }
        catch (Exception ex) { Reject(ex); }
    }
    private static void AddUnregister(MethodInfo target)
    {
        if (UnregisterTargets.Contains(target)) return;
        Patches.Patch(target, prefix: Hook(nameof(BeforeUnregister))); UnregisterTargets.Add(target);
    }
    private static HarmonyMethod Hook(string name) => new(typeof(QuickCancelCompat), name) { priority = Priority.First };
    private static void Verify()
    {
        _ = Pending;
        void Check(MethodInfo method, string name)
        {
            var hook = typeof(QuickCancelCompat).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            if (Harmony.GetPatchInfo(method)?.Prefixes.Count(p => p.owner == Patches.Id && p.PatchMethod == hook) != 1)
                throw new MissingMethodException("Quick Cancel compatibility hook is missing: " + name);
        }
        Check(_mark, nameof(BeforeMark));
        foreach (var target in UnregisterTargets) Check(target, nameof(BeforeUnregister));
    }
    private static void Clear()
    {
        Patches.UnpatchSelf(); UnregisterTargets.Clear(); _instance = null; _version = null; _pending = null; _mark = _unregister = null;
    }
    private static void Reject(Exception ex)
    {
        _failure = "Quick Cancel body replacement compatibility failed: " + ex.GetBaseException().Message + ".";
        if (_warning != _failure) { _warning = _failure; CoreLog.LogWarning(_failure); }
    }
    private static Version Normalize(Version value) => value == null || value.Build < 0 ? null
        : new Version(value.Major, value.Minor, value.Build, Math.Max(0, value.Revision));
    private static MethodInfo Method(Type type, string name, bool stat, Type result, params Type[] args)
    {
        var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.IsStatic != stat || method.ReturnType != result || method.ContainsGenericParameters || method.GetMethodBody() == null)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }
}

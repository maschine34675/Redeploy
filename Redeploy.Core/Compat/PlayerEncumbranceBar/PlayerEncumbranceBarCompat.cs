using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using EFT;
using EFT.HealthSystem;
using EFT.UI;
using HarmonyLib;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>
/// The bar has a private static health controller but takes its event sources from the
/// backend PMC. A Core replacement may carry another profile (notably the own Scav).
/// Bind its existing Show subscriptions to that exact physical body's profile instead.
/// No initial raid, bot or menu profile is adopted merely because its id matches.
/// </summary>
internal static class PlayerEncumbranceBarCompat
{
    internal const string PluginGuid = "com.mpstark.PlayerEncumbranceBar";
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.PlayerEncumbranceBar");
    private static object _plugin;
    private static Version _version;
    private static string _failure;
    private static bool _patched, _warned;
    private static FieldInfo _bar, _health, _ui;
    private static MethodInfo _show;
    private static readonly ConditionalWeakTable<UIElement, object> RetiredBars = new();
    private static UIElement _ownedBar, _pendingBar;
    private static IHealthController _ownedHealth, _pendingHealth;
    private static GameWorld _world, _pendingWorld;
    private static LocalGame _pendingGame;
    private static LocalPlayer _pendingBody;
    private static Profile _pendingProfile;
    private static UIElement _previousOwnedBar;
    private static IHealthController _previousOwnedHealth;
    private static GameWorld _previousWorld;
    private static long _showEpoch, _pendingShowEpoch;
    private static bool _closeStarted, _pendingWasVisible, _pendingHadSubscriptions, _pendingWasRetired, _rollbackWarned;

    internal static string GetRejectionReason()
    {
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info)) return null;
        if (info?.Instance == null) return "Player Encumbrance Bar has not finished loading.";
        var version = info.Metadata?.Version;
        if (ReferenceEquals(_plugin, info.Instance) && Equals(_version, version)) return _failure;
        if (_plugin != null) return _failure = "Player Encumbrance Bar changed identity during this process.";
        _plugin = info.Instance;
        _version = version;
        try
        {
            var type = info.Instance.GetType();
            var metadata = type.GetCustomAttribute<BepInPlugin>();
            if (type.FullName != "PlayerEncumbranceBar.Plugin" || type.Assembly.GetName().Name != "PlayerEncumbranceBar"
                || metadata?.GUID != PluginGuid || metadata.Version != version || version == null || version < new Version(1, 3, 0))
                throw new NotSupportedException("expected Player Encumbrance Bar 1.3.0 or newer");
            var component = type.Assembly.GetType("PlayerEncumbranceBar.PlayerEncumbranceBarComponent", true);
            var utils = type.Assembly.GetType("PlayerEncumbranceBar.Utils.GameUtils", true);
            if (!typeof(UIElement).IsAssignableFrom(component)) throw new InvalidOperationException("bar is no longer a UIElement");
            _bar = Field(type, "PlayerEncumbranceBar", component, false);
            _health = Field(component, "_healthController", typeof(IHealthController), true);
            _ui = Field(typeof(UIElement), "UI", typeof(UIParent), false);
            var profile = Method(utils, "get_PlayerProfile", true, typeof(Profile));
            _show = Method(component, "Show", false, typeof(void), typeof(IHealthController));
            var update = Method(component, "UpdateWeight", false, typeof(void), typeof(bool));
            var limits = Method(component, "UpdateWeightLimits", false, typeof(void), typeof(bool));
            _patched = true;
            Patches.Patch(profile, prefix: Hook(nameof(BeforeProfile)));
            Patches.Patch(_show, prefix: Hook(nameof(BeforeShow)), postfix: Hook(nameof(AfterShow)));
            Patches.Patch(update, prefix: Hook(nameof(BeforeUpdate)));
            Patches.Patch(limits, prefix: Hook(nameof(BeforeUpdate)));
            if (version > new Version(1, 3, 0) && !_warned)
            {
                _warned = true;
                CoreLog.LogWarning("Player Encumbrance Bar " + version + " is newer than reviewed 1.3.0; its body-binding APIs match, native compatibility is unverified.");
            }
        }
        catch (Exception ex)
        {
            if (_patched) { Patches.UnpatchSelf(); _patched = false; }
            _failure = "Player Encumbrance Bar body binding is unavailable: " + ex.GetBaseException().Message;
        }
        return _failure;
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        ForgetPreparation();
        if (_health == null || _failure != null) return;
        if (!BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, oldBody) || !BodyReplacementContext.OwnsBody(oldBody))
            throw new InvalidOperationException("Player Encumbrance Bar requires Core's exact outgoing body");
        var bar = _bar.GetValue(_plugin) as UIElement;
        // Menu health and a bar not yet created are not outgoing-body subscriptions.
        if (bar == null || !ReferenceEquals(_health.GetValue(null), oldBody.HealthController)) return;
        _pendingBar = bar;
        _pendingHealth = oldBody.HealthController;
        _pendingWorld = game.GameWorld;
        _pendingGame = game;
        _pendingBody = oldBody;
        _pendingProfile = oldBody.Profile;
        _pendingShowEpoch = _showEpoch;
        _pendingWasVisible = bar.GameObject?.activeSelf == true;
        var ui = (UIParent)_ui.GetValue(bar);
        _pendingHadSubscriptions = ui._oneCase != null || ui._destroys?.Count > 0;
        _pendingWasRetired = RetiredBars.TryGetValue(bar, out _);
        _previousOwnedBar = _ownedBar;
        _previousOwnedHealth = _ownedHealth;
        _previousWorld = _world;
    }

    internal static void CompletePreparation()
    {
        if (!_closeStarted && _showEpoch == _pendingShowEpoch && _pendingBar != null && ReferenceEquals(_bar.GetValue(_plugin), _pendingBar)
            && ReferenceEquals(_health.GetValue(null), _pendingHealth))
        {
            // Keep rollback evidence until the engine either commits or cancels.
            // Dispose can partially remove subscriptions before throwing, and a
            // subsequent preparation step can fail after Dispose has succeeded.
            _closeStarted = true;
            _ownedBar = _pendingBar;
            _ownedHealth = _pendingHealth;
            _world = _pendingWorld;
            // Use the foreign UI's own disposables before replacing its private static
            // health reference. This detaches its strength, weight and effect callbacks.
            _ownedBar.Dispose();
            _health.SetValue(null, null);
            if (!RetiredBars.TryGetValue(_ownedBar, out _)) RetiredBars.Add(_ownedBar, new object());
        }
    }

    internal static void AbortPreparation()
    {
        try
        {
            if (!_closeStarted || !CanRestorePreparation() || _showEpoch != _pendingShowEpoch) return;
            if (_pendingHadSubscriptions)
            {
                // Its own Show finishes any partial disposal before reattaching
                // the old inventory, strength and health subscriptions. The
                // temporary Core context still owns this exact old body here.
                _show.Invoke(_pendingBar, new object[] { _pendingHealth });
                if (!CanRestorePreparation() || _showEpoch != _pendingShowEpoch + 1) return;
            }
            else _health.SetValue(null, _pendingHealth);
            // Hidden does not mean unsubscribed: HideGameObject leaves the
            // native disposable collection intact. Preserve both independently.
            if (_pendingWasVisible) _pendingBar.ShowGameObject();
            else _pendingBar.HideGameObject();

            // Show's postfix temporarily adopts the old body. Restore the prior
            // adapter lease as well, before Core cancels its provisional context;
            // otherwise the first cancelled swap would freeze an ordinary bar.
            _ownedBar = _previousOwnedBar;
            _ownedHealth = _previousOwnedHealth;
            _world = _previousWorld;
            if (_pendingWasRetired)
            { if (!RetiredBars.TryGetValue(_pendingBar, out _)) RetiredBars.Add(_pendingBar, new object()); }
            else RetiredBars.Remove(_pendingBar);
        }
        catch (Exception ex)
        {
            // A foreign UI restoration failure must never prevent Core from
            // cancelling its context and releasing the surviving player's input.
            if (!_rollbackWarned)
            {
                _rollbackWarned = true;
                CoreLog.LogWarning("Player Encumbrance Bar could not restore its display after a refused body switch: "
                    + ex.GetBaseException().Message);
            }
        }
        finally { ForgetPreparation(); }
    }

    private static bool CanRestorePreparation()
        => _pendingBar != null && _pendingBody != null && _pendingBody.HealthController?.IsAlive == true
           && BodyReplacementContext.IsCreating
           && BodyReplacementContext.OwnsRaid(_pendingGame, _pendingWorld)
           && ReferenceEquals(BodyReplacementContext.PreviousBody, _pendingBody)
           && ReferenceEquals(_pendingGame.LocalPlayer, _pendingBody)
           && ReferenceEquals(_pendingWorld.MainPlayer, _pendingBody)
           && ReferenceEquals(_pendingGame.PlayerOwner?.Player, _pendingBody)
           && ReferenceEquals(_pendingBody.GameWorld, _pendingWorld)
           && ReferenceEquals(_pendingBody.Profile, _pendingProfile)
           && ReplacementProfileRule.CarriesRaidProfile(_pendingProfile, _pendingGame.Profile, null)
           && ReferenceEquals(_pendingBody.HealthController, _pendingHealth)
           && ReferenceEquals(_bar.GetValue(_plugin), _pendingBar)
           && (_health.GetValue(null) == null || ReferenceEquals(_health.GetValue(null), _pendingHealth));

    // Success and teardown failure both follow Core's authoritative body state.
    // Never reopen the outgoing UI after the engine has begun replacing it.
    internal static void EndAttempt() => ForgetPreparation();
    private static void ForgetPreparation()
    {
        _pendingBar = null; _pendingHealth = null; _pendingWorld = null;
        _pendingGame = null; _pendingBody = null; _pendingProfile = null; _closeStarted = false;
        _previousOwnedBar = null; _previousOwnedHealth = null; _previousWorld = null;
        _pendingWasVisible = false; _pendingHadSubscriptions = false; _pendingWasRetired = false;
    }

    private static Player Current(IHealthController health)
    {
        var game = BodyReplacementContext.CurrentGame;
        var world = BodyReplacementContext.CurrentWorld;
        var body = world?.MainPlayer;
        return health != null && body != null && body is LocalPlayer && body.IsYourPlayer && !body.IsAI
            && BodyReplacementContext.OwnsRaid(game, world) && BodyReplacementContext.OwnsBody(body)
            && ReferenceEquals(game.LocalPlayer, body) && ReferenceEquals(body.GameWorld, world)
            && ReferenceEquals(body.Profile, game.Profile) && ReferenceEquals(body.HealthController, health)
            && ReferenceEquals(game.PlayerOwner?.Player, body) ? body : null;
    }

    private static bool BeforeProfile(ref Profile __result)
    {
        var body = Current(_health?.GetValue(null) as IHealthController);
        if (body == null) return true;
        __result = body.Profile;
        return false;
    }

    private static void BeforeShow(UIElement __instance)
    {
        _showEpoch++;
        // An intentional new display owns its initial update, including a later menu
        // screen. Weak tombstones fence only callbacks left over from the closed UI.
        RetiredBars.Remove(__instance);
        if (ReferenceEquals(_ownedBar, __instance)) { _ownedBar = null; _ownedHealth = null; _world = null; }
    }

    private static void AfterShow(UIElement __instance)
    {
        if (!ReferenceEquals(_bar.GetValue(_plugin), __instance)) return;
        var body = Current(_health.GetValue(null) as IHealthController);
        // A menu/UI refresh during a provisional first preparation must not
        // acquire a permanent lease on its still-original player. Cancellation
        // removes that context; the refreshed ordinary display remains native.
        if (BodyReplacementContext.IsCreating && ReferenceEquals(body, BodyReplacementContext.PreviousBody)) return;
        if (body != null) { _ownedBar = __instance; _ownedHealth = body.HealthController; _world = body.GameWorld; }
    }

    private static bool BeforeUpdate(UIElement __instance)
        => !RetiredBars.TryGetValue(__instance, out _)
           && (!ReferenceEquals(_ownedBar, __instance) || Current(_health.GetValue(null) as IHealthController) != null);

    internal static void RetireWorld(GameWorld world)
    {
        if (ReferenceEquals(world, _pendingWorld)) ForgetPreparation();
        if (!ReferenceEquals(world, _world)) return;
        if (_ownedBar != null && ReferenceEquals(_bar.GetValue(_plugin), _ownedBar)
            && ReferenceEquals(_health.GetValue(null), _ownedHealth))
        {
            _ownedBar.Dispose();
            _health.SetValue(null, null);
            if (!RetiredBars.TryGetValue(_ownedBar, out _)) RetiredBars.Add(_ownedBar, new object());
        }
        _ownedBar = null;
        _ownedHealth = null;
        _world = null;
    }

    private static HarmonyMethod Hook(string name) => new(typeof(PlayerEncumbranceBarCompat), name);
    private static FieldInfo Field(Type type, string name, Type expected, bool isStatic)
    {
        var field = AccessTools.DeclaredField(type, name);
        if (field == null || field.FieldType != expected || field.IsStatic != isStatic)
            throw new MissingFieldException(type.FullName, name);
        return field;
    }
    private static MethodInfo Method(Type type, string name, bool isStatic, Type returns, params Type[] args)
    {
        var method = AccessTools.DeclaredMethod(type, name, args);
        if (method == null || method.IsStatic != isStatic || method.ReturnType != returns || method.ContainsGenericParameters)
            throw new MissingMethodException(type.FullName, name);
        return method;
    }
}

using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;

using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Rebind continuous loading to exact Core replacement bodies and retire delayed work.</summary>
internal static class ContinuousLoadAmmoCompat
{
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.ContinuousLoadAmmoCompat");
    private static readonly ContinuousLoadAmmoScope Scope = new();
    private static ContinuousLoadAmmoContract _contract;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static bool _initialized, _patchAttempted, _runtimeFault, _reportedNewer;
    private static string _failure, _lastWarning;
    private static int _reported;
    [ThreadStatic] private static DisposeFrame _disposing;
    private sealed class DisposeFrame { internal object Controller; internal DisposeFrame Previous; }

    internal static void Initialize() { if (_initialized) return; _initialized = true; Refresh(); }
    internal static string GetRejectionReason() { if (!_initialized) Initialize(); else Refresh(); return _failure; }
    private static void Refresh()
    {
        if (!Chainloader.PluginInfos.TryGetValue(ContinuousLoadAmmoContract.PluginGuid, out var info))
        { ClearBinding(); _failure = null; _runtimeFault = false; return; }
        if (_runtimeFault) return;
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Continuous Load Ammo has not finished loading");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance) && _version == ContinuousLoadAmmoContract.Normalize(info.Metadata?.Version))
            { _contract.VerifyNativeBindings(); VerifyOwnPatches(); return; }
            ClearBinding();
            var contract = ContinuousLoadAmmoContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version, out var version);
            _patchAttempted = true;
            Patch(contract.Register, nameof(BeforeRegister), finalizer: nameof(AfterRegister));
            Patch(contract.Dispose, nameof(BeforeDispose), finalizer: nameof(AfterDispose));
            Patch(contract.OnDispose, nameof(BeforeStaticDispose));
            Patch(contract.State, nameof(BeforeState), nameof(AfterState));
            Patch(contract.RemoveLimit, postfix: nameof(AfterLimitRemoved));
            Patch(contract.Outside, nameof(BeforeOutside));
            Patch(contract.EndPostfix, nameof(BeforeEndBroadcast));
            foreach (var start in contract.Starts) Patch(start, postfix: nameof(AfterProcessStart));
            _contract = contract; _plugin = info.Instance; _version = version;
            VerifyOwnPatches(); _failure = null;
            CoreLog.LogDebug("Redeploy Continuous Load Ammo: exact-controller rebind and delayed completion guards ready.");
            if (version > ContinuousLoadAmmoContract.ReviewedVersion && !_reportedNewer)
            { _reportedNewer = true; CoreLog.LogWarning("Redeploy Continuous Load Ammo " + version + " is newer than reviewed 1.2.0; its APIs were checked, but runtime compatibility is unverified."); }
        }
        catch (Exception ex) { ClearBinding(); Reject(ex); }
    }
    private static void Patch(MethodInfo target, string prefix = null, string postfix = null, string finalizer = null)
        => Patches.Patch(target, prefix == null ? null : Hook(prefix), postfix == null ? null : Hook(postfix), finalizer: finalizer == null ? null : Hook(finalizer));
    private static HarmonyMethod Hook(string name) => new(typeof(ContinuousLoadAmmoCompat), name) { priority = Priority.First };
    private static void VerifyOwnPatches()
    {
        void Check(MethodInfo target, string name, string kind)
        {
            var info = Harmony.GetPatchInfo(target);
            var entries = kind == "prefix" ? info?.Prefixes : kind == "postfix" ? info?.Postfixes : info?.Finalizers;
            var method = typeof(ContinuousLoadAmmoCompat).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            if (entries == null || entries.Count(p => p.owner == Patches.Id && p.PatchMethod == method) != 1)
                throw new InvalidOperationException("Continuous Load Ammo compatibility hook missing: " + name);
        }
        Check(_contract.Register, nameof(BeforeRegister), "prefix"); Check(_contract.Register, nameof(AfterRegister), "finalizer");
        Check(_contract.Dispose, nameof(BeforeDispose), "prefix"); Check(_contract.Dispose, nameof(AfterDispose), "finalizer");
        Check(_contract.OnDispose, nameof(BeforeStaticDispose), "prefix"); Check(_contract.State, nameof(BeforeState), "prefix");
        Check(_contract.State, nameof(AfterState), "postfix"); Check(_contract.RemoveLimit, nameof(AfterLimitRemoved), "postfix");
        Check(_contract.Outside, nameof(BeforeOutside), "prefix"); Check(_contract.EndPostfix, nameof(BeforeEndBroadcast), "prefix");
        foreach (var start in _contract.Starts)
        {
            Check(start, nameof(AfterProcessStart), "postfix");
            var capture = typeof(ContinuousLoadAmmoCompat).GetMethod(nameof(AfterProcessStart), BindingFlags.NonPublic | BindingFlags.Static);
            var sorted = PatchProcessor.GetSortedPatchMethods(start, Harmony.GetPatchInfo(start).Postfixes.ToArray());
            // A completed Task can broadcast synchronously in the foreign postfix.
            // Check the real priority + before/after graph, not Priority.First alone.
            int captureIndex = sorted.IndexOf(capture), broadcastIndex = sorted.IndexOf(_contract.EndPostfix);
            if (captureIndex < 0 || broadcastIndex < 0 || captureIndex >= broadcastIndex)
                throw new InvalidOperationException("Continuous Load Ammo completion capture must run before its broadcast postfix");
        }
    }

    private static bool ExactRaid(GameWorld world)
        => BodyReplacementContext.OwnsRaid(BodyReplacementContext.CurrentGame, world);

    // Preparation is deliberately non-destructive. Another adapter may still reject the swap.
    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        var rejection = GetRejectionReason();
        if (rejection != null) throw new InvalidOperationException(rejection);
        if (_contract == null) return;
        if (!ExactRaid(game?.GameWorld) || !BodyReplacementContext.OwnsBody(oldBody)
            || !ReferenceEquals(game.LocalPlayer, oldBody) || !ReferenceEquals(game.GameWorld.MainPlayer, oldBody))
            throw new InvalidOperationException("Continuous Load Ammo cannot verify the old replacement body.");
        var controller = _contract.Current.GetValue(null);
        if (controller != null && (!ReferenceEquals(_contract.Body.GetValue(controller), oldBody)
            || !ReferenceEquals(_contract.Inventory.GetValue(controller), oldBody.InventoryController)))
            throw new InvalidOperationException("Continuous Load Ammo is bound to an unrelated player or inventory.");
        if (Scope.Find(controller) == null && Scope.HasUncertainLimit(controller))
            throw new InvalidOperationException("Continuous Load Ammo has an unverified or shared movement restriction. Finish loading and leave the restricting area before replacing the body.");
        _preparedController = controller;
        _preparedBody = oldBody;
    }

    internal static void AbortPreparation() { _preparedController = null; _preparedBody = null; }

    private static object _preparedController, _ownedController;
    private static LocalPlayer _preparedBody;

    internal static void Tick()
    {
        if (_contract == null || BodyReplacementContext.IsCreating) return;
        _preparedController = null; _preparedBody = null;
        try
        {
            var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
            var lease = Scope.Find(_ownedController);
            if (lease != null && !lease.Disposed && (!ExactRaid(world)
                || !ReferenceEquals(world.MainPlayer, lease.Body)))
                _contract.Dispose.Invoke(lease.Controller, null);
            if (lease?.Disposed == true) _ownedController = null;
            // A failed live swap can restore the previous body. The foreign controller was
            // already disposed before the candidate registered, so recreate it for that exact
            // restored body only. This never claims a normal raid's first player.
            var game = BodyReplacementContext.CurrentGame;
            if (!_runtimeFault && ExactRaid(world) && world.MainPlayer is LocalPlayer body
                && body.IsYourPlayer && body.HealthController?.IsAlive == true
                && ReferenceEquals(game.LocalPlayer, body) && BodyReplacementContext.OwnsBody(body)
                && _contract.Current.GetValue(null) == null)
                _contract.Register.Invoke(null, new object[] { world, body });
        }
        catch (Exception ex) { Fault(ex); }
    }

    private static bool BeforeRegister(GameWorld __0, IPlayer __1, out bool __state)
    {
        __state = false;
        if (_contract == null || __0 is HideoutGameWorld || __1 is not LocalPlayer body || !body.IsYourPlayer) return true;
        try
        {
            if (!ReferenceEquals(body.GameWorld, __0) || !ReferenceEquals(__0.MainPlayer, body) || !ExactRaid(__0)) return true;
            bool candidate = BodyReplacementContext.TryOwnCandidate(body);
            bool restored = !BodyReplacementContext.IsCreating && BodyReplacementContext.OwnsBody(body)
                && ReferenceEquals(BodyReplacementContext.CurrentGame.LocalPlayer, body);
            if (!candidate && !restored) return true;
            var old = _contract.Current.GetValue(null); var lease = Scope.Find(old);
            if (old != null && lease == null)
            {
                // The original standalone spawn was never modified. Adopt its exact controller
                // only now, immediately before replacing it, after every preflight has passed.
                if (!candidate || !ReferenceEquals(old, _preparedController) || _preparedBody == null
                    || !ReferenceEquals(_contract.Body.GetValue(old), _preparedBody)
                    || !ReferenceEquals(_contract.Inventory.GetValue(old), _preparedBody.InventoryController))
                    throw new InvalidOperationException("Continuous Load Ammo controller changed during replacement.");
                lease = Scope.BindController(old, _preparedBody, __0, _preparedBody.InventoryController,
                    __0, _preparedBody, ExactRaid(__0), _preparedBody.IsYourPlayer, _preparedBody.MovementContext);
                if (lease == null) throw new InvalidOperationException("Continuous Load Ammo could not adopt the entry controller.");
            }
            if (lease != null && !lease.Disposed && ReferenceEquals(lease.Body, body) && ReferenceEquals(lease.Inventory, body.InventoryController)) return false;
            // Dispose before upstream replaces its static pointer; its own UI and preset cancellation stay authoritative.
            if (lease != null && !lease.Disposed) _contract.Dispose.Invoke(old, null);
            __state = true; return true;
        }
        catch (Exception ex) { Fault(ex); return false; }
    }
    private static Exception AfterRegister(GameWorld __0, IPlayer __1, bool __state, Exception __exception)
    {
        if (_contract == null || !__state || __1 is not Player body) return __exception;
        try
        {
            var controller = _contract.Current.GetValue(null);
            if (controller == null || !ReferenceEquals(_contract.Body.GetValue(controller), body)
                || !ReferenceEquals(_contract.Inventory.GetValue(controller), body.InventoryController))
                throw new InvalidOperationException("Continuous Load Ammo registered an unrelated controller");
            var lease = Scope.BindController(controller, body, body.GameWorld, body.InventoryController, __0, __0.MainPlayer, ExactRaid(__0), body.IsYourPlayer, body.MovementContext);
            if (lease == null) throw new InvalidOperationException("Continuous Load Ammo registration lost exact replacement ownership");
            // The upstream global completion event has no body identity. Core routes only the originating native process instead.
            _contract.EndEvent.RemoveEventHandler(null, Delegate.CreateDelegate(typeof(Action), controller, _contract.LoadingEnd));
            _ownedController = controller;
            if (__exception != null)
            {
                // Foreign UI setup can throw after its static controller was assigned. A
                // finalizer still owns and retires that partially registered exact candidate.
                _contract.Dispose.Invoke(controller, null);
                Fault(__exception);
            }
            else Report(1, "bound current replacement inventory controller");
        }
        catch (Exception ex) { Fault(__exception ?? ex); return __exception ?? ex; }
        return __exception;
    }
    private static bool BeforeDispose(object __instance, out DisposeFrame __state)
    {
        __state = null;
        if (_contract == null) return true;
        var lease = Scope.Find(__instance);
        if (lease != null && !ContinuousLoadAmmoScope.Retire(lease)) return false;
        __state = new DisposeFrame { Controller = __instance, Previous = _disposing }; _disposing = __state;
        if (lease != null)
        {
            // Upstream skips these removals when Unity already regards its body as destroyed.
            _contract.EndEvent.RemoveEventHandler(null, Delegate.CreateDelegate(typeof(Action), __instance, _contract.LoadingEnd));
            _contract.CloseEvent.RemoveEventHandler(null, Delegate.CreateDelegate(typeof(Action), __instance, _contract.Outside));
            Report(2, "retired old replacement controller and its global event subscriptions");
        }
        return true;
    }
    private static Exception AfterDispose(Exception __exception, DisposeFrame __state)
    {
        try
        {
            var lease = __state == null ? null : Scope.Find(__state.Controller);
            if (lease?.HasLimit == true && lease.Body is Player oldBody && oldBody != null
                && oldBody.HealthController?.IsAlive == true && oldBody.MovementContext != null
                && ReferenceEquals(lease.Movement, oldBody.MovementContext))
            {
                // Teardown can be followed by recovery onto the old live body. Release only
                // the observed CLA limit on its exact movement context; never an unrelated limit.
                oldBody.MovementContext.SetPhysicalCondition(EPhysicalCondition.SprintDisabled, false);
                oldBody.MovementContext.RemoveStateSpeedLimit(Player.ESpeedLimit.BarbedWire);
                lease.HasLimit = false;
            }
            if (__exception != null && __state != null && _contract != null && lease != null)
            {
                // Never leave an already-retired controller in the global slot after partial
                // foreign teardown. The newer controller, if any, is protected by identity.
                if (ReferenceEquals(_contract.Current.GetValue(null), __state.Controller))
                    _contract.OnDispose.Invoke(null, null);
                Fault(__exception);
            }
        }
        catch (Exception ex) { Fault(ex); }
        finally { if (__state != null) _disposing = __state.Previous; }
        return __exception;
    }
    private static bool BeforeStaticDispose()
    {
        if (_contract == null) return true;
        var current = _contract.Current.GetValue(null); var disposing = _disposing?.Controller;
        return ContinuousLoadAmmoScope.MayClearStatic(disposing, current, Scope.Find(disposing) != null, Scope.Find(current) != null);
    }
    private static bool Current(ContinuousLoadAmmoScope.Lease lease)
    {
        if (_contract == null || lease == null || !ReferenceEquals(Scope.Find(lease.Controller), lease)
            || lease.Body is not Player body || body == null) return false;
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        return ContinuousLoadAmmoScope.IsCurrent(lease, _contract.Current.GetValue(null), world, world?.MainPlayer,
            body.InventoryController, body.HealthController?.IsAlive == true, ExactRaid(world), body.MovementContext);
    }
    private static bool BeforeOutside(object __instance)
        => _contract == null || Scope.Find(__instance) is not { } lease || Current(lease);
    private static bool BeforeState(object __instance, bool __0, ref Task __result, out ContinuousLoadAmmoScope.LimitCall __state)
    {
        __state = null;
        var lease = _contract == null ? null : Scope.Find(__instance);
        if (lease == null)
        {
            // Observe the initial standalone controller without replacing its Task or actions.
            // A weak stamp proves the actual upstream true branch applied this limit later.
            try
            {
                if (_contract != null && __0 && _contract.Body.GetValue(__instance) is LocalPlayer observedBody
                    && observedBody != null && observedBody.IsYourPlayer && observedBody.GameWorld is not HideoutGameWorld
                    && observedBody.MovementContext != null && ReferenceEquals(_contract.Inventory.GetValue(__instance), observedBody.InventoryController))
                    __state = Scope.ObserveLimitStart(__instance, observedBody, observedBody.GameWorld, observedBody.InventoryController,
                        observedBody.MovementContext, observedBody.MovementContext._speedLimits.ContainsKey(Player.ESpeedLimit.BarbedWire));
            }
            catch (Exception ex) { Fault(ex); } // Observation failure never prevents the ordinary action.
            return true;
        }
        __result = Task.CompletedTask;
        if (!Current(lease)) return false;
        long ticket = ++lease.Revision;
        if (__0) { lease.HasLimit = true; return true; } // Keep the foreign starting animation/configuration.
        var body = (Player)lease.Body;
        body.MovementContext.SetPhysicalCondition(EPhysicalCondition.SprintDisabled, false);
        __result = FinishAfterDelay(lease, ticket);
        return false;
    }
    private static void AfterState(Task __result, ContinuousLoadAmmoScope.LimitCall __state, bool __runOriginal)
    {
        if (__state == null) return;
        try
        {
            var movement = __state.Observation.Movement as MovementContext;
            Scope.FinishLimitStart(__state, __runOriginal && __result?.Status == TaskStatus.RanToCompletion,
                movement?._speedLimits.ContainsKey(Player.ESpeedLimit.BarbedWire) == true);
        }
        catch (Exception ex) { Fault(ex); } // Preserve the exact original Task and exception behavior.
    }
    private static void AfterLimitRemoved(MovementContext __instance, Player.ESpeedLimit __0)
    {
        // Observe the actual removal; never guess an earlier asynchronous branch from
        // later MultiSelect/preset flags. Future unrelated limits are therefore not ours.
        if (__0 == Player.ESpeedLimit.BarbedWire) Scope.ObserveLimitRemoved(__instance);
    }
    private static async Task FinishAfterDelay(ContinuousLoadAmmoScope.Lease lease, long ticket)
    {
        await Task.Delay(800);
        try
        {
            if (_contract == null || !ContinuousLoadAmmoScope.MayResume(lease, ticket, Current(lease),
                (bool)_contract.Active.GetValue(lease.Controller), (bool)_contract.MultiSelect.GetValue(null))) return;
            var body = (Player)lease.Body;
            if (body.HandsIsEmpty) body.TrySetLastEquippedWeapon();
            if (lease.HasLimit) { body.MovementContext.RemoveStateSpeedLimit(Player.ESpeedLimit.BarbedWire); lease.HasLimit = false; }
            Report(4, "completed current replacement load without crossing a replacement or newer load");
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static void AfterProcessStart(object __instance, Task<IResult> __result)
    {
        if (_contract == null || __result == null) return;
        try
        {
            var inventory = __instance.GetType().GetField("_inventoryController", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(__instance);
            var lease = Scope.ForInventory(inventory);
            if (lease == null) return;
            var completion = Scope.CaptureTask(__result, lease);
            _ = CompleteOwnedProcess(__result, completion);
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static bool BeforeEndBroadcast(Task<IResult> __0) => _contract == null || Scope.ForTask(__0) == null;
    private static async Task CompleteOwnedProcess(Task<IResult> task, ContinuousLoadAmmoScope.Completion completion)
    {
        try
        {
            await task;
            // The native outer Load/UnloadMagazine continuation clears _loadProcess after Start completes.
            await Task.Yield();
            var lease = completion.Owner;
            if (_contract == null || !ContinuousLoadAmmoScope.IsLatest(completion) || !Current(lease)
                || (bool)_contract.Active.GetValue(lease.Controller)) return;
            _contract.LoadingEnd.Invoke(lease.Controller, null);
        }
        catch (OperationCanceledException) { } // Native cancellation is expected, never a compatibility failure.
        catch (Exception ex)
        {
            // A task from a retired body may fault after the next body is already active.
            // Its failure must not poison that body's future replacement admission.
            if (_contract != null && ContinuousLoadAmmoScope.IsLatest(completion) && Current(completion.Owner)) Fault(ex);
        }
    }
    private static void Report(int bit, string message)
    {
        if ((_reported & bit) != 0) return;
        _reported |= bit; CoreLog.LogDebug("Continuous Load Ammo: " + message + ".");
    }
    private static void Fault(Exception ex) { _runtimeFault = true; Reject(ex); }
    private static void Reject(Exception ex)
    {
        _failure = "Body replacement cannot safely rebind Continuous Load Ammo: " + ex.GetBaseException().Message
            + ". Update or remove Continuous Load Ammo, then restart. Ordinary raids without replacement remain available.";
        if (_lastWarning == _failure) return; _lastWarning = _failure; CoreLog.LogWarning(_failure);
    }
    private static void ClearBinding()
    {
        _contract = null; _plugin = null; _version = null; _disposing = null; _reported = 0; _preparedController = _ownedController = null; _preparedBody = null; Scope.Clear();
        if (!_patchAttempted) return; _patchAttempted = false;
        try { Patches.UnpatchSelf(); } catch (Exception ex) { Reject(ex); }
    }
    internal static void Shutdown() { ClearBinding(); _initialized = _runtimeFault = false; _failure = _lastWarning = null; }
}

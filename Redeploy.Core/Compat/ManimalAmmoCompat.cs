using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
using Version = System.Version;

namespace Redeploy.Core.Compat;

/// <summary>Retire only Manimal sessions belonging to bodies replaced by Core.</summary>
internal static class ManimalAmmoCompat
{
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.ManimalAmmoCompat");
    private static readonly ManimalAmmoScope Scope = new();
    private static ManimalAmmoContract _contract;
    private static BaseUnityPlugin _plugin;
    private static Version _version;
    private static bool _runtimeFault, _patchAttempted, _reportedNewer, _reportedRetirement;
    private static string _failure, _lastWarning;

    internal static string GetRejectionReason()
    {
        if (!Chainloader.PluginInfos.TryGetValue(ManimalAmmoContract.PluginGuid, out var info))
        { ClearBinding(); _failure = null; _runtimeFault = false; return null; }
        if (_runtimeFault) return _failure;
        try
        {
            if (info?.Instance == null) throw new InvalidOperationException("Manimal has not finished loading");
            if (_contract != null && ReferenceEquals(_plugin, info.Instance)
                && _version == ManimalAmmoContract.Normalize(info.Metadata?.Version))
            { _contract.VerifyNativeBindings(); VerifyOwnPatches(); return _failure; }
            ClearBinding();
            var contract = ManimalAmmoContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, info.Metadata?.Version, out var version);
            _patchAttempted = true;
            Patch(contract.Register, postfix: nameof(AfterRegister));
            Patch(contract.Get, nameof(BeforeBodyAction), nameof(AfterGet));
            Patch(contract.TryGet, nameof(BeforeBodyAction));
            Patch(contract.Ended, nameof(BeforeForeignEnded));
            Patch(contract.TryStart, nameof(BeforeTryStart));
            Patch(contract.SpawnController, nameof(BeforeSpawnController));
            foreach (var action in contract.BodyActions) Patch(action, nameof(BeforeBodyAction));
            foreach (var iterator in contract.Iterators)
            { Patch(iterator.Factory, postfix: nameof(AfterIterator)); Patch(iterator.MoveNext, nameof(BeforeMoveNext)); }
            Patch(contract.DetectPrefix, nameof(BeforeDetect));
            Patch(contract.DetectPostfix, nameof(BeforeForeignCompletion));
            Patch(contract.DelayPrefix, nameof(BeforeDelay));
            Patch(contract.NativeStart, postfix: nameof(AfterProcessStart));
            _contract = contract; _plugin = info.Instance; _version = version;
            VerifyOwnPatches(); _failure = null;
            CoreLog.LogDebug("Redeploy Manimal Ammo: physical-session retirement and delayed animation guards ready.");
            if (version > ManimalAmmoContract.ReviewedVersion && !_reportedNewer)
            {
                _reportedNewer = true;
                CoreLog.LogWarning("Manimal Ammo Loading Animations " + version + " is newer than reviewed 2.0.0; its APIs were checked, but runtime compatibility is unverified.");
            }
        }
        catch (Exception ex) { ClearBinding(); Reject(ex); }
        return _failure;
    }

    private static void Patch(MethodInfo target, string prefix = null, string postfix = null)
        => Patches.Patch(target, prefix == null ? null : Hook(prefix), postfix == null ? null : Hook(postfix));
    private static HarmonyMethod Hook(string name) => new(typeof(ManimalAmmoCompat), name) { priority = Priority.First };
    private static void VerifyOwnPatches()
    {
        void Check(MethodInfo target, string name, bool prefix = true)
        {
            var info = Harmony.GetPatchInfo(target);
            var entries = prefix ? info?.Prefixes : info?.Postfixes;
            var hook = typeof(ManimalAmmoCompat).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static);
            if (entries == null || entries.Count(p => p.owner == Patches.Id && p.PatchMethod == hook) != 1)
                throw new InvalidOperationException("Manimal compatibility hook missing: " + name);
        }
        Check(_contract.Register, nameof(AfterRegister), false);
        Check(_contract.Get, nameof(BeforeBodyAction)); Check(_contract.Get, nameof(AfterGet), false);
        Check(_contract.TryGet, nameof(BeforeBodyAction)); Check(_contract.Ended, nameof(BeforeForeignEnded));
        Check(_contract.TryStart, nameof(BeforeTryStart)); Check(_contract.SpawnController, nameof(BeforeSpawnController));
        foreach (var action in _contract.BodyActions) Check(action, nameof(BeforeBodyAction));
        foreach (var iterator in _contract.Iterators)
        { Check(iterator.Factory, nameof(AfterIterator), false); Check(iterator.MoveNext, nameof(BeforeMoveNext)); }
        Check(_contract.DetectPrefix, nameof(BeforeDetect)); Check(_contract.DetectPostfix, nameof(BeforeForeignCompletion));
        Check(_contract.DelayPrefix, nameof(BeforeDelay)); Check(_contract.NativeStart, nameof(AfterProcessStart), false);
        var capture = typeof(ManimalAmmoCompat).GetMethod(nameof(AfterProcessStart), BindingFlags.NonPublic | BindingFlags.Static);
        var sorted = PatchProcessor.GetSortedPatchMethods(_contract.NativeStart, Harmony.GetPatchInfo(_contract.NativeStart).Postfixes.ToArray());
        if (sorted.IndexOf(capture) < 0 || sorted.IndexOf(_contract.DetectPostfix) <= sorted.IndexOf(capture))
            throw new InvalidOperationException("Manimal completion capture must precede its foreign postfix");
    }

    private static GameWorld World => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
    private static bool ExactRaid(GameWorld world) => BodyReplacementContext.OwnsRaid(BodyReplacementContext.CurrentGame, world);
    private static bool Allowed(ManimalAmmoScope.Lease lease)
    {
        if (lease?.Owned != true) return true;
        var world = World;
        return ManimalAmmoScope.MayRun(lease, world, world?.MainPlayer, ExactRaid(world),
            lease.Body is Player body && body != null && body.HealthController?.IsAlive == true);
    }
    private static bool Allowed(ManimalAmmoScope.Ticket ticket)
    {
        if (ticket == null) return false;
        var world = World;
        return ManimalAmmoScope.MayResume(ticket, world, world?.MainPlayer, ExactRaid(world),
            ticket.Owner.Body is Player body && body != null && body.HealthController?.IsAlive == true);
    }

    // Read-only preflight. Another adapter may refuse before Core touches the old body.
    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        var rejection = GetRejectionReason();
        if (rejection != null) throw new InvalidOperationException(rejection);
        if (_contract == null) return;
        if (!ExactRaid(game?.GameWorld) || !BodyReplacementContext.OwnsBody(oldBody)
            || !ReferenceEquals(game.LocalPlayer, oldBody) || !ReferenceEquals(game.GameWorld.MainPlayer, oldBody))
            throw new InvalidOperationException("Manimal cannot verify the old replacement body");
        var session = _contract.FindSession(oldBody);
        ValidateEntry(oldBody, session);
        Scope.Prepare(oldBody, game.GameWorld, oldBody.InventoryController, session);
    }
    private static void ValidateEntry(object body, object session)
    {
        if (session != null)
        {
            if (session.GetType() != _contract.SessionType) throw new InvalidOperationException("Manimal session type changed");
            foreach (DictionaryEntry entry in _contract.Registry)
                if (ReferenceEquals(entry.Value, session) && !ReferenceEquals(entry.Key, body))
                    throw new InvalidOperationException("Manimal session is shared by unrelated physical bodies");
        }
    }
    internal static void AbortPreparation() => Scope.AbortPreparation();

    // RegisterPlayer assigns MainPlayer inside its original. A postfix is essential:
    // the prefix still sees the old body and cannot prove candidate ownership.
    private static void AfterRegister(GameWorld __instance, IPlayer __0)
    {
        if (_contract == null || Scope.PreparedBody == null || __0 is not LocalPlayer body
            || !ExactRaid(__instance) || !BodyReplacementContext.TryOwnCandidate(body)) return;
        try
        {
            var old = Scope.PreparedBody;
            var session = _contract.FindSession(old);
            ValidateEntry(old, session);
            var retired = Scope.CommitCandidate(body, __instance, body.InventoryController,
                session, ownedCandidate: true);
            Cleanup(retired);
        }
        catch (Exception ex) { Fault(ex); }
    }

    internal static void Tick()
    {
        if (_contract == null || BodyReplacementContext.IsCreating) return;
        Scope.AbortPreparation();
        try
        {
            var world = World; var game = BodyReplacementContext.CurrentGame;
            foreach (var lease in Scope.Active)
            {
                if (ExactRaid(world) && ReferenceEquals(world.MainPlayer, lease.Body)) continue;
                if (Scope.Retire(lease)) Cleanup(lease);
            }
            // A live replacement can fail after candidate registration and restore
            // its entry body. Old callbacks remain fenced by their generation.
            if (!_runtimeFault && ExactRaid(world) && world.MainPlayer is LocalPlayer body && body != null
                && body.IsYourPlayer && body.HealthController?.IsAlive == true
                && ReferenceEquals(game.LocalPlayer, body) && BodyReplacementContext.OwnsBody(body))
                Scope.Recover(body, world, body.InventoryController, exactOwnedCurrent: true);
        }
        catch (Exception ex) { Fault(ex); }
    }
    internal static void RetireWorld(GameWorld world)
    {
        if (_contract == null || ReferenceEquals(world, null)) return;
        foreach (var lease in Scope.RetireWorld(world))
            try { Cleanup(lease); } catch (Exception ex) { Fault(ex); }
    }

    private static void Cleanup(ManimalAmmoScope.Lease lease)
    {
        var entry = _contract.FindSession(lease.Body);
        if (lease.Session == null && entry == null) return;
        if (!ManimalAmmoScope.MayRemoveSession(lease, entry))
            throw new InvalidOperationException("Manimal retired session identity changed; unrelated state was preserved");
        var body = lease.Body as Player;
        var controller = _contract.ActiveController.GetValue(entry) as Player.ItemHandsController;
        var loop = _contract.Loop.GetValue(entry) as Coroutine;
        var renderer = _contract.Renderer.GetValue(entry) as MeshRenderer;
        _contract.Animating.SetValue(entry, false); _contract.LoadingCount.SetValue(entry, 0);
        _contract.DrawPending.SetValue(entry, false);
        // Pooled renderer/controller references may already have been reused. Never
        // alter them unless they still belong to this exact retired player.
        bool controllerOwned = controller != null && ReferenceEquals(controller._player, body);
        Exception cleanupFailure = null;
        try { if (body != null && loop != null) body.StopCoroutine(loop); }
        catch (Exception ex) { cleanupFailure = ex; }
        try
        {
            if (controllerOwned && renderer != null && renderer.transform.IsChildOf(controller.transform)) renderer.enabled = false;
        }
        catch (Exception ex) { cleanupFailure ??= ex; }
        // A stale Unity handle must not prevent releasing the proven managed
        // dictionary entry and its references. Generation fences are already live.
        _contract.Registry.Remove(lease.Body);
        _contract.ActiveController.SetValue(entry, null); _contract.Loop.SetValue(entry, null);
        _contract.Magazine.SetValue(entry, null); _contract.Renderer.SetValue(entry, null);
        _contract.MagazineTemplate.SetValue(entry, null);
        Scope.ForgetSession(lease);
        try { if (controllerOwned && body != null && ReferenceEquals(body.HandsController, controller)) body.DestroyController(); }
        catch (Exception ex) { cleanupFailure ??= ex; }
        if (!_reportedRetirement)
        { _reportedRetirement = true; CoreLog.LogDebug("Redeploy Manimal Ammo: retired the exact old-body session and animation references."); }
        if (cleanupFailure != null) throw new InvalidOperationException("Manimal managed state was retired but animation teardown failed", cleanupFailure);
    }

    private static bool BeforeBodyAction(Player __0) => _contract == null || Allowed(Scope.Find(__0));
    private static void AfterGet(Player __0, object __result)
    {
        if (_contract == null || __result == null) return;
        try { Scope.CaptureSession(Scope.Find(__0), __result); } catch (Exception ex) { Fault(ex); }
    }
    private static bool BeforeForeignEnded(Player __0)
        // Owned completions below have a task/session stamp. Pre-adoption foreign
        // continuations must never decrement a recovered body's fresh session.
        => _contract == null || Scope.Find(__0)?.Owned != true;
    private static bool BeforeTryStart(Player __0, object __1)
    {
        if (_contract == null) return true;
        if (!Allowed(Scope.Find(__0))) return false;
        try { Scope.Stamp(__1, __0); return true; } catch (Exception ex) { Fault(ex); return false; }
    }
    private static bool BeforeSpawnController(Player __0, object __1)
    {
        if (_contract == null) return true;
        var ticket = Scope.ForCallback(__1);
        return ticket == null ? Scope.Find(__0)?.Owned != true
            : ReferenceEquals(ticket.Owner.Body, __0) && Allowed(ticket);
    }
    private static void AfterIterator(Player __0, IEnumerator __result)
    {
        if (_contract == null || __result == null) return;
        try { Scope.Stamp(__result, __0); } catch (Exception ex) { Fault(ex); }
    }
    private static bool BeforeMoveNext(object __instance, MethodBase __originalMethod, ref bool __result)
    {
        if (_contract == null) return true;
        try
        {
            var ticket = Scope.ForCallback(__instance);
            if (ticket == null)
            {
                var iterator = _contract.Iterators.Single(i => i.MoveNext == __originalMethod);
                var body = iterator.Player.GetValue(__instance);
                // An already-running ordinary iterator may predate adapter install.
                // An unknown iterator on an owned body cannot prove its generation.
                if (Scope.Find(body)?.Owned != true) ticket = Scope.Stamp(__instance, body);
            }
            if (Allowed(ticket)) return true;
        }
        catch (Exception ex) { Fault(ex); }
        __result = false; return false;
    }

    private static bool ProcessAllowed(Player.PlayerInventoryController.LoadMagazineProcess process)
    {
        var inventory = process?._inventoryController;
        var lease = Scope.ForInventory(inventory);
        if (lease?.Owned == true) return Allowed(lease);
        var main = World?.MainPlayer;
        // The foreign prefix chooses MainPlayer instead of the native process owner.
        return Scope.Find(main)?.Owned != true;
    }
    private static bool BeforeDetect(Player.PlayerInventoryController.LoadMagazineProcess __0)
        => _contract == null || ProcessAllowed(__0);
    private static bool BeforeDelay(Player.PlayerInventoryController.LoadMagazineProcess __0, ref bool __result)
    {
        if (_contract == null || ProcessAllowed(__0)) return true;
        __result = true; return false; // Skip only Manimal's delay override; native wait remains intact.
    }
    private static bool BeforeForeignCompletion(Task<IResult> __0)
        => _contract == null || (Scope.ForCallback(__0) == null && Scope.Find(World?.MainPlayer)?.Owned != true);
    private static void AfterProcessStart(Player.PlayerInventoryController.LoadMagazineProcess __instance, Task<IResult> __result)
    {
        if (_contract == null || __result == null) return;
        try
        {
            var lease = Scope.ForInventory(__instance._inventoryController);
            if (lease?.Owned != true || !Allowed(lease) || lease.Session == null) return;
            var ticket = Scope.Stamp(__result, lease.Body, lease.Session);
            _ = CompleteProcess(__result, ticket);
        }
        catch (Exception ex) { Fault(ex); }
    }
    private static async Task CompleteProcess(Task<IResult> task, ManimalAmmoScope.Ticket ticket)
    {
        try { await task; } catch { /* Like upstream ContinueWith, finish accounting on cancellation/fault too. */ }
        await Task.Yield();
        try
        {
            if (_contract == null || !Allowed(ticket) || ticket.Session == null
                || !ReferenceEquals(_contract.FindSession(ticket.Owner.Body), ticket.Session)) return;
            int count = (int)_contract.LoadingCount.GetValue(ticket.Session);
            _contract.LoadingCount.SetValue(ticket.Session, Math.Max(0, count - 1));
        }
        catch (Exception ex) { if (Allowed(ticket)) Fault(ex); }
    }

    private static void Fault(Exception ex) { _runtimeFault = true; Reject(ex); }
    private static void Reject(Exception ex)
    {
        _failure = "Body replacement cannot safely retire Manimal Ammo Loading Animations: " + ex.GetBaseException().Message
            + ". Update or remove the mod, then restart. Ordinary raids without replacement remain available.";
        if (_lastWarning == _failure) return;
        _lastWarning = _failure; CoreLog.LogWarning(_failure);
    }
    private static void ClearBinding()
    {
        _contract = null; _plugin = null; _version = null; Scope.Clear();
        if (!_patchAttempted) return;
        _patchAttempted = false;
        try { Patches.UnpatchSelf(); } catch (Exception ex) { Reject(ex); }
    }
    internal static void Shutdown()
    { ClearBinding(); _runtimeFault = _reportedNewer = _reportedRetirement = false; _failure = _lastWarning = null; }
}

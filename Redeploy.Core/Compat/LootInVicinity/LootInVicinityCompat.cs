using System;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using Redeploy.Core.Api;
using Version = System.Version;

namespace Redeploy.Core.Compat;

internal static class LootInVicinityCompat
{
    internal const string PluginGuid = "com.softwyx.lootinvicinity";
    private static readonly StandaloneHudScope Scope = new();
    private static readonly Harmony Patches = new("com.maschine.Redeploy.Core.LootInVicinity");
    private static readonly MethodInfo BindFence = typeof(LootInVicinityCompat)
        .GetMethod(nameof(BeforeTryBind), BindingFlags.Static | BindingFlags.NonPublic);
    private static object _plugin;
    private static Version _version;
    private static LootInVicinityContract _contract;
    private static string _failure, _warning;
    private static bool _patched, _bound, _subscribed, _versionWarned;
    private static InventoryController _oldController;
    private static Player _publishedBody;
    private static bool _hadCache, _suspended, _recover;

    internal static string GetRejectionReason()
    {
        try
        {
            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var info) || FikaCompatCheck.IsFikaLoaded)
            {
                ClearBinding();
                RetireWorld(Scope.World);
                _plugin = null;
                _version = null;
                return _failure = null;
            }
            var version = StandaloneHudContract.Normalize(info.Metadata?.Version);
            if (info.Instance == null) throw new InvalidOperationException("plugin has not finished loading");
            if (ReferenceEquals(_plugin, info.Instance) && _version == version)
            {
                if (!_bound && _patched) ClearBinding(); // retry only the outstanding unpatch
                return _failure;
            }
            if (Scope.Owned) throw new InvalidOperationException("plugin changed during a body replacement");
            ClearBinding();
            _plugin = info.Instance;
            _version = version;
            _contract = LootInVicinityContract.Resolve(info.Instance.GetType(), info.Metadata?.GUID, version);
            _patched = true;
            Patches.Patch(_contract.TryBind, prefix: new HarmonyMethod(BindFence) { priority = Priority.First });
            VerifyFence();
            if (!_subscribed)
            {
                RespawnApi.Replaced += Replaced;
                RespawnApi.ReplacementFailed += Failed;
                _subscribed = true;
            }
            _bound = true;
            _failure = null;
            if (version > LootInVicinityContract.Reviewed && !_versionWarned)
            {
                _versionWarned = true;
                CoreLog.LogWarning("Loot In Vicinity " + version + " is newer than reviewed 3.0.6; its interfaces match.");
            }
        }
        catch (Exception ex)
        {
            if (!_bound && _patched)
            {
                try { ClearBinding(); }
                catch (Exception cleanup) { ex = cleanup; }
            }
            Fault(ex);
        }
        return _failure;
    }

    internal static void Prepare(LocalGame game, LocalPlayer oldBody)
    {
        if (!_bound) return;
        VerifyFence();
        var controller = oldBody.InventoryController ?? throw new InvalidOperationException("Missing outgoing inventory controller.");
        bool cached = _contract.CheckOwner(oldBody, controller);
        _contract.CheckLoot(game.GameWorld, controller, cached);
        Scope.Prepare(game, oldBody);
        _oldController = controller;
        _hadCache = cached;
    }

    internal static void CompletePreparation()
    {
        if (ReferenceEquals(Scope.Prepared, null)) return;
        if (!Scope.Preparing) throw new InvalidOperationException("Loot In Vicinity lost its prepared raid.");
        _hadCache = _contract.CheckOwner(Scope.Prepared, _oldController);
        _contract.CheckLoot(Scope.World, _oldController, _hadCache);
        _contract.ClosePanel();
        // Find() and InventoryController call TryBind() but return the backing field even
        // when it returns false. Clear both together before the engine yields during creation.
        _suspended = true;
        _contract.SetCache(null, null);
    }

    private static bool BeforeTryBind(ref bool __result)
    {
        if (!_suspended || !ReferenceEquals(Scope.World, StandaloneHudScope.CurrentWorld)
            || !ReferenceEquals(Scope.Game, StandaloneHudScope.CurrentGame)) return true;
        __result = false;
        return false;
    }

    private static void VerifyFence()
    {
        if (Harmony.GetPatchInfo(_contract.TryBind)?.Prefixes.Count(p => p.owner == Patches.Id && p.PatchMethod == BindFence) != 1)
            throw new MissingMethodException("Loot In Vicinity's cache fence is missing.");
    }

    private static void Replaced(Player oldBody, Player body, string reason)
    {
        if (!_suspended || !ReferenceEquals(oldBody, Scope.Prepared)) return;
        // Another owner may still be releasing MyPlayer when Core publishes Replaced.
        // Retain that exact physical successor and retry once the input owner agrees.
        _publishedBody = body;
        _recover = true;
        try { Bind(body, true); }
        catch (Exception ex) { Fault(ex); }
    }

    private static void Failed(Player oldBody, string stage)
    {
        if (_suspended && ReferenceEquals(oldBody, Scope.Prepared)) _recover = true;
    }

    internal static void Tick()
    {
        if (!_recover || BodyReplacementContext.IsCreating || _failure != null) return;
        if (!BodyReplacementContext.OwnsRaid(Scope.Game, Scope.World)) { RetireWorld(Scope.World); return; }
        try
        {
            if (Scope.Exact(Scope.Prepared))
            {
                RestoreOldCache();
                Scope.RecoverPrepared();
                EndPreparation();
            }
            else Bind(_publishedBody ?? Scope.World.MainPlayer, false);
        }
        catch (Exception ex) { Fault(ex); }
    }

    private static void Bind(Player body, bool published)
    {
        if (!Scope.CanBind(body, published)) return;
        var controller = body.InventoryController ?? throw new InvalidOperationException("Replacement inventory is unavailable.");
        bool alreadyBound = _contract.CheckOwner(body, controller);
        _contract.EndOldLootScreen();
        if (_hadCache && !alreadyBound) _contract.SetCache(body, controller);
        Scope.Bind((LocalPlayer)body);
        EndPreparation();
    }

    private static void RestoreOldCache()
    {
        if (_suspended && _hadCache && !_contract.CheckOwner(Scope.Prepared, _oldController))
            _contract.SetCache(Scope.Prepared, _oldController);
    }

    internal static void AbortPreparation()
    {
        try
        {
            if (_suspended && BodyReplacementContext.OwnsRaid(Scope.Game, Scope.World)
                && ReferenceEquals(Scope.Game.LocalPlayer, Scope.Prepared) && ReferenceEquals(Scope.World.MainPlayer, Scope.Prepared))
                RestoreOldCache();
        }
        catch (Exception ex) { Fault(ex); }
        finally { Scope.Abort(); EndPreparation(); }
    }

    internal static void RetireWorld(GameWorld world)
    {
        if (!ReferenceEquals(world, Scope.World)) return;
        Scope.Retire(world);
        EndPreparation();
    }

    private static void EndPreparation()
    {
        _oldController = null;
        _publishedBody = null;
        _hadCache = _suspended = _recover = false;
    }

    private static void ClearBinding()
    {
        _bound = false;
        if (_patched) { Patches.UnpatchSelf(); _patched = false; }
        _contract = null;
    }

    private static void Fault(Exception error)
    {
        _failure = "Loot In Vicinity body replacement compatibility failed: " + error.GetBaseException().Message;
        if (_warning != _failure) { _warning = _failure; CoreLog.LogWarning(_failure); }
    }
}

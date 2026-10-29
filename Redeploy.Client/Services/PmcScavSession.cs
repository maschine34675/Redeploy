using Comfort.Common;
using CommonAssets.Scripts.Game;
using Diz.Jobs;
using EFT;
using EFT.Communications;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.BattleTimer;
using HarmonyLib;
using JsonType;
using Newtonsoft.Json.Linq;
using Redeploy.Config;
using Redeploy.Core.GameAccess;
using Redeploy.Core.Spawning;
using Redeploy.Patches;
using SPT.Common.Http;
using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace Redeploy.Services;

/// <summary>
/// Owns the opt-in switch from a dead PMC to the account's regular Player-Scav.
/// The native end request still belongs to the original PMC raid; the server
/// journals both results, then native Scav UI transfers the reconciled inventory.
/// </summary>
internal static class PmcScavSession
{
    private sealed class Context
    {
        public LocalGame Game;
        public Profile OriginalPmc;
        public Profile Scav;
        public IEftSession Session;
        public string ServerId;
        public string Token;
        public string GenerationHash;
        public float DeathTime;
        public bool Ending;
        public bool ExitsBound;
        public bool CapturingInsurance;
        public FlatItem[] FinalInsurance;
        public string InsuranceFailure;
        public bool BackendAcknowledged;
        public bool ResultNoticeShown;
        public bool RegularScavSaveBlocked;
        public ExitStatus ScavOutcome;
        public readonly RegularScavLifeClock LifeClock = new();
        public LocalPlayer ScavBody;
        public string TimingFailure;
    }

    private static Context _context;
    private static LocalGame _deathGame;
    private static LocalPlayer _deathPlayer;
    private static FlatItem[] _deathInsurance;
    private static string _deathInsuranceFailure;
    private static float _deathTime;
    private static string _unsafePreparationServerId;
    private static string _unsafePreparationPmcId;
    private static string _unsafePreparationReason;
    // Set when a refusal changed nothing locally: the Scav choice stays disabled with this reason
    // for the rest of this death, while the PMC choices come back.
    private static string _refusedReason;
    private static readonly System.Reflection.FieldInfo ExitAuthority =
        AccessTools.Field(typeof(ExfiltrationPoint), "_authorityToChangeStatusExternally");

    public static string UnavailableReason
    {
        get
        {
            if (PmcScavPatches.UnavailableReason != null)
                return PmcScavPatches.UnavailableReason;
            var game = RespawnSessionState.CurrentLocalGame;
            if (game == null || HasSwitched(game))
                return null;
            return ScavUnavailableReason(LocalGameAccessor.GetSession(game)?.ProfileOfPet)
                ?? ReadOnlyUnavailableReason(game);
        }
    }

    private static string ReadOnlyUnavailableReason(LocalGame game)
    {
        if (_unsafePreparationReason != null || _context != null)
            return "A Scav continuation has already been prepared. Finish or recover that continuation before trying again.";
        if (_refusedReason != null)
            return _refusedReason;
        if (!CanContinue(game) || game.Profile?.Info?.Side == EPlayerSide.Savage)
            return "Only a live PMC raid can continue as your Scav.";
        if (_deathInsuranceFailure != null)
            return _deathInsuranceFailure;
        var world = Singleton<GameWorld>.Instance;
        if (world == null)
            return "The current raid world is unavailable.";
        var settings = LocalGameAccessor.GetLocalRaidSettings(game);
        var exits = ExfiltrationController.Instance?.ScavExfiltrationPoints;
        return ScavEligibilityPolicy.UnavailableReason(
            ReferenceEquals(_deathGame, game) && ReferenceEquals(_deathPlayer, game.LocalPlayer)
                && _deathInsurance != null,
            !string.IsNullOrWhiteSpace(settings?.serverId),
            exits != null && exits.Any(point => point != null), ExitAuthority != null,
            ScavEligibilityPolicy.HasDeliveredItems(world.BtrController?.TransferItemsController?.Stash)
                || ScavEligibilityPolicy.HasDeliveredItems(world.TransitController?.TransferItemsController?.Stash),
            ScavEligibilityPolicy.IsTransitArrival(settings?.transition));
    }

    private static string ScavUnavailableReason(Profile scav)
    {
        if (scav?.Info == null || scav.Info.Side != EPlayerSide.Savage)
            return "Your regular Scav profile is unavailable.";
        var remaining = RegularScavPolicy.CooldownSeconds(scav.Info.SavageLockTime, DateTimeExtensions.UtcNowUnixInt);
        if (double.IsInfinity(remaining))
            return "Your Scav cooldown could not be verified.";
        if (remaining <= 0)
            return null;
        var minutes = Math.Ceiling(remaining / 60d);
        return remaining < 60d
            ? $"Your Scav is on cooldown ({remaining:0}s remaining)."
            : $"Your Scav is on cooldown ({minutes:0} min remaining).";
    }

    public static bool IsOffered
    {
        get
        {
            var game = RespawnSessionState.CurrentLocalGame;
            return Settings.RespawnAsScav.Value && game?.Profile != null
                && game.Profile.Info.Side != EPlayerSide.Savage && !HasSwitched(game);
        }
    }

    public static void ResetForRaidStart()
    {
        _context = null;
        _deathGame = null;
        _deathPlayer = null;
        _deathInsurance = null;
        _deathInsuranceFailure = null;
        _deathTime = 0f;
        _unsafePreparationServerId = _unsafePreparationPmcId = _unsafePreparationReason = null;
        _refusedReason = null;
    }

    public static void RecordDeath(LocalGame game)
    {
        if (game?.Profile?.Info == null || game.Profile.Info.Side == EPlayerSide.Savage)
            return;
        _deathGame = game;
        _deathPlayer = game.LocalPlayer;
        _deathTime = game.PastTime;
        _deathInsurance = null;
        _deathInsuranceFailure = null;
        _refusedReason = null;
        if (!Settings.RespawnAsScav.Value)
            return;
        try
        {
            // Recovery after process loss cannot inspect this world again. Freeze the
            // native loss calculation now, including insured items dropped earlier.
            _deathInsurance = CaptureDeathInsurance(game);
        }
        catch (Exception ex)
        {
            _deathInsuranceFailure = "The PMC insurance snapshot could not be verified. Continue as PMC or end the raid.";
            Plugin.ModLogger.LogWarning($"{_deathInsuranceFailure} {ex.Message}");
        }
    }

    private static FlatItem[] CaptureDeathInsurance(LocalGame game)
    {
        var world = Singleton<GameWorld>.Instance
            ?? throw new InvalidOperationException("The raid world is unavailable for insurance capture.");
        var factory = Singleton<ItemFactory>.Instance
            ?? throw new InvalidOperationException("The item factory is unavailable for insurance capture.");
        // Native GetLostInsuredItems also removes restricted items from corpse slots.
        // Never run that cleanup at death: snapshot only, and let server recovery
        // exclude protected/retained slots. Include the dead equipment root because
        // this callback can precede the corpse entering the world's loot registry.
        var roots = world.LootList.OfType<LootItem>().Select(item => item.ItemOwner.RootItem)
            .Concat(world.LootList.OfType<LootableContainer>().Select(item => item.ItemOwner.RootItem))
            .Append(game.LocalPlayer.Profile.Inventory.Equipment).Where(item => item != null).Distinct().ToArray();
        var collections = roots.OfType<ContainerCollection>().GetAllItemsFromCollections();
        var serialized = factory.TreeToFlatItems(roots.Concat(collections).Distinct());
        return ScavEligibilityPolicy.SelectInsuredSnapshot(serialized,
            game.Profile.InsuredItems.Select(item => item.ItemId.ToString()));
    }

    public static bool HasSwitched(LocalGame game)
        => _context != null && ReferenceEquals(game, _context.Game)
            && _context.Scav != null && ReferenceEquals(game.Profile, _context.Scav);

    private static bool CanContinue(LocalGame game)
        => game != null && ReferenceEquals(RespawnSessionState.CurrentLocalGame, game)
            && RespawnSessionState.IsInRaid && !RespawnSessionState.HasGivenUp
            && game.Status == GameStatus.Started;

    public static IEnumerator Prepare(LocalGame game, Action<Profile, ISpawnSelector, Exception> completed)
    {
        Task<JObject> task = null;
        Task<JObject> probe = null;
        Exception failure = null;
        Context candidate = null;
        ScavRespawnItemCost cost = null;
        // Phase 1, read-only. A failure here has changed nothing, so it is a refusal (the PMC choices
        // come back) unless a held or unsafe continuation exists, which must never be returned from.
        try
        {
            bool continuationHeld = _unsafePreparationReason != null || _context != null;
            if (UnavailableReason != null)
                throw ScavEligibilityPolicy.Block(UnavailableReason, continuationHeld);
            // Revalidate this exact game immediately before any profile mutation or
            // reservation; the prompt may have been displayed before the respawn delay.
            var unavailable = ReadOnlyUnavailableReason(game);
            if (unavailable != null)
                throw ScavEligibilityPolicy.Block(unavailable, continuationHeld);
            var transfers = game.GetTransferItems();
            // A transfer stash's single root item is present even when nothing was sent.
            if (transfers.Values.Any(items => items != null && items.Length > 1))
                throw ScavEligibilityPolicy.Block(ScavEligibilityPolicy.PriorDeliveryReason, continuationHeld);
        }
        catch (Exception ex) { failure = ex; }
        if (failure != null)
        {
            completed(null, null, failure);
            yield break;
        }

        // Phase 2, still read-only locally: ask the server whether a Scav can be prepared for this raid
        // at all. Ending the dead PMC's statistics session below cannot be undone, and without this
        // question a raid the server never registered (map transit, server restarted mid-raid, an older
        // server mod) would only be found out after it, when the only way left is the PMC death ending.
        try
        {
            // Reads only; fails here (before anything is consumed) when the cost item is already gone.
            cost = new ScavRespawnItemCost(game.LocalPlayer);
            probe = Post("probe", new JObject
            {
                ["initialServerId"] = LocalGameAccessor.GetLocalRaidSettings(game).serverId,
            });
        }
        catch (Exception ex)
        {
            if (ex is not InvalidOperationException)
                Plugin.ModLogger.LogWarning($"Scav check setup failed: {ex}");
            failure = ScavEligibilityPolicy.SetupRefusal(ex);
        }
        if (failure != null)
        {
            completed(null, null, failure);
            yield break;
        }
        while (!probe.IsCompleted)
            yield return null;
        try
        {
            RequireSuccess(probe.GetAwaiter().GetResult());
            if (!CanContinue(game))
                throw new OperationCanceledException("The raid ended while checking the Scav.");
            // The item stayed on the corpse while the answer was awaited; the session end below is the
            // first thing that depends on it, so a vanished item is still a clean refusal here.
            if (!cost.IsStillHeld)
                throw new ScavPreparationRefused("The required respawn item is no longer available.");
        }
        // A timed-out or cancelled request is a refusal too, unless the raid itself is gone.
        catch (Exception ex) { failure = ScavEligibilityPolicy.ClassifyProbeFailure(ex, CanContinue(game)); }
        if (failure != null)
        {
            completed(null, null, failure);
            yield break;
        }

        // Phase 3: from here the dead body and profile change; every failure fails closed.
        try
        {
            var settings = LocalGameAccessor.GetLocalRaidSettings(game);
            var dead = game.LocalPlayer;
            dead.OnGameSessionEnd(ExitStatus.Killed, _deathTime, game.Location.Id, null);
            game.Profile.Health = dead.ActiveHealthController.Store();
            candidate = new Context
            {
                Game = game,
                OriginalPmc = game.Profile,
                Session = LocalGameAccessor.GetSession(game),
                ServerId = settings.serverId,
                DeathTime = _deathTime,
            };
            // Serialize while the cost is temporarily removed, then restore it in the
            // same frame. Cooldown rejection and preload failure consume nothing.
            var frozen = cost.CaptureWithoutCostItem(() => JObject.Parse(new SessionResult
            {
                profile = new ProfileDescriptor(candidate.OriginalPmc, FullySearchedSearchController.Instance).ToUnparsedData(),
                result = ExitStatus.Killed,
                killerId = dead.KillerId,
                killerAid = dead.KillerAccountId,
                playTime = (int)candidate.DeathTime,
                inSession = false,
                ProfileId = candidate.OriginalPmc.Id,
            }.ToJson()));
            var body = new JObject
            {
                ["protocolVersion"] = 2,
                ["initialServerId"] = candidate.ServerId,
                ["clientRequestId"] = Guid.NewGuid().ToString("N"),
                ["pmcInsuranceCaptured"] = true,
                ["frozenPmcDeath"] = new JObject
                {
                    ["serverId"] = candidate.ServerId,
                    ["results"] = frozen,
                    // Recovery fallback only: final live settlement still measures loss
                    // after the Scav has had the chance to recover insured equipment.
                    ["lostInsuredItems"] = JArray.FromObject(_deathInsurance),
                },
            };
            task = Post("prepare", body);
        }
        catch (ScavCostRestorationException ex)
        {
            _unsafePreparationServerId = candidate?.ServerId;
            _unsafePreparationPmcId = candidate?.OriginalPmc.Id;
            _unsafePreparationReason = ex.Message;
            Plugin.ModLogger.LogError($"Scav continuation preparation failed: {ex}");
            failure = ex;
        }
        catch (Exception ex) { failure = ex; }

        if (failure != null)
        {
            completed(null, null, failure);
            yield break;
        }
        while (!task.IsCompleted)
            yield return null;

        Profile scav = null;
        ISpawnSelector spawn = null;
        try
        {
            var response = task.GetAwaiter().GetResult();
            RequireSuccess(response);
            candidate.Token = (string)response["token"];
            candidate.GenerationHash = (string)response["generationHash"];
            // Retain the prepared lease before parsing so a malformed response can cancel.
            _context = candidate;
            if (string.IsNullOrWhiteSpace(candidate.Token) || string.IsNullOrWhiteSpace(candidate.GenerationHash))
                throw new InvalidOperationException("The server returned an incomplete Scav lease.");
            if (!string.Equals((string)response["phase"], "Prepared", StringComparison.Ordinal))
                throw new InvalidOperationException("The server did not prepare a new Scav continuation.");
            scav = new Profile(response["scavProfile"].ToString().ParseJsonTo<ProfileDescriptor>());
            if (scav.Info.Side != EPlayerSide.Savage
                || !RegularScavPolicy.IsAccountScav(candidate.OriginalPmc.Id,
                    candidate.Session.ProfileOfPet?.Id, scav.Id)
                || scav.FenceInfo == null || scav.FenceInfo.AvailableExitsCount < 1)
                throw new InvalidOperationException("The reserved Scav does not match your account or has no extraction allowance.");
            if (!CanContinue(game))
                throw new OperationCanceledException("The raid ended while preparing the Scav.");
            candidate.Scav = scav;
            var selected = new InfilSpawnSelector(null, scav).Resolve(game);
            if (!IsFinite(selected.position))
                throw new InvalidOperationException("No valid native Scav spawn was found.");
            spawn = new PreparedSpawn(selected.position, selected.rotation);
        }
        catch (Exception ex)
        {
            failure = ex;
            CancelPrepared();
        }
        if (failure == null)
        {
            // New gear/clothing can be absent from this raid's pools. Load everything
            // needed by a local Scav, including first-person hands, before old-body teardown.
            Task preload = PreloadScav(game, scav);
            while (!preload.IsCompleted)
                yield return null;
            try { preload.GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                failure = ex;
                CancelPrepared();
            }
        }
        if (failure == null)
        {
            try
            {
                if (!CanContinue(game))
                    throw new OperationCanceledException("The raid ended before the Scav switch.");
                cost.Commit();
            }
            catch (Exception ex)
            {
                failure = ex;
                CancelPrepared();
            }
        }
        completed(failure == null ? scav : null, spawn, failure);
    }

    private static async Task PreloadScav(LocalGame game, Profile scav)
    {
        var factory = Singleton<ObjectsFactory>.Instance;
        var token = ObjectsFactory.DefaultCancellationToken;
        bool Current() => CanContinue(game) && !token.IsCancellationRequested
            && token == ObjectsFactory.DefaultCancellationToken
            && ReferenceEquals(factory, Singleton<ObjectsFactory>.Instance)
            && factory != null && factory.IsPoolReady(ObjectsFactory.PoolsCategory.Raid);
        if (!Current())
            throw new OperationCanceledException("The Scav's raid pools are no longer available.");
        var resources = scav.GetAllPrefabPaths(allCustomization: true)
            .Where(key => key != null && key != ResourceKey.EmptyResourceKey).Distinct().ToArray();
        if (resources.Length == 0)
            throw new InvalidOperationException("Your Scav has no loadable prefab resources.");
        await factory.LoadBundlesAndCreatePools(ObjectsFactory.PoolsCategory.Raid,
            ObjectsFactory.AssemblyType.Local, resources, JobYieldPriority.General, null, token);
        if (!Current())
            throw new OperationCanceledException("The raid ended while loading the Scav's equipment.");
    }

    private static bool IsFinite(Vector3 value)
        => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z)
            && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);

    public static void CompleteSwitch(LocalGame game)
    {
        if (!HasSwitched(game))
            throw new InvalidOperationException("Your Scav did not become the active profile.");
        if (_context.Ending || game.Status != GameStatus.Started)
            return;
        if (!_context.LifeClock.Owns(game.LocalPlayer) || _context.TimingFailure != null)
            throw new InvalidOperationException("The Scav's statistics start could not be verified.");
        // A failed acknowledgement cannot undo a committed body. The server also recognizes
        // this exact issued Scav ID on native final settlement if the response was lost.
        ObserveActivation(_context);
        if (_context.ExitsBound)
            return;
        BindScavExits(game);
        _context.ExitsBound = true;
        ShowNotice("You are now playing your Scav. Extract to transfer your loot to the stash. Your next death ends the raid. BTR item delivery is unavailable during this continuation.");
        Plugin.ModLogger.LogInfo("PMC-to-Scav continuation active; your regular Scav is in use and the PMC remains dead.");
    }

    private static void BindScavExits(LocalGame game)
    {
        var controller = ExfiltrationController.Instance;
        var points = controller.EligiblePoints(game.Profile);
        if (points == null || points.Length == 0)
            throw new InvalidOperationException("No usable Scav extraction was assigned.");
        var secret = controller.GetScavSecretExits();
        foreach (var point in controller.ExfiltrationPoints.Concat(controller.ScavExfiltrationPoints)
            .Concat(controller.SecretExfiltrationPoints).Distinct())
        {
            point.OnStatusChanged -= game.OnStatusChangedHandler;
            point.OnStatusChanged -= game.ShowNewSecretExit;
        }
        // SPT performs this only at GameWorld.OnGameStarted, which preceded the identity switch.
        foreach (var point in controller.ExfiltrationPoints)
        {
            if (point is ScavExfiltrationPoint scavPoint)
            {
                if (!scavPoint.EligibleIds.Contains(game.Profile.Id))
                    point.Disable();
            }
            else
            {
                point.Disable();
                ExitAuthority.SetValue(point, false);
            }
        }
        if (Singleton<GameWorld>.Instance.TransitController is TransitController transit)
        {
            foreach (var point in transit.pointsById.Values)
                point.Enabled = false;
        }
        var timer = LocalGameAccessor.GetGameUI(game).TimerPanel;
        ScavExtractionUi.ValidateOwnedTimers(timer);
        // Native Close clears its transit dictionary without closing the instantiated
        // rows. Retire them before rebuilding the panel for a life with no transit.
        foreach (var transitTimer in timer.GetComponentsInChildren<TransitTimerPanel>(true))
        {
            if (transitTimer == timer._transitTimerTemplate)
                continue;
            transitTimer.Close();
            UnityEngine.Object.Destroy(transitTimer.gameObject);
        }
        timer.Close();
        ScavExtractionUi.RetireNativeTimersAfterClose(timer);
        timer.ProfileId = game.Profile.Id;
        timer.SetTime(DateTimeExtensions.UtcNow, EPlayerSide.Savage,
            Mathf.Max(0f, game.GameTimer.EscapeTimeSeconds()), points, secret);
        foreach (var point in points)
        {
            point.OnStatusChanged += game.OnStatusChangedHandler;
            game.UpdateExfiltrationUi(point, false, true);
        }
        foreach (var point in secret)
        {
            point.OnStatusChanged += game.OnStatusChangedHandler;
            point.OnStatusChanged += game.ShowNewSecretExit;
            game.UpdateExfiltrationUi(point, false, true);
        }
    }

    public static bool BlocksTransit(Player player)
        => _context != null && !_context.Ending && _context.Scav != null
            && ReferenceEquals(player?.Profile, _context.Scav);

    public static bool BlocksBtrItemDelivery(object profile, ETraderServiceType serviceType)
    {
        var context = _context;
        if (serviceType != ETraderServiceType.BtrItemsDelivery || context?.Scav == null
            || !RespawnSessionState.IsInRaid
            || !ReferenceEquals(RespawnSessionState.CurrentLocalGame, context.Game)
            || !HasSwitched(context.Game)
            || !ReferenceEquals(context.Game.LocalPlayer?.Profile, context.Scav)
            || !ReferenceEquals(profile, context.Scav))
            return false;
        // Keep the exact adopted identity fenced during Ending as well: a delayed
        // native service completion must not add delivery after the final snapshot.
        ShowNotice(ScavEligibilityPolicy.BtrDeliveryReason, warning: true);
        return true;
    }

    public static bool BlocksScavSellAll(BackEndInventoryController controller)
    {
        if (controller is not OfflineInventoryController offline)
            return false;
        var context = _context;
        var accountScav = context?.Session?.ProfileOfPet;
        bool ownsResultTransfer = context?.Scav != null && context.Ending && context.BackendAcknowledged
            && (context.ScavOutcome == ExitStatus.Survived || context.ScavOutcome == ExitStatus.Runner)
            && ReferenceEquals(controller._backendSession, context.Session)
            && ReferenceEquals(controller.Profile, accountScav)
            && accountScav?.Inventory?.Equipment != null
            && ReferenceEquals(controller.Inventory?.Equipment, accountScav.Inventory.Equipment)
            && RegularScavPolicy.IsAccountScav(context.OriginalPmc.Id, accountScav.Id, context.Scav.Id)
            && accountScav.Inventory.Equipment.Id == context.Scav.Inventory?.Equipment?.Id;
        if (!ownsResultTransfer && !ScavRecoveryClient.OwnsTransfer(offline))
            return false;
        // Native SellAll pays server-side but removes the Scav equipment only locally.
        // A crash before FinishScavSession would otherwise expose the same loot again.
        ShowNotice("Move loot to your stash first; Sell all is unavailable for this continued Scav.", warning: true);
        return true;
    }

    public static bool BlocksTransitUi(ExtractionTimersPanel panel)
        => _context != null && !_context.Ending && _context.ExitsBound && _context.Scav != null
            && panel != null && panel.ProfileId == _context.Scav.Id
            && ReferenceEquals(LocalGameAccessor.GetGameUI(_context.Game)?.TimerPanel, panel);

    public static bool BlocksRegularScavSave(SessionResultShowOperation operation)
    {
        var context = _context;
        if (context?.Scav == null || operation == null
            || !RegularScavPolicy.OwnsResult(context.Ending,
                ReferenceEquals(operation._session, context.Session), context.OriginalPmc.Id,
                context.Scav.Id, operation._matchProfileId))
            return false;
        if (!context.RegularScavSaveBlocked)
        {
            context.RegularScavSaveBlocked = true;
            Plugin.ModLogger.LogDebug("PMC-to-Scav result: stale client Scav save bypassed; the native transfer uses the server-settled inventory.");
        }
        // Keep this guard until the next raid: native Init can run again after an error.
        return true;
    }

    public static void BeforeGameEnd(LocalGame game, ExitStatus outcome)
    {
        if (!HasSwitched(game) || _context.Ending)
            return;
        _context.Ending = true;
        _context.ScavOutcome = outcome;
        if (!_context.LifeClock.TryFreeze(game.LocalPlayer, game.PastTime, out _))
            _context.TimingFailure = "Redeploy could not verify the Scav's own play time. The combined raid was not saved to protect progression.";
        // Capture while every physical body/item is still present. The native method uses
        // game.Profile only for the insured-ID filter; scope the swap to this sync call.
        try { _context.FinalInsurance = CaptureOriginalInsurance(game); }
        catch (Exception ex)
        {
            _context.InsuranceFailure = "Redeploy could not safely determine the PMC's lost insured items. The combined raid was not saved; server recovery data has been retained.";
            Plugin.ModLogger.LogError($"{_context.InsuranceFailure} {ex}");
            // Let native cleanup/error UI finish, but block the unsafe save at the backend
            // call instead of throwing from a Harmony prefix and stranding a Stopping raid.
            ShowNotice(_context.InsuranceFailure, warning: true);
        }
    }

    private static FlatItem[] CaptureOriginalInsurance(LocalGame game)
    {
        var profile = game.Profile;
        _context.CapturingInsurance = true;
        try
        {
            game.Profile = _context.OriginalPmc;
            return game.GetLostInsuredItems();
        }
        finally
        {
            game.Profile = profile;
            _context.CapturingInsurance = false;
        }
    }

    public static bool TryGetFinalInsurance(LocalGame game, out FlatItem[] items)
    {
        items = null;
        if (!HasSwitched(game) || !_context.Ending || _context.CapturingInsurance)
            return false;
        if (_context.InsuranceFailure != null)
        {
            items = Array.Empty<FlatItem>();
            return true;
        }
        items = _context.FinalInsurance ?? CaptureOriginalInsurance(game);
        return true;
    }

    public static Task TrackBackendEnd(LocalRaidSettings settings, SessionResult results, Task original)
    {
        var context = _context;
        if (context == null || !context.Ending || context.Scav == null
            || settings?.serverId != context.ServerId || results?.ProfileId != context.Scav.Id)
            return original;
        return AwaitBackendEnd(context, original);
    }

    public static string GetUnsafeSettlementReason(LocalRaidSettings settings, SessionResult results)
    {
        if (_unsafePreparationReason != null && settings?.serverId == _unsafePreparationServerId
            && results?.ProfileId == _unsafePreparationPmcId)
            return _unsafePreparationReason;
        var context = _context;
        return context != null && context.Ending && context.Scav != null
            && settings?.serverId == context.ServerId && results?.ProfileId == context.Scav.Id
                ? context.TimingFailure ?? context.InsuranceFailure : null;
    }

    public static void RecordScavLifeStart(Player player)
    {
        var context = _context;
        if (context?.Scav == null || context.Ending || player is not LocalPlayer local
            || !ReferenceEquals(player.Profile, context.Scav) || !CanContinue(context.Game))
            return;
        if (!context.LifeClock.TryStart(local, context.Game.PastTime))
            context.TimingFailure = "Redeploy could not record the Scav's statistics start.";
        else
            context.ScavBody = local;
    }

    public static void RewriteScavSessionTime(LocalPlayer player, ref float pastTime)
    {
        var context = _context;
        if (context?.Scav == null || !context.Ending || !ReferenceEquals(player, context.ScavBody)
            || !ReferenceEquals(player.Profile, context.Scav) || !HasSwitched(context.Game))
            return;
        if (context.LifeClock.TryRead(player, out var elapsed))
            pastTime = elapsed;
        else
        {
            // Do not let the old PMC clock reach lifetime/conditional counters even if
            // the timing contract failed; the backend fence below refuses that save.
            pastTime = 0f;
            context.TimingFailure = "Redeploy could not verify the Scav's own play time. The combined raid was not saved to protect progression.";
        }
    }

    public static void RewriteScavBackendTime(LocalRaidSettings settings, SessionResult results)
    {
        var context = _context;
        if (context?.Scav == null || !context.Ending || settings?.serverId != context.ServerId
            || results?.ProfileId != context.Scav.Id)
            return;
        if (context.LifeClock.TryRead(context.ScavBody, out var elapsed))
            results.playTime = (int)elapsed;
        else
            context.TimingFailure = "Redeploy could not verify the Scav's own play time. The combined raid was not saved to protect progression.";
    }

    private static async Task AwaitBackendEnd(Context context, Task original)
    {
        await original;
        context.BackendAcknowledged = true;
    }

    public static void RewriteScavResult(IEftSession session, ref string profileId, ref Profile savageProfile,
        ref ExitStatus exitStatus, ref TimeSpan exitTime)
    {
        var context = _context;
        if (context?.Scav == null || !RegularScavPolicy.OwnsResult(context.Ending,
            ReferenceEquals(session, context.Session), context.OriginalPmc.Id, context.Scav.Id, profileId))
            return;
        if (!context.BackendAcknowledged)
            throw new InvalidOperationException("The combined PMC/Scav result was not acknowledged; loot transfer cannot open safely.");
        var accountScav = session.ProfileOfPet;
        if (!RegularScavPolicy.IsAccountScav(context.OriginalPmc.Id, accountScav?.Id, context.Scav.Id))
            throw new InvalidOperationException("The settled Scav profile could not be verified; loot transfer cannot open safely.");
        bool extracted = context.ScavOutcome == ExitStatus.Survived || context.ScavOutcome == ExitStatus.Runner;
        if (extracted && (accountScav.Inventory?.Equipment == null
            || accountScav.Inventory.Equipment.Id != context.Scav.Inventory?.Equipment?.Id))
            throw new InvalidOperationException("The Scav transfer inventory has already changed; an old result cannot reopen it.");
        profileId = context.Scav.Id;
        exitStatus = context.ScavOutcome;
        if (!context.LifeClock.TryRead(context.ScavBody, out var elapsed))
            throw new InvalidOperationException("The settled Scav duration is unavailable.");
        exitTime = TimeSpan.FromSeconds(elapsed);
        // GetProfiles has already loaded authoritative post-raid inventories. Never
        // reinstall our pre-transfer snapshot: Init may run again after some loot moved.
        savageProfile = extracted ? accountScav : context.Scav;
        if (extracted)
            accountScav.LearnAll();
        if (!context.ResultNoticeShown)
        {
            context.ResultNoticeShown = true;
            ShowNotice(extracted
                ? "Your PMC remains dead. Your Scav extracted; transfer the recovered loot to your stash."
                : "PMC raid ended in death. Your Scav did not extract; no loot was recovered.");
        }
    }

    public static void CancelPrepared()
    {
        var context = _context;
        if (context == null || HasSwitched(context.Game))
            return;
        _context = null;
        if (!string.IsNullOrWhiteSpace(context.Token))
            ObserveCancellation(context);
    }

    private static async void ObserveActivation(Context context)
    {
        try
        {
            RequireSuccess(await Post("activate", new JObject
            {
                ["token"] = context.Token,
                ["initialServerId"] = context.ServerId,
                ["scavProfileId"] = (string)context.Scav.Id,
                ["generationHash"] = context.GenerationHash,
            }));
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Scav activation acknowledgement unavailable; native settlement retains the issued identity: {ScavEligibilityPolicy.DescribeForLog(ex)}");
        }
    }

    private static async void ObserveCancellation(Context context)
    {
        try
        {
            RequireSuccess(await Post("cancel", new JObject
            {
                ["token"] = context.Token,
                ["initialServerId"] = context.ServerId,
                ["reasonCode"] = "client-before-body-switch",
            }));
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Unused Scav preparation could not be cancelled: {ScavEligibilityPolicy.DescribeForLog(ex)}");
        }
    }

    private static async Task<JObject> Post(string operation, JObject payload)
    {
        string json = await RequestHandler.PostJsonAsync("/redeploy/pmc-scav/" + operation, payload.ToString(Newtonsoft.Json.Formatting.None));
        return JObject.Parse(json);
    }

    private static void RequireSuccess(JObject response)
    {
        if ((bool?)response?["success"] != true)
            throw new ScavServerRefusal((string)response?["error"]);
    }

    public static void NotifyUnavailable(string reason)
        => ShowNotice((_unsafePreparationReason != null
                ? "Scav continuation unavailable; raid saving was stopped to protect your profile. "
                : "Scav continuation unavailable; the PMC raid will end normally. ")
            + (reason ?? string.Empty), warning: true);

    /// <summary>Nothing was changed: keep the Scav choice disabled for this death and say why.</summary>
    public static void RecordRefusal(string reason)
        => _refusedReason = string.IsNullOrWhiteSpace(reason) ? "The Scav continuation is unavailable." : reason;

    public static void NotifyRefused(string reason)
        => ShowNotice("Scav continuation unavailable. Your PMC respawn choices are still open. " + (reason ?? string.Empty),
            warning: true);

    private static void ShowNotice(string message, bool warning = false)
    {
        try
        {
            NotificationManager.DisplayMessageNotification(message, ENotificationDurationType.Long,
                warning ? ENotificationIconType.Alert : ENotificationIconType.Default,
                warning ? Color.yellow : Color.white);
        }
        catch (Exception ex)
        {
            // Teardown may already have removed the notification UI; that must not
            // prevent native cleanup, error presentation or the committed body's exit.
            Plugin.ModLogger.LogDebug($"Scav continuation notice unavailable: {ex.Message}");
        }
    }

    private sealed class PreparedSpawn : ISpawnSelector
    {
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        public PreparedSpawn(Vector3 position, Quaternion rotation) { _position = position; _rotation = rotation; }
        public (Vector3 position, Quaternion rotation) Resolve(LocalGame game) => (_position, _rotation);
    }
}

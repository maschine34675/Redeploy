using Comfort.Common;
using CommonAssets.Scripts.Game;
using Diz.Jobs;
using EFT;
using EFT.Communications;
using EFT.Interactive;
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
/// Owns only the opt-in PMC-to-Scav conversion. In particular, it never replaces the
/// backend account's PMC/regular Scav pair. The native end request still belongs to
/// the original PMC raid; the server journals and settles both phases separately.
/// </summary>
internal static class PmcScavSession
{
    private sealed class Context
    {
        public LocalGame Game;
        public Profile OriginalPmc;
        public Profile Scav;
        public IEftSession Session;
        public PlayerVisualRepresentation PmcVisual;
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
    }

    private static Context _context;
    private static LocalGame _deathGame;
    private static float _deathTime;
    private static readonly System.Reflection.FieldInfo ExitAuthority =
        AccessTools.Field(typeof(ExfiltrationPoint), "_authorityToChangeStatusExternally");

    public static string UnavailableReason => PmcScavPatches.UnavailableReason;

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
        _deathTime = 0f;
    }

    public static void RecordDeath(LocalGame game)
    {
        if (game?.Profile?.Info.Side == EPlayerSide.Savage)
            return;
        _deathGame = game;
        _deathTime = game.PastTime;
    }

    public static bool HasSwitched(LocalGame game)
        => _context != null && ReferenceEquals(game, _context.Game)
            && _context.Scav != null && ReferenceEquals(game.Profile, _context.Scav);

    private static bool CanContinue(LocalGame game)
        => ReferenceEquals(RespawnSessionState.CurrentLocalGame, game)
            && RespawnSessionState.IsInRaid && !RespawnSessionState.HasGivenUp
            && game.Status == GameStatus.Started;

    public static IEnumerator Prepare(LocalGame game, Action consumeRequiredItem,
        Action<Profile, ISpawnSelector, Exception> completed)
    {
        Task<JObject> task = null;
        Exception failure = null;
        Context candidate = null;
        try
        {
            if (UnavailableReason != null)
                throw new InvalidOperationException(UnavailableReason);
            if (!CanContinue(game) || game.Profile.Info.Side == EPlayerSide.Savage)
                throw new InvalidOperationException("Only a live PMC raid can continue as a fresh Scav.");
            if (!ReferenceEquals(_deathGame, game))
                throw new InvalidOperationException("The PMC death snapshot is unavailable.");
            var settings = LocalGameAccessor.GetLocalRaidSettings(game);
            if (string.IsNullOrWhiteSpace(settings?.serverId))
                throw new InvalidOperationException("The original raid identity is unavailable.");
            var controller = ExfiltrationController.Instance;
            if (controller?.ScavExfiltrationPoints == null || controller.ScavExfiltrationPoints.Length == 0)
                throw new InvalidOperationException("This location has no Scav extraction points.");
            if (ExitAuthority == null)
                throw new InvalidOperationException("Scav extraction compatibility could not be verified.");
            var transfers = game.GetTransferItems();
            // A transfer stash's single root item is present even when nothing was sent.
            if (transfers.Values.Any(items => items != null && items.Length > 1))
                throw new InvalidOperationException("Scav continuation is unavailable after sending items through BTR or transit.");

            // The final immutable PMC payload must include the cost of continuing.
            consumeRequiredItem();
            var dead = game.LocalPlayer;
            dead.OnGameSessionEnd(ExitStatus.Killed, _deathTime, game.Location.Id, null);
            game.Profile.Health = dead.ActiveHealthController.Store();
            candidate = new Context
            {
                Game = game,
                OriginalPmc = game.Profile,
                Session = LocalGameAccessor.GetSession(game),
                PmcVisual = game.Profile.GetVisualEquipmentState(),
                ServerId = settings.serverId,
                DeathTime = _deathTime,
            };
            var frozen = new SessionResult
            {
                profile = new ProfileDescriptor(candidate.OriginalPmc, FullySearchedSearchController.Instance).ToUnparsedData(),
                result = ExitStatus.Killed,
                killerId = dead.KillerId,
                killerAid = dead.KillerAccountId,
                playTime = (int)candidate.DeathTime,
                inSession = false,
                ProfileId = candidate.OriginalPmc.Id,
            };
            var body = new JObject
            {
                ["protocolVersion"] = 1,
                ["initialServerId"] = candidate.ServerId,
                ["clientRequestId"] = Guid.NewGuid().ToString("N"),
                ["frozenPmcDeath"] = new JObject
                {
                    ["serverId"] = candidate.ServerId,
                    ["results"] = JObject.Parse(frozen.ToJson()),
                    // Insurance is measured at final exit, after the Scav can recover gear.
                    ["lostInsuredItems"] = new JArray(),
                },
            };
            task = Post("prepare", body);
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
            if (scav.Info.Side != EPlayerSide.Savage || scav.Id == candidate.OriginalPmc.Id
                || scav.Id == LocalGameAccessor.GetSession(game).ProfileOfPet?.Id
                || scav.FenceInfo == null || scav.FenceInfo.AvailableExitsCount < 1)
                throw new InvalidOperationException("The generated Scav has an invalid identity or extraction allowance.");
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
            throw new InvalidOperationException("The generated Scav has no loadable prefab resources.");
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
            throw new InvalidOperationException("The fresh Scav did not become the active profile.");
        if (_context.Ending || game.Status != GameStatus.Started)
            return;
        // A failed acknowledgement cannot undo a committed body. The server also recognizes
        // this exact issued Scav ID on native final settlement if the response was lost.
        ObserveActivation(_context);
        if (_context.ExitsBound)
            return;
        BindScavExits(game);
        _context.ExitsBound = true;
        ShowNotice("You are now a Scav. Extract to receive your carried loot by message. Your next death ends the raid.");
        Plugin.ModLogger.LogInfo("PMC-to-Scav continuation active; the PMC death is frozen and regular Scav is unchanged.");
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

    public static bool BlocksTransitUi(ExtractionTimersPanel panel)
        => _context != null && !_context.Ending && _context.ExitsBound && _context.Scav != null
            && panel != null && panel.ProfileId == _context.Scav.Id
            && ReferenceEquals(LocalGameAccessor.GetGameUI(_context.Game)?.TimerPanel, panel);

    public static bool BlocksRegularScavSave(SessionResultShowOperation operation)
    {
        var context = _context;
        if (context == null || !context.Ending || context.Scav == null || operation == null
            || !ReferenceEquals(operation._session, context.Session)
            || operation._matchProfileId != context.OriginalPmc.Id)
            return false;
        if (!context.RegularScavSaveBlocked)
        {
            context.RegularScavSaveBlocked = true;
            Plugin.ModLogger.LogDebug("PMC-to-Scav result: regular Player-Scav save bypassed; loot is settled by the continuation message.");
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
        var context = _context;
        return context != null && context.Ending && context.Scav != null
            && settings?.serverId == context.ServerId && results?.ProfileId == context.Scav.Id
                ? context.InsuranceFailure : null;
    }

    private static async Task AwaitBackendEnd(Context context, Task original)
    {
        await original;
        context.BackendAcknowledged = true;
    }

    public static void RewritePmcResult(IEftSession session, string profileId, ref ExitStatus exitStatus, ref TimeSpan exitTime)
    {
        var context = _context;
        if (context == null || !context.Ending || !ReferenceEquals(session, context.Session)
            || profileId != context.OriginalPmc.Id)
            return;
        exitStatus = ExitStatus.Killed;
        exitTime = TimeSpan.FromSeconds(context.DeathTime);
        // Native GameEnd stored the final Scav's visual state. Change only presentation,
        // after the final payload was serialized/sent, to the PMC's cloned death appearance.
        session.LastPlayerState = context.PmcVisual;
        if (context.BackendAcknowledged && !context.ResultNoticeShown)
        {
            context.ResultNoticeShown = true;
            bool extracted = context.ScavOutcome == ExitStatus.Survived || context.ScavOutcome == ExitStatus.Runner;
            ShowNotice(extracted
                ? "PMC raid ended in death. Your Scav extracted; collect the recovered loot from messages."
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
            Plugin.ModLogger.LogWarning($"Scav activation acknowledgement unavailable; native settlement retains the issued identity: {ex.Message}");
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
            Plugin.ModLogger.LogWarning($"Unused Scav preparation could not be cancelled: {ex.Message}");
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
            throw new InvalidOperationException((string)response?["error"] ?? "The server rejected the Scav continuation.");
    }

    public static void NotifyUnavailable(string reason)
        => ShowNotice("Scav continuation unavailable; the PMC raid will end normally. "
            + (reason ?? string.Empty), warning: true);

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

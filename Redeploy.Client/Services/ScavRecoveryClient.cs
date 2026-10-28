using System;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Screens;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace Redeploy.Services;

// Kept free of EFT/Unity types so the actual request ownership and button policy
// can be exercised outside the game, without copying its logic into the tests.
internal sealed class ScavRecoveryFlow
{
    public object Session { get; private set; }
    public object Menu { get; private set; }
    public int Revision { get; private set; }
    public bool Checked { get; private set; }
    public bool Busy { get; private set; }
    public bool NeedsRestart { get; private set; }
    public bool BackendUnavailable { get; private set; }
    public string State { get; private set; } = "none";
    public string RecoveryId { get; private set; }
    public string Action { get; private set; } = "abandon";
    public string Message { get; private set; }
    public string Error { get; private set; }
    public bool Visible => State != "none" || Error != null;
    public bool BlocksMenu => Session != null && Menu != null && ((!Checked && !BackendUnavailable) || Busy || State != "none" || NeedsRestart);

    public void Attach(object session, object menu)
    {
        if (ReferenceEquals(Session, session) && ReferenceEquals(Menu, menu))
            return;
        bool sameSession = ReferenceEquals(Session, session);
        Session = session;
        Menu = menu;
        Revision++;
        Checked = false;
        Busy = false;
        Error = null;
        if (!sameSession)
        {
            NeedsRestart = false;
            BackendUnavailable = false;
            State = "none";
            RecoveryId = Message = null;
        }
    }

    public bool Owns(int revision, object session, object menu)
        => revision == Revision && ReferenceEquals(Session, session) && ReferenceEquals(Menu, menu);

    public int BeginStatus(bool explicitRetry)
    {
        if (Session == null || Menu == null || Busy || State == "restart" || ((Checked || BackendUnavailable) && !explicitRetry))
            return -1;
        Busy = Checked = true;
        Error = null;
        BackendUnavailable = false;
        return ++Revision;
    }

    public int BeginAction(string requiredState)
    {
        if (Busy || State != requiredState || Error != null || string.IsNullOrWhiteSpace(RecoveryId))
            return -1;
        Busy = true;
        return ++Revision;
    }

    public void ProfilesMayHaveChanged(int revision)
    {
        if (revision == Revision)
            NeedsRestart = true;
    }

    public bool Accept(int revision, string state, string recoveryId, string message, string action = null)
    {
        if (revision != Revision || !Busy)
            return false;
        if (state != "none" && state != "abandon" && state != "transfer" && state != "blocked")
            throw new InvalidOperationException("Unknown recovery state.");
        if ((state == "abandon" || state == "transfer") && string.IsNullOrWhiteSpace(recoveryId))
            throw new InvalidOperationException("Missing recovery identity.");
        if (state == "abandon" && action != null && action != "abandon" && action != "save")
            throw new InvalidOperationException("Unknown recovery action.");
        State = state == "none" && NeedsRestart ? "restart" : state;
        RecoveryId = recoveryId;
        Action = action ?? "abandon";
        Message = message;
        Error = null;
        Busy = false;
        return true;
    }

    public void Fail(int revision, string message)
    {
        if (revision != Revision)
            return;
        Error = message;
        Busy = false;
    }

    public void MissingBackend(int revision, string message)
    {
        if (revision != Revision)
            return;
        BackendUnavailable = true;
        Fail(revision, message);
    }

    public void Interrupt(string message)
    {
        // A native inventory error invalidates any concurrent refresh/finish await.
        Revision++;
        NeedsRestart = true;
        Error = message;
        Busy = false;
    }

    public void Release(int revision)
    {
        if (revision == Revision)
            Busy = false;
    }

    public void RequireRestart(int revision)
    {
        if (revision != Revision)
            return;
        NeedsRestart = true;
        State = "restart";
        Error = null;
        Busy = false;
    }

    public void DismissUnavailable()
    {
        if (!Busy && State == "none" && !NeedsRestart)
            Error = null;
    }

    public static bool MatchesTransfer(string pmcId, string accountScavId, string accountEquipmentId,
        string expectedScavId, string expectedEquipmentId)
        => !string.IsNullOrWhiteSpace(pmcId) && !string.IsNullOrWhiteSpace(expectedScavId)
            && !string.IsNullOrWhiteSpace(expectedEquipmentId) && pmcId != accountScavId
            && accountScavId == expectedScavId && accountEquipmentId == expectedEquipmentId;
}

internal static class ScavRecoveryClient
{
    public static ScavRecoveryFlow Flow { get; } = new();
    private static MainMenuShowOperation _operation;
    private static MenuScreen _menu;
    private static MenuScreen.MainMenuScreenController _menuController;
    private static ScavengerInventoryScreen.ScavengerInventoryScreenController _transfer;
    private static Action _finishTransfer;
    private static string _scavProfileId;
    private static string _scavEquipmentId;
    private static Client _http;
    private static IEftSession _httpSession;

    internal static bool OwnsTransfer(OfflineInventoryController controller)
        => controller != null && _transfer != null && !InGameStatus.InRaid
            && ReferenceEquals(_transfer.ScavController, controller)
            && ReferenceEquals(_transfer.Session, Flow.Session)
            && ReferenceEquals(EftScreenManager.Instance.CurrentScreenController, _transfer);

    public static void PreparingMenu(MainMenuShowOperation operation)
    {
        if (!InGameStatus.InRaid)
            _operation = operation;
    }

    public static void MenuShown(MenuScreen menu, MenuScreen.MainMenuBaseScreenController controller)
    {
        if (InGameStatus.InRaid || !(controller is MenuScreen.MainMenuScreenController main)
            || _operation?.iEftSession?.Profile == null || main.Profile?.Id != _operation.iEftSession.Profile.Id)
            return;
        _menu = menu;
        _menuController = main;
        Flow.Attach(_operation.iEftSession, main);
    }

    public static bool IsMainMenu => !InGameStatus.InRaid && _menu != null && _menu.isActiveAndEnabled
        && _menuController != null && ReferenceEquals(EftScreenManager.Instance.CurrentScreenController, _menuController)
        && ReferenceEquals(_operation?.iEftSession, Flow.Session);

    public static bool BlocksMenu(MainMenuShowOperation operation)
        => !InGameStatus.InRaid && ReferenceEquals(operation?.iEftSession, Flow.Session) && Flow.BlocksMenu;

    public static bool OwnsMutatedMenu(MainMenuShowOperation operation)
        => BlocksMenu(operation) && Flow.NeedsRestart;

    public static async void InventoryError()
    {
        if (Flow.Error != null)
            return;
        var session = (IEftSession)Flow.Session;
        var menu = Flow.Menu;
        Flow.Interrupt("An inventory update failed. Check status before resuming, or close and restart the game. Items already transferred remain in your stash.");
        if (_transfer != null)
            await ReturnToMenu(session, menu);
    }

    public static void Tick()
    {
        // Menu.Show can precede ScreenManager's current-controller assignment.
        // Waiting for readiness here does not poll the backend: BeginStatus admits
        // exactly one automatic request for this menu lifecycle.
        if (IsMainMenu && !Flow.Checked && !Flow.Busy && !Flow.BackendUnavailable && Flow.State != "restart")
            CheckStatus(false);
    }

    public static void Retry() => CheckStatus(true);

    private static bool Current(int revision, IEftSession session, object menu, bool mainMenuOnly = false)
        => Flow.Owns(revision, session, menu) && !InGameStatus.InRaid
            && ReferenceEquals(_operation?.iEftSession, session)
            && (mainMenuOnly ? IsMainMenu : IsMainMenu || _transfer != null);

    private static async void CheckStatus(bool retry)
    {
        if (!IsMainMenu)
            return;
        int revision = Flow.BeginStatus(retry);
        if (revision < 0)
            return;
        var session = (IEftSession)Flow.Session;
        var menu = Flow.Menu;
        try
        {
            JObject response = await Post(session, "status", new JObject());
            if (!Current(revision, session, menu, true))
                return;
            ReadStatus(revision, response);
        }
        catch (Exception ex)
        {
            if (Current(revision, session, menu, true))
            {
                if (IsMissingEndpoint(ex))
                    Flow.MissingBackend(revision, "Scav recovery is unavailable on this server. Install matching Redeploy Client and Server versions, then restart the game.");
                else
                    Flow.Fail(revision, "Could not check the interrupted Scav raid. Check the server connection and retry.");
            }
            LogFailure("status", ex);
        }
    }

    private static void ReadStatus(int revision, JObject response)
    {
        string state = (string)response["state"];
        // A proven unsafe outcome is a useful, fenced status even when success
        // is false. Its server message is explicitly whitelisted by this route.
        if (state != "blocked")
            RequireSuccess(response);
        string scavId = (string)response["scavProfileId"];
        string equipmentId = (string)response["scavEquipmentId"];
        if (state == "transfer" && (string.IsNullOrWhiteSpace(scavId) || string.IsNullOrWhiteSpace(equipmentId)))
            throw new InvalidOperationException("Incomplete transfer identity.");
        if (Flow.Accept(revision, state, (string)response["recoveryId"], (string)response["message"], (string)response["action"]))
        {
            _scavProfileId = scavId;
            _scavEquipmentId = equipmentId;
        }
    }

    // Called only by the explicit confirmation button, never by menu readiness.
    public static async void Abandon()
    {
        if (!IsMainMenu)
            return;
        int revision = Flow.BeginAction("abandon");
        if (revision < 0)
            return;
        var session = (IEftSession)Flow.Session;
        var menu = Flow.Menu;
        string recoveryId = Flow.RecoveryId;
        try
        {
            if (!(await session.FlushOperationQueue()).Succeed)
                throw new InvalidOperationException("Pending inventory operations did not finish.");
            if (!Current(revision, session, menu, true))
                return;
            Flow.ProfilesMayHaveChanged(revision); // Even a lost acknowledgement may have committed.
            MonoBehaviourSingleton<PreloaderUI>.Instance.NotifierView.SetProfileChangeHandlerState(_operation, false);
            JObject response = await Post(session, "abandon", new JObject { ["recoveryId"] = recoveryId });
            if (!Current(revision, session, menu, true))
                return;
            ReadStatus(revision, response);
        }
        catch (Exception ex)
        {
            if (Current(revision, session, menu, true))
                Flow.Fail(revision, "The recovery result could not be confirmed. Check status before trying again. Your profile remains protected.");
            LogFailure("abandon", ex);
        }
    }

    public static async void ResumeTransfer()
    {
        if (!IsMainMenu)
            return;
        int revision = Flow.BeginAction("transfer");
        if (revision < 0)
            return;
        var session = (IEftSession)Flow.Session;
        var menu = Flow.Menu;
        string expectedScavId = _scavProfileId;
        string expectedEquipmentId = _scavEquipmentId;
        try
        {
            // Flush before GetProfiles: never replace a profile underneath pending
            // inventory transactions, including a transfer interrupted in this process.
            if (!(await session.FlushOperationQueue()).Succeed)
                throw new InvalidOperationException("Pending inventory operations did not finish.");
            if (!Current(revision, session, menu, true))
                return;
            Flow.ProfilesMayHaveChanged(revision);
            MonoBehaviourSingleton<PreloaderUI>.Instance.NotifierView.SetProfileChangeHandlerState(_operation, false);
            await session.GetProfiles();
            if (!Current(revision, session, menu, true))
                return;
            var pmc = session.Profile;
            var scav = session.ProfileOfPet;
            if (!ScavRecoveryFlow.MatchesTransfer(pmc?.Id, scav?.Id, scav?.Inventory?.Equipment?.Id,
                    expectedScavId, expectedEquipmentId) || pmc.Inventory?.Stash == null
                || scav.Info?.Side != EPlayerSide.Savage || scav.Health == null)
                throw new InvalidOperationException("The current transfer identity does not match.");

            // Only current backend profiles are used. Never install a saved Scav
            // snapshot: items already moved to the stash must remain there.
            var main = new OfflineInventoryController(session, pmc, pmc.Id);
            var pet = new OfflineInventoryController(session, scav, pmc.Id);
            ProfileUpdatesHandler.Bind(pmc, session, main, null);
            ProfileUpdatesHandler.Bind(scav, session, pet, null);
            var health = new OfflineHealthController(scav.Health, pet, pmc.Skills, regeneration: false);
            scav.LearnAll();
            session.QueueStatus = EOperationQueueStatus.Idle;
            _transfer = new ScavengerInventoryScreen.ScavengerInventoryScreenController(main, pet, health, main.Inventory.Stash, session);
            var transfer = _transfer;
            _finishTransfer = () => FinishTransfer(session, menu, transfer);
            transfer.OnShowNextScreen += _finishTransfer;
            if (!(await transfer.ShowScreenAsync(EScreenState.Root)))
                throw new InvalidOperationException("The transfer screen could not open.");
            if (!Current(revision, session, menu))
                return;
            Flow.Release(revision);
        }
        catch (Exception ex)
        {
            if (Current(revision, session, menu))
            {
                Flow.Fail(revision, "Loot transfer could not open safely. Check status and retry, or close and restart the game. Already transferred items stay in your stash.");
                await ReturnToMenu(session, menu);
            }
            LogFailure("resume transfer", ex);
        }
    }

    private static async void FinishTransfer(IEftSession session, object menu,
        ScavengerInventoryScreen.ScavengerInventoryScreenController transfer)
    {
        if (InGameStatus.InRaid || !ReferenceEquals(_transfer, transfer) || !ReferenceEquals(Flow.Session, session)
            || !ReferenceEquals(Flow.Menu, menu) || !ReferenceEquals(EftScreenManager.Instance.CurrentScreenController, transfer))
            return;
        int revision = Flow.BeginAction("transfer");
        if (revision < 0)
            return;
        try
        {
            // Native confirmation covers any items the user deliberately leaves.
            if (!(await EftScreenManager.Instance.TryCloseAllScreens()))
            {
                Flow.Release(revision);
                return;
            }
            if (!Current(revision, session, menu))
                return;
            if (!(await session.FlushOperationQueue()).Succeed)
                throw new InvalidOperationException("Transfer operations did not finish.");
            if (!Current(revision, session, menu))
                return;
            if (!(await session.FinishScavSession()).Succeed)
                throw new InvalidOperationException("Transfer completion was not acknowledged.");
            if (!Current(revision, session, menu))
                return;
            Flow.RequireRestart(revision);
        }
        catch (Exception ex)
        {
            if (Current(revision, session, menu))
                Flow.Fail(revision, "Transfer completion could not be confirmed. Check status before resuming. Items already moved to your stash are preserved.");
            LogFailure("finish transfer", ex);
        }
        if (Current(revision, session, menu))
            await ReturnToMenu(session, menu);
    }

    private static async Task ReturnToMenu(IEftSession session, object menu)
    {
        if (InGameStatus.InRaid || !ReferenceEquals(Flow.Session, session) || !ReferenceEquals(Flow.Menu, menu))
            return;
        var transfer = _transfer;
        var finished = _finishTransfer;
        var controller = _menuController;
        try
        {
            if (transfer != null && finished != null)
                transfer.OnShowNextScreen -= finished;
            // Only a UI transition; no raid-result save and no profile restoration.
            await controller.ShowScreenAsync(EScreenState.Root, forced: true);
        }
        catch (Exception ex) { LogFailure("return to menu", ex); }
        finally
        {
            if (ReferenceEquals(_transfer, transfer))
            {
                _transfer = null;
                _finishTransfer = null;
            }
        }
    }

    private static async Task<JObject> Post(IEftSession session, string operation, JObject body)
    {
        // Use SPT's authenticated transport, but no automatic retries: a missing
        // optional endpoint should make one request, and mutation retries need an
        // explicit status check. Credentials are never stored in UI state or logs.
        if (!ReferenceEquals(_httpSession, session))
        {
            _http = new Client(RequestHandler.Host, session.GetPhpSessionId(), retries: 0);
            _httpSession = session;
        }
        var data = await _http.PostAsync("/redeploy/pmc-scav/recovery/" + operation,
            Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None)));
        return JObject.Parse(Encoding.UTF8.GetString(data));
    }

    private static void RequireSuccess(JObject response)
    {
        if ((bool?)response?["success"] != true)
            throw new InvalidOperationException("The server could not safely complete recovery.");
    }

    private static bool IsMissingEndpoint(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is WebException web && web.Response is HttpWebResponse response
                && response.StatusCode == HttpStatusCode.NotFound)
                return true;
            // SPT.Common.Client uses this exact exception for a non-2xx response.
            if (current.Message == "Http response status code: NotFound")
                return true;
        }
        return false;
    }

    // Never log request/response JSON, authentication data or profile snapshots.
    private static void LogFailure(string action, Exception exception)
        => Plugin.ModLogger.LogWarning($"Scav recovery {action} failed ({exception.GetType().Name}).");
}

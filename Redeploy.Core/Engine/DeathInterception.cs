using EFT;
using EFT.UI;
using EFT.UI.SessionEnd;
using HarmonyLib;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;
using Redeploy.Core.GameAccess;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Redeploy.Core.Engine;

/// <summary>
/// One mod's claim on the death-interception patch set. Every decision point of the
/// raid-keep-alive machinery is a separate hook so a driver can mirror its exact
/// legacy predicates. All hooks must be cheap and side-effect free except
/// <see cref="OnPlayerDied"/>, <see cref="OnExternalStop"/> and <see cref="OnRaidEnded"/>.
/// </summary>
public interface IDeathInterceptionDriver
{
    string Name { get; }

    /// <summary>Higher wins when several drivers want the same death.</summary>
    int Priority { get; }

    /// <summary>CG_Spawn (the death handler): claim this death instead of ending the raid?</summary>
    bool WantsToHandleDeath(LocalGame game);

    /// <summary>Called instead of the game's death handler when this driver claimed the death.</summary>
    void OnPlayerDied(LocalGame game);

    /// <summary>InitiateGameStopping: keep the death screen from starting?</summary>
    bool ShouldBlockDeathScreen(LocalGame game);

    /// <summary>Stop() with ExitStatus.Killed: keep the raid from ending?</summary>
    bool ShouldBlockKilledStop(LocalGame game);

    /// <summary>
    /// An external raid end (MIA timer, menu leave, transit, another mod) is proceeding —
    /// stand down so Stop's continuation (session end + raid serialization) gets through.
    /// </summary>
    void OnExternalStop();

    /// <summary>PreloaderUI.StartBlackScreenShow: suppress the raid-end fade?</summary>
    bool ShouldHoldBlackScreen();

    /// <summary>SessionResultExitStatus.Show with ExitStatus.Killed: suppress the results screen?</summary>
    bool ShouldSuppressKilledSessionResult();

    /// <summary>BaseLocalGame.GameEnd ran — reset per-raid state.</summary>
    void OnRaidEnded();

    /// <summary>
    /// Should GamePlayerOwner.CleanupOnDestroy be skipped right now even outside the
    /// engine pipeline? (E.g. while a respawn is pending/cooling down, an external raid
    /// end destroying the dead player's owner must not run vanilla cleanup.) The engine
    /// pipeline itself is always guarded regardless of this hook.
    /// </summary>
    bool ShouldGuardOwnerCleanup();
}

/// <summary>
/// The single Harmony patch set that keeps a raid alive across a player death, shared
/// by all respawn-style mods. Exactly one death-interception mod may act per death;
/// drivers register here and the highest-priority driver that wants a given death wins.
/// </summary>
public static class DeathInterception
{
    private const int KeepAlivePatchCount = 6;

    private static readonly List<IDeathInterceptionDriver> Drivers = new List<IDeathInterceptionDriver>();
    private static bool _patched;
    private static bool _keepAliveComplete;

    /// <summary>Raised when a driver claims a death (before its OnPlayerDied runs).</summary>
    public static event Action<LocalGame, IDeathInterceptionDriver> OnDeathIntercepted;

    /// <summary>
    /// Every keep-alive patch is installed. With one of them missing a claimed death could
    /// end half-handled (no death screen, a raid that never ends), so an incomplete set
    /// means Core claims nothing and blocks nothing.
    /// </summary>
    internal static bool IsOperational => _keepAliveComplete;

    internal static int DriverCount => Drivers.Count;

    /// <summary>
    /// Core can replace the local body in this session: keep-alive patches complete, at
    /// least one driver registered, Fika not loaded — evaluated in that order, so the Fika
    /// probe is never asked before a driver exists. When false, Core is idle: no death is
    /// claimed, no guard blocks anything, and RunRespawn is refused before any teardown.
    /// </summary>
    internal static bool IsAvailable
        => AvailabilityRule.Evaluate(IsOperational, DriverCount, () => FikaCompatCheck.IsFikaLoaded);

    public static void Register(IDeathInterceptionDriver driver)
    {
        if (driver == null || Drivers.Contains(driver))
            return;

        Drivers.Add(driver);

        // OrderBy is stable: drivers of equal priority keep their registration order, so
        // which one claims a death never varies between runs.
        var ordered = Drivers.OrderByDescending(d => d.Priority).ToList();
        Drivers.Clear();
        Drivers.AddRange(ordered);

        CoreLog.LogInfo($"Death-interception driver registered: {driver.Name} (priority {driver.Priority})");
    }

    public static void Unregister(IDeathInterceptionDriver driver)
    {
        Drivers.Remove(driver);
    }

    public static void EnsurePatched()
    {
        if (_patched)
            return;
        _patched = true;

        // One patch at a time: a target lost to a game update must neither abort the
        // caller's Awake (ArenaMode installs dozens more sets after this) nor leave the
        // set half-installed while pretending to work.
        var keepAlive = 0;
        keepAlive += Install(() => new CgSpawnPatch(), nameof(CgSpawnPatch));
        keepAlive += Install(() => new InitiateGameStoppingPatch(), nameof(InitiateGameStoppingPatch));
        keepAlive += Install(() => new StopPatch(), nameof(StopPatch));
        keepAlive += Install(() => new BlackScreenGuardPatch(), nameof(BlackScreenGuardPatch));
        keepAlive += Install(() => new SessionResultGuardPatch(), nameof(SessionResultGuardPatch));
        keepAlive += Install(() => new OwnerCleanupGuardPatch(), nameof(OwnerCleanupGuardPatch));

        // The GameEnd postfix (drivers' OnRaidEnded, engine reset) lives with its raid-start
        // twin so the two are always paired.
        RaidLifecycle.EnsurePatched();

        _keepAliveComplete = keepAlive == KeepAlivePatchCount && RaidLifecycle.IsRaidEndPatched;
        if (!_keepAliveComplete)
            CoreLog.LogError("Death interception is incomplete — Core stands down: no death is claimed, nothing is blocked, RunRespawn is refused.");

        // Optional: losing these degrades a respawn, it does not disable the mod.
        Install(() => new StatisticsRestorePatch(), nameof(StatisticsRestorePatch));
        DeadBodyGuard.EnsurePatched();
    }

    private static int Install(Func<ModulePatch> create, string name)
    {
        try
        {
            create().Enable();
            return 1;
        }
        catch (Exception ex)
        {
            CoreLog.LogError($"{name} could not be installed: {ex}");
            return 0;
        }
    }

    /// <summary>The highest-priority driver that wants this death, or null. Pure apart from the drivers' own predicates.</summary>
    internal static IDeathInterceptionDriver FindClaimant(LocalGame game)
    {
        foreach (var driver in Drivers)
        {
            try
            {
                if (driver.WantsToHandleDeath(game))
                    return driver;
            }
            catch (Exception ex)
            {
                CoreLog.LogWarning($"Death-interception driver '{driver.Name}' failed in WantsToHandleDeath: {ex}");
            }
        }

        return null;
    }

    internal static void NotifyDriversRaidEnded()
    {
        foreach (var driver in Drivers)
        {
            try
            {
                driver.OnRaidEnded();
            }
            catch (Exception ex)
            {
                CoreLog.LogWarning($"Death-interception driver '{driver.Name}' failed in OnRaidEnded: {ex}");
            }
        }
    }

    private static bool Any(Func<IDeathInterceptionDriver, bool> predicate)
    {
        foreach (var driver in Drivers)
        {
            try
            {
                if (predicate(driver))
                    return true;
            }
            catch (Exception ex)
            {
                CoreLog.LogWarning($"Death-interception driver '{driver.Name}' failed: {ex}");
            }
        }

        return false;
    }

    private sealed class CgSpawnPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "CG_Spawn");
        }

        [PatchPrefix]
        private static bool Prefix(BaseLocalGame<EftGamePlayerOwner> __instance)
        {
            if (__instance is not LocalGame localGame)
                return true;

            // Fail closed: an incomplete patch set, no driver or Fika means the death runs
            // vanilla instead of being claimed by machinery that cannot finish it.
            if (!IsAvailable)
                return true;

            var body = localGame.LocalPlayer;
            var claimant = FindClaimant(localGame);
            if (claimant == null)
            {
                // Vanilla takes this death; the OnDead-time prediction must stop for it.
                RespawnClaims.MarkProcessed(body);
                return true;
            }

            // Pending from here until the driver has acted, whatever the raid status.
            RespawnClaims.BeginClaim(body, claimant);
            CoreSignals.Respawn.Raise(OnDeathIntercepted, h => h(localGame, claimant), nameof(OnDeathIntercepted));
            RespawnApi.PublishDeathClaimed(body, claimant.Name);

            try
            {
                claimant.OnPlayerDied(localGame);
            }
            catch (Exception ex)
            {
                // Fail open: a throwing driver must not swallow the death — let the
                // vanilla death handler run so the death screen still appears.
                RespawnClaims.EndDispatch(failedOpen: true);
                CoreLog.LogError($"Death-interception driver '{claimant.Name}' failed in OnPlayerDied — vanilla death proceeds: {ex}");
                return true;
            }

            RespawnClaims.EndDispatch(failedOpen: false);
            return false;
        }
    }

    private sealed class InitiateGameStoppingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "InitiateGameStopping");
        }

        [PatchPrefix]
        private static bool Prefix(BaseLocalGame<EftGamePlayerOwner> __instance)
        {
            if (__instance is not LocalGame localGame)
                return true;

            if (!IsAvailable)
                return true;

            return !Any(d => d.ShouldBlockDeathScreen(localGame));
        }
    }

    private sealed class StopPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            // Must target the DECLARED override: LocalGame.Stop kills the three
            // spawn scenarios (_bossSpawnScenario/_nonWavesSpawnScenario/
            // _wavesSpawnScenario) BEFORE chaining to base.Stop — a prefix on the
            // base declaration only fires after that, so a blocked Killed-stop
            // would leave every continuation raid with bot spawning permanently
            // dead. On the override the block prevents the scenario shutdown too.
            return AccessTools.Method(
                typeof(LocalGame),
                nameof(LocalGame.Stop),
                new[] { typeof(string), typeof(ExitStatus), typeof(string), typeof(float) });
        }

        [PatchPrefix]
        private static bool Prefix(LocalGame __instance, ExitStatus exitStatus)
        {
            var localGame = __instance;

            // An unavailable Core blocks nothing; the stand-down below is still delivered.
            if (exitStatus == ExitStatus.Killed && IsAvailable && Any(d => d.ShouldBlockKilledStop(localGame)))
                return false;

            // An external raid end (MIA timer, menu leave, transit, another mod) is
            // proceeding. Drivers stand down so the black-screen guard lets Stop's
            // continuation (OnGameSessionEnd + raid serialization) through — otherwise
            // the game soft-locks in Stopping and the raid is never saved.
            foreach (var driver in Drivers)
            {
                try
                {
                    driver.OnExternalStop();
                }
                catch (Exception ex)
                {
                    CoreLog.LogWarning($"Death-interception driver '{driver.Name}' failed in OnExternalStop: {ex}");
                }
            }

            return true;
        }
    }

    private sealed class BlackScreenGuardPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => typeof(PreloaderUI).GetMethod(nameof(PreloaderUI.StartBlackScreenShow));

        [PatchPrefix]
        private static bool Prefix()
        {
            if (!IsAvailable)
                return true;

            if (!Any(d => d.ShouldHoldBlackScreen()))
                return true;

            CoreLog.LogInfo("Blocked raid-end black screen while respawn is pending.");
            return false;
        }
    }

    private sealed class SessionResultGuardPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(SessionResultExitStatus),
                nameof(SessionResultExitStatus.Show),
                new[]
                {
                    typeof(Profile),
                    typeof(PlayerVisualRepresentation),
                    typeof(ESideType),
                    typeof(ExitStatus),
                    typeof(TimeSpan),
                    typeof(IEftSession),
                    typeof(bool)
                });
        }

        [PatchPrefix]
        private static bool Prefix(ExitStatus exitStatus)
        {
            if (exitStatus != ExitStatus.Killed)
                return true;

            if (!IsAvailable)
                return true;

            return !Any(d => d.ShouldSuppressKilledSessionResult());
        }
    }

    /// <summary>
    /// During the respawn pipeline, skip GamePlayerOwner.CleanupOnDestroy so deferred
    /// Destroy does not null MyPlayer or start CloseBattleUi after the new owner exists.
    /// </summary>
    private sealed class OwnerCleanupGuardPatch : ModulePatch
    {
        private static readonly FieldInfo OwnerDisposableField =
            AccessTools.Field(typeof(PlayerOwner), "OnDestroyCompositeDisposable");

        protected override MethodBase GetTargetMethod()
            => typeof(GamePlayerOwner).GetMethod(
                nameof(GamePlayerOwner.CleanupOnDestroy),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        [PatchPrefix]
        private static bool Prefix(GamePlayerOwner __instance)
        {
            // The owner the pipeline retired is skipped whatever the flags say by now: its
            // deferred Destroy resolves at the end of the replacing frame, after the
            // pipeline flag is down, and vanilla cleanup would null MyPlayer for the NEW body.
            if (!OwnerCleanupHelper.IsRetired(__instance))
            {
                if (!IsAvailable)
                    return true;

                if (!RespawnEngineState.IsPipelineRunning && !Any(d => d.ShouldGuardOwnerCleanup()))
                    return true;
            }

            // Do not invoke CleanupOnDestroy virtually — that re-enters this prefix and overflows the stack.
            if (OwnerDisposableField.GetValue(__instance) is CompositeDisposable disposable)
                disposable.Dispose();

            return false;
        }
    }

    /// <summary>
    /// A new statistics session on respawn clears EftStats.Victims; restore the
    /// pre-respawn kill list snapshotted by the pipeline.
    /// </summary>
    private sealed class StatisticsRestorePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BaseStatisticsManager), nameof(BaseStatisticsManager.BeginStatisticsSession));
        }

        [PatchPostfix]
        private static void Postfix(BaseStatisticsManager __instance)
        {
            if (!RespawnEngineState.IsPipelineRunning)
                return;

            // Timed here because it runs inside player creation, where the pipeline cannot place a mark.
            var started = RespawnPipelineTiming.StartVictimsRestore();
            RaidVictimsSnapshot.Restore(__instance.Profile);
            RespawnPipelineTiming.RecordVictimsRestore(started, __instance.Profile);
        }
    }
}

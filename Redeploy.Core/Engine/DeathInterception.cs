using EFT;
using EFT.UI;
using EFT.UI.SessionEnd;
using HarmonyLib;
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
    private static readonly List<IDeathInterceptionDriver> Drivers = new List<IDeathInterceptionDriver>();
    private static bool _patched;

    /// <summary>Raised when a driver claims a death (before its OnPlayerDied runs).</summary>
    public static event Action<LocalGame, IDeathInterceptionDriver> OnDeathIntercepted;

    public static void Register(IDeathInterceptionDriver driver)
    {
        if (driver == null || Drivers.Contains(driver))
            return;

        Drivers.Add(driver);
        Drivers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
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

        new CgSpawnPatch().Enable();
        new InitiateGameStoppingPatch().Enable();
        new StopPatch().Enable();
        new BlackScreenGuardPatch().Enable();
        new SessionResultGuardPatch().Enable();
        new GameEndPatch().Enable();
        new OwnerCleanupGuardPatch().Enable();
        new StatisticsRestorePatch().Enable();
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

            IDeathInterceptionDriver claimant = null;
            foreach (var driver in Drivers)
            {
                try
                {
                    if (driver.WantsToHandleDeath(localGame))
                    {
                        claimant = driver;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    CoreLog.LogWarning($"Death-interception driver '{driver.Name}' failed in WantsToHandleDeath: {ex}");
                }
            }

            if (claimant == null)
                return true;

            try
            {
                OnDeathIntercepted?.Invoke(localGame, claimant);
            }
            catch (Exception ex)
            {
                CoreLog.LogWarning($"OnDeathIntercepted subscriber failed: {ex}");
            }

            try
            {
                claimant.OnPlayerDied(localGame);
            }
            catch (Exception ex)
            {
                // Fail open: a throwing driver must not swallow the death — let the
                // vanilla death handler run so the death screen still appears.
                CoreLog.LogError($"Death-interception driver '{claimant.Name}' failed in OnPlayerDied — vanilla death proceeds: {ex}");
                return true;
            }

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

            return !Any(d => d.ShouldBlockDeathScreen(localGame));
        }
    }

    private sealed class StopPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(
                typeof(BaseLocalGame<EftGamePlayerOwner>),
                nameof(BaseLocalGame<EftGamePlayerOwner>.Stop),
                new[] { typeof(string), typeof(ExitStatus), typeof(string), typeof(float) });
        }

        [PatchPrefix]
        private static bool Prefix(BaseLocalGame<EftGamePlayerOwner> __instance, ExitStatus exitStatus)
        {
            if (__instance is not LocalGame localGame)
                return true;

            if (exitStatus == ExitStatus.Killed && Any(d => d.ShouldBlockKilledStop(localGame)))
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

            return !Any(d => d.ShouldSuppressKilledSessionResult());
        }
    }

    private sealed class GameEndPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "GameEnd");
        }

        [PatchPostfix]
        private static void Postfix()
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

            RaidLifecycle.RaiseRaidEnded();
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
            if (!RespawnEngineState.IsPipelineRunning && !Any(d => d.ShouldGuardOwnerCleanup()))
                return true;

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

            RaidVictimsSnapshot.Restore(__instance.Profile);
        }
    }
}

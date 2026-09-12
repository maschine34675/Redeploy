using EFT;
using HarmonyLib;
using Redeploy.Core.Compat;
using SPT.Reflection.Patching;
using System;
using System.Reflection;
using System.Threading.Tasks;

namespace Redeploy.Core.Engine;

/// <summary>
/// Core-level raid boundaries, patched once no matter how many consumers call
/// <see cref="EnsurePatched"/>. Start and end are always installed together, so a consumer
/// that sees a raid start also gets its end. Both postfixes reset the engine's per-raid
/// state first (the end also notifies the death-interception drivers), then raise their
/// event one subscriber at a time.
/// </summary>
public static class RaidLifecycle
{
    private static bool _patched;

    /// <summary>
    /// Raised from GameWorld.OnGameStarted (player exists, raid is live). Not raised for
    /// trader narrate scenes, which never reach GameEnd.
    /// </summary>
    public static event Action<GameWorld> OnRaidStarted;

    /// <summary>
    /// Raised after BaseLocalGame.GameEnd, once the drivers have reset and the engine state
    /// is cleared. Not raised for the hideout. Fika's CoopGame is a BaseLocalGame too and
    /// does reach this (ArenaSession relies on the pairing).
    /// </summary>
    public static event Action OnRaidEnded;

    /// <summary>The GameEnd postfix is installed — part of the keep-alive set DeathInterception requires.</summary>
    internal static bool IsRaidEndPatched { get; private set; }

    public static void EnsurePatched()
    {
        if (_patched)
            return;
        _patched = true;

        try
        {
            new GameStartedPatch().Enable();
        }
        catch (Exception ex)
        {
            CoreLog.LogError($"Raid-start patch could not be installed: {ex}");
        }

        try
        {
            new GameEndPatch().Enable();
            IsRaidEndPatched = true;
        }
        catch (Exception ex)
        {
            CoreLog.LogError($"Raid-end patch could not be installed: {ex}");
        }
    }

    internal static void RaiseRaidEnded()
        => CoreSignals.Lifecycle.Raise(OnRaidEnded, h => h(), nameof(OnRaidEnded));

    private static void RaiseRaidStarted(GameWorld world)
        => CoreSignals.Lifecycle.Raise(OnRaidStarted, h => h(world), nameof(OnRaidStarted));

    private sealed class GameStartedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(GameWorld).GetMethod(nameof(GameWorld.OnGameStarted));
        }

        [PatchPostfix]
        private static void Postfix(GameWorld __instance)
        {
            // Trader narrate scenes start a NarrateGameWorld and never reach GameEnd; a start
            // without an end would leave every consumer's per-raid state armed. The hideout
            // never calls OnGameStarted at all, so the end-side filter needs no twin here.
            if (__instance is NarrateGameWorld)
                return;

            // A raid whose GameEnd was missed is cleaned up here rather than never.
            RespawnEngineState.ResetForRaidBoundary();

            // Every consumer that rebinds the owner needs Freecam's camera-operation patches,
            // not only Redeploy. Kept apart from the raise: a failure here must cost nobody
            // the raid start.
            try
            {
                FreecamCompatHelper.EnablePatchesIfPresent();
            }
            catch (Exception ex)
            {
                CoreLog.LogWarning($"Freecam compatibility patches could not be enabled: {ex}");
            }

            RaiseRaidStarted(__instance);
        }
    }

    private sealed class GameEndPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "GameEnd");
        }

        // Untyped on purpose: the patch sits on a shared generic base, so the instance may be
        // a HideoutGame (BaseLocalGame<HideoutPlayerOwner>) should Mono share the method body.
        [PatchPostfix]
        private static void Postfix(object __instance, Task __result)
        {
            if (__instance is HideoutGame)
                return;

            // GameEnd is async: an exception in its synchronous part (before the first await)
            // lands in the task, and this postfix still runs. The handlers run anyway, but
            // the usual guarantees (CleanUp done, Status == Stopped) do not hold then.
            if (__result != null && __result.IsFaulted)
                CoreLog.LogWarning($"GameEnd faulted before its first await; raid-end handlers run regardless: {__result.Exception?.GetBaseException().Message}");

            // Drivers first, as before: their per-raid reset must precede consumer handlers
            // such as ArenaSession's, which read driver state.
            DeathInterception.NotifyDriversRaidEnded();
            RespawnEngineState.ResetForRaidBoundary();
            RaiseRaidEnded();
        }
    }
}

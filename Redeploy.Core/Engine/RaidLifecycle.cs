using EFT;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace Redeploy.Core.Engine;

/// <summary>
/// Core-level raid lifecycle events, patched once no matter how many consumers call
/// <see cref="EnsurePatched"/>. OnRaidEnded is raised by the death-interception hub's
/// GameEnd postfix (enable that via <see cref="DeathInterception.EnsurePatched"/>).
/// </summary>
public static class RaidLifecycle
{
    private static bool _patched;

    /// <summary>Raised from GameWorld.OnGameStarted (player exists, raid is live).</summary>
    public static event Action<GameWorld> OnRaidStarted;

    /// <summary>Raised after BaseLocalGame.GameEnd.</summary>
    public static event Action OnRaidEnded;

    public static void EnsurePatched()
    {
        if (_patched)
            return;
        _patched = true;

        new GameStartedPatch().Enable();
    }

    internal static void RaiseRaidEnded()
    {
        try
        {
            OnRaidEnded?.Invoke();
        }
        catch (Exception ex)
        {
            CoreLog.LogWarning($"OnRaidEnded subscriber failed: {ex}");
        }
    }

    private sealed class GameStartedPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return typeof(GameWorld).GetMethod(nameof(GameWorld.OnGameStarted));
        }

        [PatchPostfix]
        private static void Postfix(GameWorld __instance)
        {
            try
            {
                OnRaidStarted?.Invoke(__instance);
            }
            catch (Exception ex)
            {
                CoreLog.LogWarning($"OnRaidStarted subscriber failed: {ex}");
            }
        }
    }
}

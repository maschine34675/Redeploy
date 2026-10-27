using EFT;
using Redeploy.Config;
using Redeploy.Core.Engine;
using Redeploy.Core.Compat;

namespace Redeploy.Services;

/// <summary>
/// Redeploy's claim on the shared death-interception patch set. The predicates are
/// the verbatim gates of the pre-Core patches, so behavior is unchanged. Priority 0:
/// any mode-style mod (e.g. ArenaMode) that wants the death outranks the prompt flow.
/// </summary>
internal sealed class RedeployDriver : IDeathInterceptionDriver
{
    public string Name => "Redeploy";

    public int Priority => 0;

    private static bool ShouldIntercept(LocalGame game, bool refreshCompatibility = false)
    {
        if (!Settings.Enabled.Value)
            return false;

        if (!RespawnSessionState.IsInRaid)
            return false;

        if (RespawnSessionState.HasGivenUp)
            return false;

        if (PmcScavSession.HasSwitched(game))
            return false;

        if (RespawnSessionState.IsRespawnInProgress)
            return false;

        if (!Settings.HasRespawnsRemaining(RespawnSessionState.RespawnsUsed))
            return false;

        if (game?.Location?.Id == "hideout")
            return false;

        // Mirrors RespawnService.OnPlayerDied exactly: a claim the service then cannot
        // serve would swallow the death (no prompt, raid never ending). A death that
        // arrives while the raid is already stopping belongs to that stop.
        if (game?.LocalPlayer == null || !game.LocalPlayer.IsYourPlayer)
            return false;

        if (game.Status != GameStatus.Started)
            return false;

        // Verify at death admission, not in repeatedly polled UI/death-screen predicates.
        return (refreshCompatibility ? BodyReplacementCompatibility.GetRejectionReason()
            : BodyReplacementCompatibility.CachedRejectionReason) == null;
    }

    public bool WantsToHandleDeath(LocalGame game) => ShouldIntercept(game, refreshCompatibility: true);

    public void OnPlayerDied(LocalGame game) => RespawnService.OnPlayerDied(game);

    public bool ShouldBlockDeathScreen(LocalGame game)
        => RespawnSessionState.PendingRespawnChoice && ShouldIntercept(game);

    public bool ShouldBlockKilledStop(LocalGame game)
        => RespawnSessionState.PendingRespawnChoice && ShouldIntercept(game);

    public void OnExternalStop()
    {
        if (!RespawnSessionState.PendingRespawnChoice && !RespawnSessionState.IsRespawnInProgress)
            return;

        Plugin.ModLogger.LogWarning("External raid end while respawn pending — standing down.");
        RespawnSessionState.HasGivenUp = true;
        RespawnSessionState.PendingRespawnChoice = false;
        UI.RespawnPromptPanel.Instance?.Hide();
    }

    public bool ShouldHoldBlackScreen()
    {
        if (!Settings.Enabled.Value || RespawnSessionState.HasGivenUp)
            return false;

        return RespawnSessionState.PendingRespawnChoice || RespawnSessionState.IsRespawnInProgress;
    }

    public bool ShouldSuppressKilledSessionResult()
    {
        if (!Settings.Enabled.Value)
            return false;

        return RespawnSessionState.PendingRespawnChoice && !RespawnSessionState.HasGivenUp;
    }

    public void OnRaidEnded() => RespawnSessionState.OnRaidEnded();

    // Verbatim gate of the pre-Core RespawnOwnerCleanupPatch: armed for the whole
    // respawn flow including the cooldown wait, so an external raid end during the
    // cooldown skips vanilla CleanupOnDestroy exactly like the old build did.
    public bool ShouldGuardOwnerCleanup()
        => Settings.Enabled.Value && RespawnSessionState.IsRespawnInProgress;
}

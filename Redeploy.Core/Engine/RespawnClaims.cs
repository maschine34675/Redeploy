using Comfort.Common;
using EFT;
using System;

namespace Redeploy.Core.Engine;

/// <summary>
/// Which local body a driver has claimed, which one is resolved, and whether the raid has
/// already ended — the state behind RespawnApi.IsRespawnPending and the "never after
/// RaidEnded" rule of ReplacementFailed. Evaluated live; nothing here latches on the order
/// of Stop prefixes. Reset at both raid boundaries.
/// </summary>
internal static class RespawnClaims
{
    private static Player _claimedBody;
    private static IDeathInterceptionDriver _claimant;
    private static Player _processedBody;
    private static bool _dispatching;
    private static object _raidEndedGame;

    /// <summary>The CG_Spawn prefix found a claimant: pending from here until the driver has acted.</summary>
    internal static void BeginClaim(Player body, IDeathInterceptionDriver claimant)
    {
        _claimedBody = body;
        _claimant = claimant;
        _dispatching = true;
    }

    /// <summary>The driver's OnPlayerDied returned. A driver that threw let the death run vanilla: resolved.</summary>
    internal static void EndDispatch(bool failedOpen)
    {
        _dispatching = false;
        if (failedOpen)
            Resolve(_claimedBody);
    }

    /// <summary>No driver wanted this death at CG_Spawn: vanilla proceeds, and the prediction must stop.</summary>
    internal static void MarkProcessed(Player body) => Resolve(body);

    /// <summary>A replacement of this body finished, with or without a new body.</summary>
    internal static void Resolve(Player body)
    {
        if (ReferenceEquals(body, null))
            return;

        _processedBody = body;
        if (ReferenceEquals(body, _claimedBody))
        {
            _claimedBody = null;
            _claimant = null;
        }
    }

    /// <summary>GameEnd ran for this game: no pipeline event is published for it any more.</summary>
    internal static void MarkRaidEnded(object game) => _raidEndedGame = game;

    internal static bool RaidEndedFor(object game)
        => game != null && ReferenceEquals(game, _raidEndedGame);

    internal static void ResetForRaidBoundary()
    {
        _claimedBody = null;
        _claimant = null;
        _processedBody = null;
        _dispatching = false;
        _raidEndedGame = null;
    }

    internal static bool IsPending(Player body)
    {
        try
        {
            var game = Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
            if (ReferenceEquals(body, null))
                body = game?.LocalPlayer;

            // Unity's overloaded == : a destroyed body is not pending anything.
            var hasBody = body != null;
            var isClaimed = hasBody && ReferenceEquals(body, _claimedBody);
            var isCurrentDeadLocalBody = hasBody && game != null
                && ReferenceEquals(body, game.LocalPlayer)
                && body.HealthController?.IsAlive == false;

            var claimant = _claimant;
            return RespawnClaimLogic.Decide(
                hasBody: hasBody,
                dispatchingThisBody: _dispatching && isClaimed,
                coreCanReplace: game != null && DeathInterception.IsAvailable && !RespawnEngine.RaidIsTearingDown(game),
                isClaimedBody: isClaimed,
                claimantHolds: () => SafeHolds(claimant),
                isProcessedBody: hasBody && ReferenceEquals(body, _processedBody),
                isCurrentDeadLocalBody: isCurrentDeadLocalBody,
                aDriverWouldClaim: () => DeathInterception.FindClaimant(game) != null);
        }
        catch (Exception ex)
        {
            // A query must never throw into a consumer's death handler.
            CoreLog.LogWarning($"IsRespawnPending failed; answering false: {ex.Message}");
            return false;
        }
    }

    private static bool SafeHolds(IDeathInterceptionDriver claimant)
    {
        if (claimant == null)
            return false;

        try
        {
            return claimant.ShouldHoldBlackScreen();
        }
        catch (Exception ex)
        {
            CoreLog.LogWarning($"Death-interception driver '{claimant.Name}' failed in ShouldHoldBlackScreen: {ex.Message}");
            return false;
        }
    }
}

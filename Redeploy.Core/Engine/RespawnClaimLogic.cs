using System;

namespace Redeploy.Core.Engine;

/// <summary>
/// The decision behind RespawnApi.IsRespawnPending, as a pure function so its truth table
/// is tested (docs/RESPAWN-API.md, sections 3.2 and 8). <see cref="RespawnClaims"/> reads
/// the game and supplies each operand; nothing here touches Unity or EFT. The two operands
/// that ask a driver are lazy, so a driver hook is only invoked when an earlier check has
/// not already settled the answer — while Core is idle no driver is consulted at all.
/// </summary>
public static class RespawnClaimLogic
{
    /// <param name="hasBody">A body was named or the game has a local player (a destroyed body counts as none).</param>
    /// <param name="dispatchingThisBody">The DeathClaimed dispatch for exactly this body is in progress.</param>
    /// <param name="coreCanReplace">There is a LocalGame, Core is available, and the raid is not tearing down.</param>
    /// <param name="isClaimedBody">A driver claimed this body's death and has not resolved it.</param>
    /// <param name="claimantHolds">That driver still holds the raid open (ShouldHoldBlackScreen, false when it throws).</param>
    /// <param name="isProcessedBody">This body was left to vanilla at CG_Spawn, its driver threw, or its replacement resolved.</param>
    /// <param name="isCurrentDeadLocalBody">This body is game.LocalPlayer and dead (the OnDead-time prediction window).</param>
    /// <param name="aDriverWouldClaim">A registered driver wants this death right now.</param>
    public static bool Decide(
        bool hasBody,
        bool dispatchingThisBody,
        bool coreCanReplace,
        bool isClaimedBody,
        Func<bool> claimantHolds,
        bool isProcessedBody,
        bool isCurrentDeadLocalBody,
        Func<bool> aDriverWouldClaim)
    {
        if (!hasBody)
            return false;

        // The DeathClaimed window is always pending, whatever the raid status: an Arena
        // claim can arrive during Stopping, and the handlers must see one consistent answer.
        if (dispatchingThisBody)
            return true;

        if (!coreCanReplace)
            return false;

        if (isClaimedBody)
            return claimantHolds != null && claimantHolds();

        // A resolved body must never re-enter the prediction below: after a failed
        // replacement the dead game.LocalPlayer would otherwise read as pending again as
        // long as any driver would claim it (an Arena raid with Redeploy installed).
        if (isProcessedBody)
            return false;

        return isCurrentDeadLocalBody && aDriverWouldClaim != null && aDriverWouldClaim();
    }
}

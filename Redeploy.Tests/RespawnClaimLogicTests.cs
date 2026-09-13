using Redeploy.Core.Engine;
using Xunit;

namespace Redeploy.Tests;

/// <summary>
/// The truth table behind RespawnApi.IsRespawnPending (docs/RESPAWN-API.md, section 8.1).
/// Each case names the in-game situation it stands for.
/// </summary>
public class RespawnClaimLogicTests
{
    private static bool Decide(
        bool hasBody = true, bool dispatchingThisBody = false, bool coreCanReplace = true,
        bool isClaimedBody = false, bool claimantHolds = false, bool isProcessedBody = false,
        bool isCurrentDeadLocalBody = false, bool aDriverWouldClaim = false)
        => RespawnClaimLogic.Decide(hasBody, dispatchingThisBody, coreCanReplace, isClaimedBody,
            () => claimantHolds, isProcessedBody, isCurrentDeadLocalBody, () => aDriverWouldClaim);

    [Fact]
    public void DriversAreNotConsultedWhenAnEarlierCheckSettlesTheAnswer()
    {
        // Core idle, dispatch window, processed body: no driver hook runs at all.
        Func<bool> forbidden = () => throw new InvalidOperationException("driver consulted");
        Assert.False(RespawnClaimLogic.Decide(true, false, false, true, forbidden, false, true, forbidden));
        Assert.True(RespawnClaimLogic.Decide(true, true, false, true, forbidden, false, true, forbidden));
        Assert.False(RespawnClaimLogic.Decide(true, false, true, false, forbidden, true, true, forbidden));
        // A claimed body asks its claimant only, never the claim search.
        Assert.True(RespawnClaimLogic.Decide(true, false, true, true, () => true, false, true, forbidden));
        Assert.False(RespawnClaimLogic.Decide(true, false, true, false, forbidden, false, false, forbidden));
    }

    [Fact]
    public void PredictionAtOnDeadTimeNeedsADriverThatWantsTheDeath()
    {
        // Inside Player.OnDead postfixes, before CG_Spawn: the current local body is dead.
        Assert.True(Decide(isCurrentDeadLocalBody: true, aDriverWouldClaim: true));
        Assert.False(Decide(isCurrentDeadLocalBody: true, aDriverWouldClaim: false));
    }

    [Fact]
    public void DispatchWindowIsAlwaysPendingEvenWhileTheRaidTearsDown()
    {
        // An Arena claim can arrive during Stopping; DeathClaimed handlers see one answer.
        Assert.True(Decide(dispatchingThisBody: true, coreCanReplace: false));
        Assert.True(Decide(dispatchingThisBody: true, isProcessedBody: true));
    }

    [Fact]
    public void AClaimedBodyIsPendingExactlyWhileItsDriverHolds()
    {
        Assert.True(Decide(isClaimedBody: true, claimantHolds: true));
        // Give-up, stand-down, terminal Team DM hold: the driver no longer holds.
        Assert.False(Decide(isClaimedBody: true, claimantHolds: false));
    }

    [Fact]
    public void RedeployPromptWithoutARespawnItemHoldsUntilTheGiveUp()
    {
        // The prompt only offers "end raid", but it is still shown: pending ...
        Assert.True(Decide(isClaimedBody: true, claimantHolds: true));
        // ... and false from the give-up on, before the Stop(Killed) that follows.
        Assert.False(Decide(isClaimedBody: true, claimantHolds: false, isCurrentDeadLocalBody: true, aDriverWouldClaim: true));
    }

    [Fact]
    public void MatchDecidingTeamDmDeathPredictsTrueThenTurnsFalseAfterTheClaim()
    {
        // Documented heuristic deviation: the prediction only knows a driver will claim.
        Assert.True(Decide(isCurrentDeadLocalBody: true, aDriverWouldClaim: true));
        // After the claim the driver's terminal hold releases immediately.
        Assert.False(Decide(isClaimedBody: true, claimantHolds: false, isCurrentDeadLocalBody: true, aDriverWouldClaim: true));
    }

    [Fact]
    public void AThrowingDriverFailsOpenToVanillaAndIsNotPending()
    {
        // EndDispatch(failedOpen: true) resolves the body: processed, no longer claimed.
        Assert.False(Decide(isProcessedBody: true, isCurrentDeadLocalBody: true, aDriverWouldClaim: true));
    }

    [Fact]
    public void AProcessedBodyNeverReentersThePrediction()
    {
        // After ReplacementFailed in prepare/create the dead game.LocalPlayer stays false even
        // though another driver (Redeploy in an Arena raid) would claim it again.
        Assert.False(Decide(isProcessedBody: true, isCurrentDeadLocalBody: true, aDriverWouldClaim: true));
        // Unclaimed at CG_Spawn (vanilla death, or a foreign CG_Spawn prefix took it): same.
        Assert.False(Decide(isProcessedBody: true));
    }

    [Fact]
    public void BodiesThatAreNotTheCurrentLocalBodyAreNeverPredicted()
    {
        // A retired swap body dying, a bot, an old corpse: not game.LocalPlayer (or alive).
        Assert.False(Decide(isCurrentDeadLocalBody: false, aDriverWouldClaim: true));
    }

    [Fact]
    public void NothingIsPendingWhenCoreCannotReplace()
    {
        // Raid ending, Core unavailable (patches incomplete, no driver, Fika), no game.
        Assert.False(Decide(coreCanReplace: false, isClaimedBody: true, claimantHolds: true));
        Assert.False(Decide(coreCanReplace: false, isCurrentDeadLocalBody: true, aDriverWouldClaim: true));
    }

    [Fact]
    public void NoBodyMeansNothingIsPending()
    {
        Assert.False(Decide(hasBody: false, dispatchingThisBody: true, isClaimedBody: true, claimantHolds: true));
    }

    [Fact]
    public void AClaimWinsOverTheProcessedMarkOfAnEarlierBody()
    {
        // Claimed and processed cannot both be true for the same body (Resolve clears the
        // claim), but the order of checks is part of the contract: claim first.
        Assert.True(Decide(isClaimedBody: true, claimantHolds: true, isProcessedBody: true));
    }
}

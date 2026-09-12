using Redeploy.Core.Engine;
using Xunit;

namespace Redeploy.Tests;

public class RespawnPreflightTests
{
    private static Exception Check(
        bool hasRequest = true, bool hasGame = true, bool hasSpawnSelector = true,
        bool engineAvailable = true, bool raidTearingDown = false,
        int respawnSignalDepth = 0, bool pipelineRunning = false)
        => RespawnPreflight.Check(hasRequest, hasGame, hasSpawnSelector, engineAvailable, raidTearingDown, respawnSignalDepth, pipelineRunning);

    [Fact]
    public void HealthyRequestPasses()
    {
        Assert.Null(Check());
    }

    [Fact]
    public void MissingRequestIsAnArgumentNull()
    {
        Assert.IsType<ArgumentNullException>(Check(hasRequest: false));
    }

    [Fact]
    public void MissingGameAndSelectorAreArgumentErrors()
    {
        Assert.IsType<ArgumentException>(Check(hasGame: false));
        Assert.IsType<ArgumentException>(Check(hasSpawnSelector: false));
    }

    [Fact]
    public void UnavailableEngineIsAnInvalidOperation()
    {
        Assert.IsType<InvalidOperationException>(Check(engineAvailable: false));
    }

    [Fact]
    public void EndingRaidIsACancellation()
    {
        Assert.IsType<OperationCanceledException>(Check(raidTearingDown: true));
    }

    [Fact]
    public void ReentrancyAndRunningPipelineAreInvalidOperations()
    {
        var nested = Assert.IsType<InvalidOperationException>(Check(respawnSignalDepth: 1));
        Assert.Contains("handler", nested.Message);
        Assert.IsType<InvalidOperationException>(Check(pipelineRunning: true));
    }

    [Fact]
    public void ArgumentsWinOverAvailabilityOverRaidStateOverReentrancy()
    {
        // The documented order: a caller sees the most fundamental refusal first.
        Assert.IsType<ArgumentNullException>(Check(hasRequest: false, engineAvailable: false, raidTearingDown: true, respawnSignalDepth: 1, pipelineRunning: true));
        Assert.IsType<InvalidOperationException>(Check(engineAvailable: false, raidTearingDown: true, respawnSignalDepth: 1));
        Assert.IsType<OperationCanceledException>(Check(raidTearingDown: true, respawnSignalDepth: 1, pipelineRunning: true));
        var reentrant = Assert.IsType<InvalidOperationException>(Check(respawnSignalDepth: 1, pipelineRunning: true));
        Assert.Contains("handler", reentrant.Message);
    }
}

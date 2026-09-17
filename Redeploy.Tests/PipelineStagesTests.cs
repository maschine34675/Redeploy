using Redeploy.Core.Engine;
using Xunit;

namespace Redeploy.Tests;

/// <summary>
/// The stage bookkeeping behind the "Respawn pipeline" log line: a step is the distance to the
/// previous mark, steps sum into their group, groups keep the order they ran in, and a collection
/// during a step is flagged on the step and its group.
/// </summary>
public class PipelineStagesTests
{
    private const long TicksPerSecond = 10_000;   // 10 ticks = 1 ms

    [Fact]
    public void StepsAreTheDistanceToThePreviousMarkAndSumIntoTheirGroup()
    {
        var stages = new PipelineStages(startTicks: 1_000, collections: 5);
        stages.Mark("teardown.owner", 1_028, 5);       // 2.8 ms
        stages.Mark("teardown.unregister", 1_030, 5);  // 0.2 ms
        stages.Mark("create", 7_932, 6);               // 690.2 ms, a collection ran
        stages.Mark("rewire.camera", 8_334, 6);        // 40.2 ms
        stages.Mark("rewire.ui", 8_335, 6);            // 0.1 ms

        Assert.Equal(7_335, stages.TotalTicks);
        Assert.Equal(
            "teardown 3.0 (owner 2.8, unregister 0.2) | create 690.2* | rewire 40.3 (camera 40.2, ui 0.1)",
            stages.Format(TicksPerSecond));
    }

    [Fact]
    public void AGroupSeenAgainLaterKeepsItsFirstPosition()
    {
        var stages = new PipelineStages(0, 0);
        stages.Mark("rewire.camera", 10, 0);
        stages.Mark("publish.replaced", 20, 0);
        stages.Mark("rewire.late", 30, 1);

        Assert.Equal("rewire 2.0* (camera 1.0, late 1.0*) | publish 1.0 (replaced 1.0)", stages.Format(TicksPerSecond));
    }

    [Fact]
    public void ABackwardClockNeverProducesNegativeTime()
    {
        var stages = new PipelineStages(100, 0);
        stages.Mark("a.one", 90, 0);
        stages.Mark("a.two", 110, 0);

        Assert.Equal("a 1.0 (one 0.0, two 1.0)", stages.Format(TicksPerSecond));
        Assert.Equal(10, stages.TotalTicks);
    }

    [Fact]
    public void ShortStepsLeaveTheDetailButNotTheGroupTotal()
    {
        var stages = new PipelineStages(0, 0);
        stages.Mark("rewire.dictionary", 0, 0);      // 0.0 ms: left out
        stages.Mark("rewire.camera", 400, 0);        // 40.0 ms
        stages.Mark("rewire.ui", 400, 1);            // 0.0 ms but a collection ran: kept
        stages.Mark("publish.claims", 400, 1);       // group with only short steps: total, no parentheses

        Assert.Equal("rewire 40.0* (camera 40.0, ui 0.0*) | publish 0.0", stages.Format(TicksPerSecond, minStepMilliseconds: 0.05));
    }

    [Fact]
    public void NothingMarkedIsAnEmptyLineAndAnUnknownClockSaysSo()
    {
        Assert.Equal("", new PipelineStages(0, 0).Format(TicksPerSecond));
        Assert.Equal("?", PipelineStages.Milliseconds(10, 0));
    }
}

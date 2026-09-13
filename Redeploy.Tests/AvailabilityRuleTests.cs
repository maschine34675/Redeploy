using Redeploy.Core.Engine;
using Xunit;

namespace Redeploy.Tests;

/// <summary>S0-12: the Fika probe is the last operand of IsAvailable, never the first.</summary>
public class AvailabilityRuleTests
{
    [Fact]
    public void AvailableOnlyWithCompletePatchesADriverAndNoFika()
    {
        Assert.True(AvailabilityRule.Evaluate(true, 1, () => false));
        Assert.False(AvailabilityRule.Evaluate(true, 1, () => true));
        Assert.False(AvailabilityRule.Evaluate(false, 1, () => false));
        Assert.False(AvailabilityRule.Evaluate(true, 0, () => false));
    }

    [Fact]
    public void TheFikaProbeIsNotAskedBeforeTheOtherGatesPass()
    {
        var probed = false;
        bool Probe() { probed = true; return false; }

        Assert.False(AvailabilityRule.Evaluate(false, 1, Probe));
        Assert.False(AvailabilityRule.Evaluate(true, 0, Probe));
        Assert.False(probed);

        Assert.True(AvailabilityRule.Evaluate(true, 1, Probe));
        Assert.True(probed);
    }
}

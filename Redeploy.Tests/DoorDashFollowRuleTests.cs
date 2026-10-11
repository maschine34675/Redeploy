using Redeploy.Core.Compat;
using Xunit;

namespace Redeploy.Tests;

/// <summary>
/// When the DoorDash breacher is moved to a replacement body (Compat/DoorDashCompatHelper.cs).
/// The part that touches Unity stays in-game; the decision is pinned here.
/// </summary>
public class DoorDashFollowRuleTests
{
    [Fact]
    public void AnInitialisedDoorDashIsFollowed()
        => Assert.Equal(DoorDashFollow.Follow, DoorDashFollowRule.Decide(true, true, true, true, true));

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, true, true, true)]     // leftovers of an unloaded plugin never count
    public void WithoutDoorDashNothingHappens(bool breacherFound, bool breacherIsBehaviour, bool hasSwitch, bool switchBound)
        => Assert.Equal(DoorDashFollow.NotInstalled,
            DoorDashFollowRule.Decide(false, breacherFound, breacherIsBehaviour, hasSwitch, switchBound));

    [Theory]
    [InlineData(false, false)]   // the component was renamed or removed
    [InlineData(true, false)]    // the name still exists, but is no longer a component
    public void ABreacherOfAnotherShapeIsNeverAttached(bool breacherFound, bool breacherIsBehaviour)
    {
        Assert.Equal(DoorDashFollow.UnknownShape, DoorDashFollowRule.Decide(true, breacherFound, breacherIsBehaviour, true, true));
        // The shape is asked first: an uninitialised DoorDash of another shape is still "unknown shape".
        Assert.Equal(DoorDashFollow.UnknownShape, DoorDashFollowRule.Decide(true, breacherFound, breacherIsBehaviour, true, false));
    }

    [Fact]
    public void ADoorDashThatNeverBoundItsSettingsIsLeftAlone()
        => Assert.Equal(DoorDashFollow.NotInitialised, DoorDashFollowRule.Decide(true, true, true, true, false));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]   // "bound" means nothing for a switch that does not exist
    public void AMissingSwitchDoesNotHoldTheBreacherBack(bool switchBound)
        => Assert.Equal(DoorDashFollow.Follow, DoorDashFollowRule.Decide(true, true, true, false, switchBound));
}

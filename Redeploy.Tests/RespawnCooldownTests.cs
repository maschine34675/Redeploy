using Redeploy.Services;
using Xunit;

namespace Redeploy.Tests;

/// <summary>
/// The respawn cooldown counts from the death. The complaint it answers: after watching a death
/// replay and then choosing Respawn, the camera sat on the corpse for the full cooldown again.
/// </summary>
public class RespawnCooldownTests
{
    [Theory]
    [InlineData(5f, 100f, 100f, 5f)]     // chosen at the moment of death: the whole cooldown
    [InlineData(5f, 100f, 103f, 2f)]     // three seconds already spent dead
    [InlineData(5f, 100f, 105f, 0f)]
    [InlineData(5f, 100f, 114f, 0f)]     // chosen after an 11 s death replay: right away
    [InlineData(0f, 100f, 100f, 0f)]     // cooldown off
    [InlineData(-3f, 100f, 100f, 0f)]    // a negative setting behaves like off, as before
    public void TheCooldownIsTheTimeLeftSinceTheDeath(float cooldown, float diedAt, float now, float expected)
        => Assert.Equal(expected, RespawnCooldown.Remaining(cooldown, diedAt, now), 3);

    [Theory]
    [InlineData(-1f, 50f)]               // no death recorded this raid
    [InlineData(float.NaN, 50f)]
    [InlineData(60f, 50f)]               // a death time after now: clock confusion
    public void AnUnknownDeathTimeKeepsTheFullCooldown(float diedAt, float now)
        => Assert.Equal(5f, RespawnCooldown.Remaining(5f, diedAt, now));

    [Fact]
    public void ABrokenSettingNeverShortensAnythingItShouldNot()
    {
        Assert.Equal(0f, RespawnCooldown.Remaining(float.NaN, 100f, 100f));
        Assert.Equal(float.MaxValue, RespawnCooldown.Remaining(float.PositiveInfinity, 100f, 1000f));
    }
}

using Redeploy.Core.Compat;
using Xunit;

namespace Redeploy.Tests;

/// <summary>
/// The stale PerfectCulling sampler check without a scene search: what an earlier sampler or the
/// static Instance leaves behind is destroyed, the starting sampler never, and a destroyed one is
/// forgotten. Stand-in samplers carry an "alive" flag instead of Unity's overloaded null.
/// </summary>
public class StaleSamplerRegistryTests
{
    private sealed class Sampler
    {
        public bool Alive = true;
        public readonly string Name;
        public Sampler(string name) => Name = name;
        public override string ToString() => Name;
    }

    private static StaleSamplerRegistry NewRegistry() => new(sampler => sampler is Sampler { Alive: true });

    [Fact]
    public void TheFirstSamplerOfARaidHasNothingStale()
    {
        var registry = NewRegistry();
        var raidStart = new Sampler("raid start");

        Assert.Empty(registry.TakeStale(raidStart, current: null));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void ARespawnCameraFindsTheSamplerItReplaces()
    {
        var registry = NewRegistry();
        var old = new Sampler("old camera");
        registry.TakeStale(old, null);

        var fresh = new Sampler("respawn camera");
        Assert.Equal(new object[] { old }, registry.TakeStale(fresh, current: old));
    }

    [Fact]
    public void TheStaticInstanceCountsEvenIfItsStartWasNeverSeen()
    {
        var registry = NewRegistry();
        var unseen = new Sampler("started before the patch");
        var fresh = new Sampler("fresh");

        Assert.Equal(new object[] { unseen }, registry.TakeStale(fresh, current: unseen));
    }

    [Fact]
    public void TheStartingSamplerIsNeverStaleEvenAsInstance()
    {
        var registry = NewRegistry();
        var fresh = new Sampler("fresh");
        registry.TakeStale(fresh, null);

        Assert.Empty(registry.TakeStale(fresh, current: fresh));
    }

    [Fact]
    public void ADestroyedSamplerIsForgottenAndOneThatSurvivesIsOfferedAgain()
    {
        var registry = NewRegistry();
        var first = new Sampler("first");
        var second = new Sampler("second");
        var third = new Sampler("third");
        registry.TakeStale(first, null);
        registry.TakeStale(second, first);          // first returned; pretend its destruction silently failed

        Assert.Equal(new object[] { first }, registry.TakeStale(third, current: second).Where(s => s == first));

        first.Alive = false;
        second.Alive = false;
        var fourth = new Sampler("fourth");
        Assert.Equal(new object[] { third }, registry.TakeStale(fourth, current: null));
        Assert.Equal(2, registry.Count);             // fourth, and third until it is found dead
    }

    [Fact]
    public void TheSameSamplerIsReturnedOnceEvenWhenItIsAlsoTheInstance()
    {
        var registry = NewRegistry();
        var old = new Sampler("old");
        registry.TakeStale(old, null);

        var stale = registry.TakeStale(new Sampler("new"), current: old);
        Assert.Single(stale);
    }

    [Fact]
    public void ALivenessCheckIsRequired()
        => Assert.Throws<ArgumentNullException>(() => new StaleSamplerRegistry(null));
}

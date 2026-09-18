using Redeploy.Core.Compat;
using Xunit;

namespace Redeploy.Tests;

/// <summary>
/// The pure array logic behind OcclusionCullingSwitcherCache: an empty or null array counts as
/// alive, a destroyed element makes the whole array stale, and filtering keeps only the elements a
/// predicate accepts without disturbing the source array. Stand-in elements carry plain flags
/// instead of Unity's overloaded null / GameObject.activeInHierarchy.
/// </summary>
public class ArrayLivenessFilterTests
{
    private sealed class Switcher
    {
        public bool Alive = true;
        public bool Active = true;
    }

    // ---------------------------------------------------------------- AllAlive

    [Fact]
    public void AnEmptyArrayCountsAsAlive()
        => Assert.True(ArrayLivenessFilter.AllAlive(new Switcher[0], s => s.Alive));

    [Fact]
    public void ANullArrayCountsAsAlive()
        => Assert.True(ArrayLivenessFilter.AllAlive<Switcher>(null, s => s.Alive));

    [Fact]
    public void AllAliveElementsAreAlive()
    {
        var array = new[] { new Switcher(), new Switcher(), new Switcher() };
        Assert.True(ArrayLivenessFilter.AllAlive(array, s => s.Alive));
    }

    [Fact]
    public void OneDestroyedElementMakesTheWholeArrayStale()
    {
        var array = new[] { new Switcher(), new Switcher { Alive = false }, new Switcher() };
        Assert.False(ArrayLivenessFilter.AllAlive(array, s => s.Alive));
    }

    [Fact]
    public void AllAliveRequiresTheLivenessTest()
        => Assert.Throws<System.ArgumentNullException>(() => ArrayLivenessFilter.AllAlive(new Switcher[0], null));

    // ---------------------------------------------------------------- Where

    [Fact]
    public void WhereKeepsOnlyTheElementsThePredicateAccepts()
    {
        var active1 = new Switcher();
        var inactive = new Switcher { Active = false };
        var active2 = new Switcher();
        var array = new[] { active1, inactive, active2 };

        var result = ArrayLivenessFilter.Where(array, s => s.Active);

        Assert.Equal(new[] { active1, active2 }, result);
    }

    [Fact]
    public void WherePreservesOrder()
    {
        var a = new Switcher();
        var b = new Switcher();
        var c = new Switcher();
        var result = ArrayLivenessFilter.Where(new[] { a, b, c }, _ => true);

        Assert.Equal(new[] { a, b, c }, result);
    }

    [Fact]
    public void WhereReturnsAFreshArrayNotTheSourceArray()
    {
        var array = new[] { new Switcher() };
        var result = ArrayLivenessFilter.Where(array, _ => true);

        Assert.Equal(array, result);
        Assert.NotSame(array, result);
    }

    [Fact]
    public void WhereOnANullArrayReturnsAnEmptyArrayNotNull()
    {
        var result = ArrayLivenessFilter.Where<Switcher>(null, _ => true);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void WhereOnAnEmptyArrayReturnsEmpty()
        => Assert.Empty(ArrayLivenessFilter.Where(new Switcher[0], _ => true));

    [Fact]
    public void WhereRequiresTheKeepPredicate()
        => Assert.Throws<System.ArgumentNullException>(() => ArrayLivenessFilter.Where(new Switcher[0], null));
}

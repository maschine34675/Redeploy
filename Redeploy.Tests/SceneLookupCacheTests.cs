using Redeploy.Core.Compat;
using Xunit;

namespace Redeploy.Tests;

/// <summary>
/// The per-raid scene-search cache without a scene search: what one key found stays remembered
/// (a hit or a null result) until either the key changes or the remembered object dies, a fresh
/// key always re-searches, and a null key (no raid yet) is never remembered at all. Stand-in
/// results carry an "alive" flag instead of Unity's overloaded null.
/// </summary>
public class SceneLookupCacheTests
{
    private sealed class Found
    {
        public bool Alive = true;
    }

    [Fact]
    public void TheFirstRequestForAKeyRunsTheSearchAndRemembersIt()
    {
        var calls = 0;
        var found = new Found();
        var cache = new SceneLookupCache(search: () => { calls++; return found; }, isAlive: c => c is Found { Alive: true });
        var key = new object();

        var result = cache.Resolve(key);

        Assert.Same(found, result);
        Assert.Equal(1, calls);
        Assert.False(cache.LastResolveWasCacheHit);
    }

    [Fact]
    public void ANullResultIsRememberedTooAndNotSearchedAgain()
    {
        var calls = 0;
        var cache = new SceneLookupCache(search: () => { calls++; return null; }, isAlive: c => c is Found { Alive: true });
        var key = new object();

        var first = cache.Resolve(key);
        Assert.False(cache.LastResolveWasCacheHit); // first request: ran the search

        var second = cache.Resolve(key);
        Assert.True(cache.LastResolveWasCacheHit); // later requests: served the remembered null

        var third = cache.Resolve(key);
        Assert.True(cache.LastResolveWasCacheHit);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Null(third);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void TheSameKeyReusesAResultThatIsStillAlive()
    {
        var calls = 0;
        var found = new Found();
        var cache = new SceneLookupCache(search: () => { calls++; return found; }, isAlive: c => c is Found { Alive: true });
        var key = new object();

        cache.Resolve(key);
        var second = cache.Resolve(key);

        Assert.Same(found, second);
        Assert.Equal(1, calls);
        Assert.True(cache.LastResolveWasCacheHit);
    }

    [Fact]
    public void ADestroyedRememberedResultIsSearchedAgain()
    {
        var calls = 0;
        var first = new Found();
        var second = new Found();
        var results = new System.Collections.Generic.Queue<Found>(new[] { first, second });
        var cache = new SceneLookupCache(search: () => { calls++; return results.Dequeue(); }, isAlive: c => c is Found { Alive: true });
        var key = new object();

        var resolved1 = cache.Resolve(key);
        Assert.Same(first, resolved1);

        first.Alive = false;
        var resolved2 = cache.Resolve(key);

        Assert.Same(second, resolved2);
        Assert.Equal(2, calls);
        Assert.False(cache.LastResolveWasCacheHit);
    }

    [Fact]
    public void ANewKeySearchesAgainEvenIfTheOldResultIsStillAlive()
    {
        var calls = 0;
        var first = new Found();
        var second = new Found();
        var results = new System.Collections.Generic.Queue<Found>(new[] { first, second });
        var cache = new SceneLookupCache(search: () => { calls++; return results.Dequeue(); }, isAlive: c => c is Found { Alive: true });

        var resolvedForA = cache.Resolve(new object());
        var resolvedForB = cache.Resolve(new object());

        Assert.Same(first, resolvedForA);
        Assert.Same(second, resolvedForB);
        Assert.Equal(2, calls);
        Assert.False(cache.LastResolveWasCacheHit);
        Assert.True(first.Alive); // the old result was not consulted for liveness at all
    }

    [Fact]
    public void ANullKeyAlwaysSearchesAndNeverCaches()
    {
        var calls = 0;
        var found = new Found();
        var cache = new SceneLookupCache(search: () => { calls++; return found; }, isAlive: c => c is Found { Alive: true });

        cache.Resolve(null);
        cache.Resolve(null);
        cache.Resolve(null);

        Assert.Equal(3, calls);
        Assert.False(cache.LastResolveWasCacheHit);
    }

    [Fact]
    public void ANullKeyRequestDoesNotDisturbAnExistingEntryForARealKey()
    {
        var calls = 0;
        var found = new Found();
        var cache = new SceneLookupCache(search: () => { calls++; return found; }, isAlive: c => c is Found { Alive: true });
        var key = new object();

        cache.Resolve(key);          // remembered for `key`
        cache.Resolve(null);         // must not overwrite or clear the entry
        var again = cache.Resolve(key);

        Assert.Same(found, again);
        Assert.Equal(2, calls);      // one for `key`, one for the null-key call — not a third
        Assert.True(cache.LastResolveWasCacheHit);
    }

    [Fact]
    public void ALivenessCheckIsRequired()
        => Assert.Throws<System.ArgumentNullException>(() => new SceneLookupCache(search: () => null, isAlive: null));

    [Fact]
    public void ASearchDelegateIsRequired()
        => Assert.Throws<System.ArgumentNullException>(() => new SceneLookupCache(search: null, isAlive: c => true));
}

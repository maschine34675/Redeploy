using System;

namespace Redeploy.Core.Compat;

/// <summary>
/// Caches the result of a one-shot scene search (typically UnityEngine.Object.FindObjectOfType)
/// per raid, so a respawn does not repeat it. Measured 18.09.2026 (GPU raid, Labyrinth deathmatch,
/// RespawnLeakProbe): PerfectCullingCrossSceneSampler.method_0 walks every MonoBehaviour including
/// inactive pooled ones on every respawn's camera recreation, 72 ms at raid start growing past
/// 110 ms — see docs/RESPAWN-API.md, "18.09.2026" and Compat/PerfectCullingFix.cs.
///
/// <see cref="Resolve"/> is keyed on an object that identifies the current raid/scene (Core passes
/// the GameWorld instance): the first request for a key runs the search and remembers the result,
/// including a null one. A search that legitimately finds nothing — baked scene content some maps
/// do not have, e.g. Labyrinth's missing CullingGridPreProcess — must not be repeated every
/// respawn either, or the miss costs the same full scene walk as a hit would. A later request for
/// the same key returns the remembered result as long as it is null or still alive; once it is
/// found destroyed (a new key never reuses a dead object, but a caller could still be holding one
/// via a stale reference of its own) the cache re-searches once and remembers whatever comes back,
/// including null again. A different key means a new raid started, so the cache always re-searches
/// rather than serving the previous raid's answer. A null key means no raid is running yet, so
/// nothing is remembered and every call searches: caching against "no raid" would leak whichever
/// raid asked last into the next one before it even has a key.
///
/// Pure: the search and the liveness test are both delegates (Unity's overloaded null in the
/// game), so Redeploy.Tests links this file, like StaleSamplerRegistry.
/// </summary>
internal sealed class SceneLookupCache
{
    private readonly Func<object> _search;
    private readonly Func<object, bool> _isAlive;

    private object _key;
    private object _result;
    private bool _hasEntry;

    public SceneLookupCache(Func<object> search, Func<object, bool> isAlive)
    {
        _search = search ?? throw new ArgumentNullException(nameof(search));
        _isAlive = isAlive ?? throw new ArgumentNullException(nameof(isAlive));
    }

    /// <summary>
    /// True after the most recent <see cref="Resolve"/> call reused a remembered result instead
    /// of running the search — the one thing worth a log line, and only the first time it happens.
    /// </summary>
    public bool LastResolveWasCacheHit { get; private set; }

    /// <summary>
    /// Returns the search result for <paramref name="key"/>, from the cache when possible. See
    /// the type doc comment for the branch rules; <see cref="LastResolveWasCacheHit"/> reflects
    /// which branch this call took.
    /// </summary>
    public object Resolve(object key)
    {
        if (key == null)
        {
            LastResolveWasCacheHit = false;
            return _search();
        }

        if (_hasEntry && ReferenceEquals(_key, key) && (_result == null || _isAlive(_result)))
        {
            LastResolveWasCacheHit = true;
            return _result;
        }

        LastResolveWasCacheHit = false;
        _key = key;
        _result = _search();
        _hasEntry = true;
        return _result;
    }
}

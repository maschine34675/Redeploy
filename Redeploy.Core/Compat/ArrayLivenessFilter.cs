using System;
using System.Collections.Generic;

namespace Redeploy.Core.Compat;

/// <summary>
/// The pure array logic behind OcclusionCullingSwitcherCache: whether a remembered array is still
/// safe to reuse, and narrowing an array down to the elements a predicate keeps.
///
/// <see cref="AllAlive{T}"/> backs the cache's re-search trigger. An empty array counts as alive —
/// nothing in it can have been destroyed — the same rule SceneLookupCache already applies to a
/// remembered null (see its doc comment): caching "nothing found" must not cost a fresh scan every
/// respawn either, or the miss is as expensive as a hit.
///
/// <see cref="Where{T}"/> backs the per-call refilter. UnityEngine.Object.FindObjectsOfType&lt;T&gt;()
/// excludes inactive objects; a cached array can go stale that way without anything in it dying
/// (SetActive(false) does not destroy), so OcclusionCullingSwitcherCache re-applies an
/// activeInHierarchy check to the cached array on every answer instead of baking it into what gets
/// remembered — see that class's doc comment for why the two must stay separate.
///
/// Pure: both the liveness test and the keep predicate are injected (same pattern as
/// StaleSamplerRegistry and SceneLookupCache), so Redeploy.Tests links this file and drives it with
/// synthetic elements instead of Unity objects.
/// </summary>
internal static class ArrayLivenessFilter
{
    public static bool AllAlive<T>(T[] array, Func<T, bool> isAlive)
    {
        if (isAlive == null)
            throw new ArgumentNullException(nameof(isAlive));

        if (array == null)
            return true;

        foreach (var element in array)
        {
            if (!isAlive(element))
                return false;
        }

        return true;
    }

    public static T[] Where<T>(T[] array, Func<T, bool> keep)
    {
        if (keep == null)
            throw new ArgumentNullException(nameof(keep));

        if (array == null || array.Length == 0)
            return array ?? Array.Empty<T>();

        var kept = new List<T>(array.Length);
        foreach (var element in array)
        {
            if (keep(element))
                kept.Add(element);
        }

        return kept.ToArray();
    }
}

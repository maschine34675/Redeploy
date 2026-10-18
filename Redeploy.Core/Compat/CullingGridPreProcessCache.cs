using Comfort.Common;
using EFT;
using System;

namespace Redeploy.Core.Compat;

/// <summary>
/// Backs the transpiler in <see cref="PerfectCullingFix"/> that removes the per-respawn scene
/// search inside PerfectCullingCrossSceneSampler.method_0. Measured 18.09.2026 (GPU raid,
/// Labyrinth deathmatch, RespawnLeakProbe): every respawn rebinds the camera, which creates a new
/// sampler, whose Start -> method_0 runs
/// <c>UnityEngine.Object.FindObjectOfType&lt;CullingGridPreProcess&gt;()</c> — a full scene walk
/// over every MonoBehaviour including inactive pooled ones (263,580 on that raid, 32,023 active
/// and growing with every player body), 72 ms at raid start growing past 110 ms a few minutes in.
/// It returns null on Labyrinth: the map has no CullingGridPreProcess. CullingGridPreProcess is
/// baked scene content — present or absent from scene load on, never appearing mid-raid — so one
/// search per raid is correct, and a null result is exactly as cacheable as a hit
/// (<see cref="SceneLookupCache"/> remembers it either way).
///
/// This also helps any other mod that recreates the sampler, e.g. KillcamReplay's replay camera:
/// the cache is keyed on the raid (the GameWorld instance), not on which caller triggered the
/// search, so whichever mod asks first pays for the walk and every later respawn in the same raid,
/// from any consumer, reuses its answer as long as this Core is installed.
/// </summary>
internal static class CullingGridPreProcessCache
{
    private static readonly SceneLookupCache Cache = new SceneLookupCache(Search, IsAlive);
    private static bool _loggedCacheHit;

    /// <summary>CullingGridPreProcess, resolved by name once in <see cref="PerfectCullingFix"/>.
    /// Null means the transpiler was never wired (type missing), so <see cref="Resolve"/> also
    /// falls back to nothing rather than crash.</summary>
    internal static Type SearchType;

    /// <summary>
    /// The method the transpiler calls in place of
    /// <c>UnityEngine.Object.FindObjectOfType&lt;CullingGridPreProcess&gt;()</c>. Same
    /// zero-argument, one-return-value shape, so the transpiler only has to add a castclass
    /// after the call — see <see cref="FindObjectOfTypeRewrite"/>. Public despite the internal
    /// class: Harmony's patched IL calls across assemblies (same pattern as QuietBrainLog's
    /// BrainLogRelay.Emit).
    /// </summary>
    public static UnityEngine.Object Resolve()
    {
        try
        {
            var key = Singleton<GameWorld>.Instantiated ? (object)Singleton<GameWorld>.Instance : null;
            var result = (UnityEngine.Object)Cache.Resolve(key);

            if (Cache.LastResolveWasCacheHit && !_loggedCacheHit)
            {
                _loggedCacheHit = true;
                CoreLog.LogDebug(
                    "CullingGridPreProcess scene search reused from cache instead of searching the "
                    + $"scene again (cached result: {(result == null ? "null" : "found")}).");
            }

            return result;
        }
        catch (Exception ex)
        {
            // The cache must never cost the sampler its culling: fall back to exactly what
            // method_0 used to do, uncached.
            CoreLog.LogWarning($"CullingGridPreProcess scene-search cache failed, searching directly: {ex.Message}");
            return Search() as UnityEngine.Object;
        }
    }

    private static object Search()
        => SearchType != null ? UnityEngine.Object.FindObjectOfType(SearchType) : null;

    private static bool IsAlive(object candidate)
        => candidate is UnityEngine.Object unity && unity != null;
}

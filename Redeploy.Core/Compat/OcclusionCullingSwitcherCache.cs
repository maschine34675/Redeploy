using Comfort.Common;
using EFT;
using System;

namespace Redeploy.Core.Compat;

/// <summary>
/// Backs the transpiler in <see cref="OcclusionCullingFix"/> that removes the per-respawn
/// OcclusionCullingSwitcher scene search inside CameraManager.UpdateUseOcclusionCulling. Measured
/// 18.09.2026 (GPU raid, RespawnLeakProbe 0.1.3, Core 1.0.5): with the CullingGridPreProcess search
/// already cached (Core 1.0.5), CameraManager.method_2 -&gt; UpdateUseOcclusionCulling is the
/// camera-rebind step's remaining growth — 82-86 ms per respawn and growing per created body —
/// because <c>FindObjectsProxy.FindUnityObjectsOfType&lt;OcclusionCullingSwitcher&gt;()</c> walks
/// every MonoBehaviour including inactive pooled ones, the same shape as the search Core 1.0.5 cut
/// (docs/RESPAWN-API.md, "18.09.2026"). Vanilla only pays for this once, at the camera's single
/// per-raid creation; Redeploy.Core recreates the camera on every respawn, so it pays every time.
///
/// OcclusionCullingSwitcher (EFT.CameraControl, a DisablerCullingObjectBase) is baked scene trigger
/// content: present from scene load like CullingGridPreProcess, never created mid-raid — so one
/// search per raid is correct, and <see cref="CullingGridPreProcessCache"/>'s "cache the miss too"
/// rule (via the same <see cref="SceneLookupCache"/>) applies here the same way.
///
/// Calls UnityEngine.Object.FindObjectsOfType&lt;OcclusionCullingSwitcher&gt;() directly rather
/// than through FindObjectsProxy.FindUnityObjectsOfType&lt;T&gt;(): the proxy only wraps that exact
/// call with a string.Format(...) whose result it never logs or uses
/// (_decompiled/FindObjectsProxy.cs) — same scene walk, same result, minus a discarded
/// StackFrame/string.Format allocation. That allocation only happens once per raid either way, so
/// this is not the fix; it just means the cache does not need to resolve or depend on the proxy
/// wrapper to get vanilla's exact result.
///
/// Unlike CullingGridPreProcessCache's target, the remembered result can go stale without any
/// element being destroyed: a switcher can be deactivated (SetActive(false)) without dying, and
/// FindObjectsOfType&lt;T&gt;() excludes inactive objects by default, so a fresh search would stop
/// returning it (and would start returning one reactivated since). The cached search therefore
/// includes inactive switchers (review, 18.09.: caching the default search would lose a switcher
/// that was inactive at that moment for the rest of the raid); every <see cref="Resolve"/> call
/// re-filters that array down to
/// gameObject.activeInHierarchy before handing it back (<see cref="ArrayLivenessFilter.Where{T}"/>),
/// so CameraManager.UpdateUseOcclusionCulling's <c>array.Any(s =&gt; s.HasEntered)</c> always sees
/// the same set a fresh search would have produced — never a deactivated switcher's stale
/// HasEntered, and never one that only became active since the cached search ran.
/// </summary>
internal static class OcclusionCullingSwitcherCache
{
    private static readonly SceneLookupCache Cache =
        new SceneLookupCache(Search, candidate => ArrayLivenessFilter.AllAlive((OcclusionCullingSwitcher[])candidate, IsAlive));

    private static bool _loggedCacheHit;

    /// <summary>
    /// The method the transpiler calls in place of
    /// <c>FindObjectsProxy.FindUnityObjectsOfType&lt;OcclusionCullingSwitcher&gt;()</c>. Same
    /// return type as that call (OcclusionCullingSwitcher[]), so the transpiler needs no castclass
    /// after it — see <see cref="GenericStaticCallRewrite"/>. Public despite the internal class:
    /// Harmony's patched IL calls across assemblies (same pattern as
    /// CullingGridPreProcessCache.Resolve and QuietBrainLog's BrainLogRelay.Emit).
    /// </summary>
    public static OcclusionCullingSwitcher[] Resolve()
    {
        try
        {
            var key = Singleton<GameWorld>.Instantiated ? (object)Singleton<GameWorld>.Instance : null;
            var raw = (OcclusionCullingSwitcher[])Cache.Resolve(key);
            var result = ArrayLivenessFilter.Where(raw, IsActive);

            if (Cache.LastResolveWasCacheHit && !_loggedCacheHit)
            {
                _loggedCacheHit = true;
                CoreLog.LogInfo(
                    "OcclusionCullingSwitcher scene search reused from cache instead of searching the "
                    + $"scene again ({result.Length} switcher(s)).");
            }

            return result;
        }
        catch (Exception ex)
        {
            // The cache must never cost the camera its occlusion-culling update: fall back to
            // exactly what UpdateUseOcclusionCulling used to call, uncached.
            CoreLog.LogWarning($"OcclusionCullingSwitcher scene-search cache failed, searching directly: {ex.Message}");
            return SearchDirect();
        }
    }

    // Includes inactive switchers on purpose: Resolve filters to activeInHierarchy on every call,
    // so a switcher that is inactive now and activated later is still found, exactly as a fresh
    // FindObjectsOfType (which only skips objects inactive at that moment) would find it.
    private static object Search() => UnityEngine.Object.FindObjectsOfType<OcclusionCullingSwitcher>(true);

    private static OcclusionCullingSwitcher[] SearchDirect() => UnityEngine.Object.FindObjectsOfType<OcclusionCullingSwitcher>();

    private static bool IsAlive(OcclusionCullingSwitcher candidate) => candidate != null;

    private static bool IsActive(OcclusionCullingSwitcher candidate)
        => candidate != null && candidate.gameObject != null && candidate.gameObject.activeInHierarchy;
}

using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Redeploy.Core.Compat;

// The PlayerCameraController Destroy/Create cycle (the respawn path) can leave a stale
// PerfectCulling sampler behind; a second sampler starting then throws GException8
// ("Autocull sampler exists") during Stop() cleanup, which breaks the teardown and leaves
// the next raid with a half-culled map. DestroyImmediate the stale ones before the new
// sampler initializes. Idempotent alongside the same patch in Killcam/KillcamReplay
// (the prefix only destroys stale duplicates, so double-patching is harmless).
//
// The stale ones come from a registry of samplers seen starting plus the sampler's static
// Instance, not from FindObjectsOfType: that walked the whole scene on every respawn and grew
// with every corpse a long raid keeps (docs/RESPAWN-API.md, "Messraid 17.09.2026").
//
// method_0 (called from Start, right before the stale-sampler check above runs) does its own
// scene search when the map has autoculling: UnityEngine.Object.FindObjectOfType<CullingGridPreProcess>().
// Measured 18.09.2026: that one call is all of the per-body growth in the camera-recreation step
// of a respawn (docs/RESPAWN-API.md, "18.09.2026"). RedeployCorePerfectCullingSceneSearchPatch below replaces it with a
// per-raid cache (CullingGridPreProcessCache / SceneLookupCache) the same way RedeployCorePerfectCullingSamplerPatch above
// replaced the stale-sampler scene search with a registry — wired here so it is active whenever
// this fix is, never separately.
public static class PerfectCullingFix
{
    private static bool _enabled;

    public static void EnableOnce()
    {
        if (_enabled)
            return;
        _enabled = true;

        // Unrelated game system (vanilla CameraManager, not the third-party PerfectCulling
        // sampler this class is named for) but the same cause (respawn recreating the camera) and
        // the same driver Awake call, so it is enabled from here too — see
        // Compat/OcclusionCullingFix.cs. Deliberately BEFORE the PerfectCulling type resolution
        // below: CameraManager exists regardless of whether that third-party plugin is installed,
        // so this must not share its early return when PerfectCulling is absent.
        OcclusionCullingFix.EnableOnce();

        RedeployCorePerfectCullingSamplerPatch.SamplerType = AccessTools.TypeByName("Koenigz.PerfectCulling.EFT.PerfectCullingCrossSceneSampler");
        if (RedeployCorePerfectCullingSamplerPatch.SamplerType == null)
        {
            CoreLog.LogWarning("PerfectCullingCrossSceneSampler type not found — sampler patch skipped");
            return;
        }

        RedeployCorePerfectCullingSamplerPatch.InstanceProperty = AccessTools.Property(RedeployCorePerfectCullingSamplerPatch.SamplerType, "Instance");
        if (RedeployCorePerfectCullingSamplerPatch.InstanceProperty == null)
            CoreLog.LogWarning("PerfectCullingCrossSceneSampler.Instance not found — only samplers seen starting are checked");

        new RedeployCorePerfectCullingSamplerPatch().Enable();

        EnableSceneSearchCacheOnce();
    }

    /// <summary>
    /// Resolves method_0 and CullingGridPreProcess by name and, if both exist, patches the scene
    /// search out of method_0. Missing either one just skips this half of the fix (logged once):
    /// the sampler still gets its stale-instance cleanup from RedeployCorePerfectCullingSamplerPatch, only the scene-search
    /// caching is lost, same fail-open shape as the rest of this class.
    /// </summary>
    private static void EnableSceneSearchCacheOnce()
    {
        var method0 = AccessTools.Method(RedeployCorePerfectCullingSamplerPatch.SamplerType, "method_0");
        if (method0 == null)
        {
            CoreLog.LogWarning("PerfectCullingCrossSceneSampler.method_0 not found — scene-search cache skipped, "
                + "the per-respawn scene search stays.");
            return;
        }

        var cullingGridPreProcessType = AccessTools.TypeByName("Koenigz.PerfectCulling.EFT.CullingGridPreProcess");
        if (cullingGridPreProcessType == null)
        {
            CoreLog.LogWarning("CullingGridPreProcess type not found — scene-search cache skipped, "
                + "the per-respawn scene search stays.");
            return;
        }

        CullingGridPreProcessCache.SearchType = cullingGridPreProcessType;
        RedeployCorePerfectCullingSceneSearchPatch.Method0 = method0;

        try
        {
            new RedeployCorePerfectCullingSceneSearchPatch().Enable();
        }
        catch (Exception ex)
        {
            // Same rule as everywhere else in this file: losing the fast path must never cost
            // the sampler its Start, so a failed patch just leaves the scene search in place.
            CoreLog.LogWarning($"Scene-search cache transpiler failed to enable, the per-respawn scene search stays: {ex.Message}");
        }
    }

    private sealed class RedeployCorePerfectCullingSamplerPatch : ModulePatch
    {
        internal static Type SamplerType;
        internal static PropertyInfo InstanceProperty;

        private static readonly StaleSamplerRegistry Started =
            new StaleSamplerRegistry(sampler => sampler is UnityEngine.Object unity && unity != null);

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(SamplerType, "Start");
        }

        [PatchPrefix]
        private static void Prefix(UnityEngine.Object __instance)
        {
            if (SamplerType == null)
                return;

            try
            {
                var current = InstanceProperty?.GetValue(null, null);
                foreach (UnityEngine.Object stale in Started.TakeStale(__instance, current))
                {
                    CoreLog.LogDebug($"Destroying stale PerfectCulling sampler '{stale.name}'.");
                    UnityEngine.Object.DestroyImmediate(stale);
                }
            }
            catch (Exception ex)
            {
                // Never cost the sampler its Start: without it the map is not culled at all.
                CoreLog.LogWarning($"Stale PerfectCulling sampler check failed: {ex.Message}");
            }
        }
    }

    private sealed class RedeployCorePerfectCullingSceneSearchPatch : ModulePatch
    {
        internal static MethodBase Method0;

        private static readonly MethodInfo ResolveMethod =
            AccessTools.Method(typeof(CullingGridPreProcessCache), nameof(CullingGridPreProcessCache.Resolve));

        protected override MethodBase GetTargetMethod()
        {
            return Method0;
        }

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            try
            {
                if (!FindObjectOfTypeRewrite.TryReplace(codes, CullingGridPreProcessCache.SearchType, ResolveMethod))
                {
                    CoreLog.LogWarning("Expected exactly 1 FindObjectOfType<CullingGridPreProcess>() call in "
                        + "PerfectCullingCrossSceneSampler.method_0 — found a different number, instructions left "
                        + "unchanged, the per-respawn scene search stays.");
                }
            }
            catch (Exception ex)
            {
                // A transpiler must never throw: that would break patching method_0 at all,
                // i.e. cost the sampler its Start. Hand back the instructions untouched instead.
                CoreLog.LogWarning($"Scene-search cache transpiler failed, left unchanged: {ex.Message}");
            }

            return codes;
        }
    }
}

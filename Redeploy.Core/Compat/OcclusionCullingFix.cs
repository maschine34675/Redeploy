using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Redeploy.Core.Compat;

/// <summary>
/// Removes the per-respawn OcclusionCullingSwitcher scene search from
/// CameraManager.UpdateUseOcclusionCulling — the camera-rebind step's remaining growth after Core
/// 1.0.5's CullingGridPreProcess cache (docs/RESPAWN-API.md, "18.09.2026", Core 1.0.6 entry).
/// Vanilla calls UpdateUseOcclusionCulling once per raid, at the camera's single creation; a
/// respawn recreates the camera (CameraRebindHelper.RebindToPlayer -&gt; PlayerCameraController.Create
/// -&gt; CameraManager.method_2 -&gt; UpdateUseOcclusionCulling), so this Core pays for the search every
/// respawn. Same design as PerfectCullingFix's scene-search cache: a per-raid cache
/// (<see cref="OcclusionCullingSwitcherCache"/>) behind a pure transpiler
/// (<see cref="GenericStaticCallRewrite"/>), resolved defensively, never throws, and falls back to
/// the original search on any exception.
///
/// CameraManager is a core game type (Assembly-CSharp), always present, so it is referenced by
/// <c>typeof</c> rather than resolved by name (compare PerfectCullingFix, which resolves the
/// third-party PerfectCulling sampler type by name because that plugin may not be installed). The
/// method itself is still resolved defensively: a future EFT build could rename or remove it, and
/// losing this fast path must never cost the camera its occlusion-culling update.
///
/// Enabled from <see cref="PerfectCullingFix.EnableOnce"/>, the same place PerfectCullingFix's own
/// patches are enabled — this fix targets a different game system (vanilla's CameraManager, not the
/// third-party PerfectCulling sampler), but is triggered by the same cause (respawn recreating the
/// camera) and follows the same driver Awake call.
/// </summary>
internal static class OcclusionCullingFix
{
    private static bool _enabled;

    public static void EnableOnce()
    {
        if (_enabled)
            return;
        _enabled = true;

        var target = AccessTools.Method(typeof(CameraManager), "UpdateUseOcclusionCulling");
        if (target == null)
        {
            CoreLog.LogWarning("CameraManager.UpdateUseOcclusionCulling not found — occlusion-culling scene-search cache skipped.");
            return;
        }

        RedeployCoreOcclusionCullingPatch.Target = target;

        try
        {
            new RedeployCoreOcclusionCullingPatch().Enable();
        }
        catch (Exception ex)
        {
            // Same rule as PerfectCullingFix's scene-search patch: losing the fast path must never
            // cost the camera its occlusion-culling update, so a failed patch just leaves the
            // original scene search in place.
            CoreLog.LogWarning($"Occlusion-culling scene-search cache transpiler failed to enable, the per-respawn scene search stays: {ex.Message}");
        }
    }

    private sealed class RedeployCoreOcclusionCullingPatch : ModulePatch
    {
        internal static MethodBase Target;

        private static readonly MethodInfo ResolveMethod =
            AccessTools.Method(typeof(OcclusionCullingSwitcherCache), nameof(OcclusionCullingSwitcherCache.Resolve));

        protected override MethodBase GetTargetMethod() => Target;

        [PatchTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);

            try
            {
                // castTo: null — OcclusionCullingSwitcherCache.Resolve() already returns
                // OcclusionCullingSwitcher[], exactly what FindUnityObjectsOfType<OcclusionCullingSwitcher>()
                // pushed, so nothing needs converting (compare RedeployCorePerfectCullingSceneSearchPatch,
                // whose replacement returns UnityEngine.Object and does need the cast).
                if (!GenericStaticCallRewrite.TryReplace(
                        codes, typeof(FindObjectsProxy), "FindUnityObjectsOfType", typeof(OcclusionCullingSwitcher), ResolveMethod, castTo: null))
                {
                    CoreLog.LogWarning("Expected exactly 1 FindObjectsProxy.FindUnityObjectsOfType<OcclusionCullingSwitcher>() call in "
                        + "CameraManager.UpdateUseOcclusionCulling — found a different number, instructions left unchanged, "
                        + "the per-respawn scene search stays.");
                }
            }
            catch (Exception ex)
            {
                // A transpiler must never throw: that would break patching UpdateUseOcclusionCulling
                // at all, i.e. cost the camera its occlusion-culling update entirely.
                CoreLog.LogWarning($"Occlusion-culling scene-search cache transpiler failed, left unchanged: {ex.Message}");
            }

            return codes;
        }
    }
}

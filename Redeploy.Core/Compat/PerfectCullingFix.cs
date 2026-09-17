using HarmonyLib;
using SPT.Reflection.Patching;
using System;
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
public static class PerfectCullingFix
{
    private static bool _enabled;

    public static void EnableOnce()
    {
        if (_enabled)
            return;
        _enabled = true;

        SamplerPatch.SamplerType = AccessTools.TypeByName("Koenigz.PerfectCulling.EFT.PerfectCullingCrossSceneSampler");
        if (SamplerPatch.SamplerType == null)
        {
            CoreLog.LogWarning("PerfectCullingCrossSceneSampler type not found — sampler patch skipped");
            return;
        }

        SamplerPatch.InstanceProperty = AccessTools.Property(SamplerPatch.SamplerType, "Instance");
        if (SamplerPatch.InstanceProperty == null)
            CoreLog.LogWarning("PerfectCullingCrossSceneSampler.Instance not found — only samplers seen starting are checked");

        new SamplerPatch().Enable();
    }

    private sealed class SamplerPatch : ModulePatch
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
                    CoreLog.LogWarning($"Destroying stale PerfectCulling sampler '{stale.name}'.");
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
}

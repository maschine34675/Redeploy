using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace Redeploy.Core.Compat;

// The PlayerCameraController Destroy/Create cycle (the respawn path) can leave a stale
// PerfectCulling sampler behind; a second sampler starting then throws GException8
// ("Autocull sampler exists") during Stop() cleanup, which breaks the teardown and leaves
// the next raid with a half-culled map. DestroyImmediate the stale ones before the new
// sampler initializes. Idempotent alongside the same patch in Killcam/KillcamReplay
// (the prefix only destroys stale duplicates, so double-patching is harmless).
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

        new SamplerPatch().Enable();
    }

    private sealed class SamplerPatch : ModulePatch
    {
        internal static System.Type SamplerType;

        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(SamplerType, "Start");
        }

        [PatchPrefix]
        private static void Prefix(Object __instance)
        {
            if (SamplerType == null)
                return;

            var existing = Object.FindObjectsOfType(SamplerType);
            foreach (var obj in existing)
            {
                if (obj != __instance)
                {
                    CoreLog.LogWarning($"Destroying stale PerfectCulling sampler '{obj.name}'.");
                    Object.DestroyImmediate(obj);
                }
            }
        }
    }
}

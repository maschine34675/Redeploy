using HarmonyLib;
using SPT.Reflection.Patching;
using System.Reflection;
using UnityEngine;

namespace CorpseRun.Patches;

// The PlayerCameraController Destroy/Create cycle (our respawn path) can leave a stale
// PerfectCulling sampler behind; a second sampler starting then throws GException8
// ("Autocull sampler exists") during Stop() cleanup, which breaks the teardown and leaves
// the next raid with a half-culled map. DestroyImmediate the stale ones before the new
// sampler initializes. Idempotent alongside the same patch in Killcam/KillcamReplay.
internal sealed class FixPerfectCullingSamplerPatch : ModulePatch
{
    private static System.Type _samplerType;

    public static void EnableIfPresent()
    {
        _samplerType = AccessTools.TypeByName("Koenigz.PerfectCulling.EFT.PerfectCullingCrossSceneSampler");
        if (_samplerType == null)
        {
            Plugin.ModLogger.LogWarning("PerfectCullingCrossSceneSampler type not found — sampler patch skipped");
            return;
        }

        new FixPerfectCullingSamplerPatch().Enable();
    }

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(_samplerType, "Start");
    }

    [PatchPrefix]
    private static void Prefix(Object __instance)
    {
        if (_samplerType == null)
            return;

        var existing = Object.FindObjectsOfType(_samplerType);
        foreach (var obj in existing)
        {
            if (obj != __instance)
            {
                Plugin.ModLogger.LogWarning($"Destroying stale PerfectCulling sampler '{obj.name}'.");
                Object.DestroyImmediate(obj);
            }
        }
    }
}

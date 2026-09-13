using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Redeploy.Config;
using Redeploy.Core;
using Redeploy.Core.Compat;
using Redeploy.Core.Engine;
using Redeploy.Drivers;
using Redeploy.Patches;
using Redeploy.Services;
using Redeploy.UI;
using System;

namespace Redeploy;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("com.SPT.core", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.maschine.Redeploy";
    public const string PluginName = "maschine-Redeploy";
    public const string PluginVersion = "0.9.0";

    public static ManualLogSource ModLogger { get; private set; }

    private void Awake()
    {
        ModLogger = base.Logger;
        RedeployCore.Init(ModLogger);
        WarnIfCoreIsOlderThanBuild();
        Settings.Init(Config);

        RaidStartedPatch.EnablePatch();
        DeathInterception.EnsurePatched();
        DeathInterception.Register(new RedeployDriver());
        PerfectCullingFix.EnableOnce();

        gameObject.AddComponent<RespawnPromptPanel>();

        ModLogger.LogInfo($"{PluginName} {PluginVersion} loaded (Core {RedeployCore.Version})");
    }

    /// <summary>
    /// A Core older than this build still runs the driver, degraded to its fixes; the
    /// finding goes to the log and to Chainloader.DependencyErrors, which SPT reports
    /// after loading (docs/RESPAWN-API.md 3.9). RedeployCore.Version exists since Core
    /// 0.9, so reading it is safe against any Core this driver can meet.
    /// </summary>
    private static void WarnIfCoreIsOlderThanBuild()
    {
        try
        {
            var warning = CoreVersionGate.Evaluate(
                PluginName, RedeployCore.Version, CoreVersionGate.ReadBuiltAgainst(typeof(Plugin).Assembly));
            if (warning == null)
                return;

            ModLogger.LogWarning(warning);
            Chainloader.DependencyErrors.Add(warning);
        }
        catch (Exception ex)
        {
            ModLogger.LogWarning($"Core version check skipped: {ex.Message}");
        }
    }
}

using BepInEx;
using BepInEx.Logging;
using Redeploy.Config;
using Redeploy.Core;
using Redeploy.Core.Compat;
using Redeploy.Core.Engine;
using Redeploy.Patches;
using Redeploy.Services;
using Redeploy.UI;

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
        Settings.Init(Config);

        RaidStartedPatch.EnablePatch();
        DeathInterception.EnsurePatched();
        DeathInterception.Register(new RedeployDriver());
        PerfectCullingFix.EnableOnce();

        gameObject.AddComponent<RespawnPromptPanel>();

        ModLogger.LogInfo($"{PluginName} {PluginVersion} loaded (Core {RedeployCore.Version})");
    }
}

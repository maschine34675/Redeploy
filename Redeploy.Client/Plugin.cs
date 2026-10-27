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
using System.Runtime.CompilerServices;

namespace Redeploy;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("com.SPT.core", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.maschine.Redeploy";
    public const string PluginName = "maschine-Redeploy";
    public const string PluginVersion = "1.0.2";

    public static ManualLogSource ModLogger { get; private set; }

    private void Awake()
    {
        ModLogger = base.Logger;
        string error = CoreVersionGate.Check(PluginName, typeof(Plugin).Assembly);
        if (error != null)
        {
            ModLogger.LogError(error);
            Chainloader.DependencyErrors.Add(error);
            enabled = false;
            return;
        }
        InitializeRuntime();
    }

    // Awake must remain loadable without resolving Core types or members.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void InitializeRuntime()
    {
        RedeployCore.Init(ModLogger);
        Settings.Init(Config);

        RedeployRaidStartedPatch.EnablePatch();
        PmcScavPatches.EnablePatches();
        DeathInterception.EnsurePatched();
        DeathInterception.Register(new RedeployDriver());
        PerfectCullingFix.EnableOnce();

        gameObject.AddComponent<RespawnPromptPanel>();

        ModLogger.LogInfo($"{PluginName} {PluginVersion} loaded (Core {RedeployCore.Version})");
    }

}

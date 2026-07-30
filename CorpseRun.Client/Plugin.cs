using BepInEx;
using BepInEx.Logging;
using CorpseRun.Config;
using CorpseRun.Patches;
using CorpseRun.UI;

namespace CorpseRun;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency("com.SPT.core", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.maschine.CorpseRun";
    public const string PluginName = "maschine-CorpseRun";
    public const string PluginVersion = "1.0.0";

    public static ManualLogSource ModLogger { get; private set; }

    private void Awake()
    {
        ModLogger = base.Logger;
        Settings.Init(Config);

        RaidStartedPatch.EnablePatch();
        BlockDeathRaidEndPatch.Enable();
        BlackScreenPatch.Enable();
        RespawnOwnerCleanupPatch.Enable();
        SessionResultPatch.Enable();
        RespawnStatisticsPatch.Enable();
        FixPerfectCullingSamplerPatch.EnableIfPresent();

        gameObject.AddComponent<RespawnPromptPanel>();

        ModLogger.LogInfo($"{PluginName} {PluginVersion} loaded");
    }
}

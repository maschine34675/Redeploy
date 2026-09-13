using System;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using YourMod.Interop;

namespace RespawnApiProbe;

/// <summary>
/// In-game probe for RespawnApi v1 (docs/RESPAWN-API.md 8.5, questions in section 10).
/// The GUID sorts before both drivers (com.maschine.ArenaMode, com.maschine.Redeploy) and the
/// probe declares no dependency on either, so its Awake runs first. This class touches no
/// Core type: the interop tracker binds by reflection before anything compiled against Core
/// has run (question 1). Everything compiled against Core lives in <see cref="ProbeRuntime"/>.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.maschine.AAA.RespawnApiProbe";
    public const string PluginName = "maschine-RespawnApiProbe";
    public const string PluginVersion = "0.1.0";

    internal static ManualLogSource Log { get; private set; }
    internal static ProbeSettings Settings { get; private set; }
    internal static LocalBodyTracker Tracker { get; private set; }

    private void Awake()
    {
        Log = Logger;
        Settings = new ProbeSettings(Config);

        bool coreLoadedBefore = CoreAssemblyLoaded();
        Tracker = gameObject.AddComponent<LocalBodyTracker>();   // binds in its own Awake, right here
        Tracker.Log = message => Log.LogInfo("[probe] interop: " + message);
        Log.LogInfo($"[probe] evt=Awake frame={Time.frameCount} coreLoadedBeforeBind={coreLoadedBefore} "
                    + $"interopBound={Tracker.IsBound} coreLoadedAfterBind={CoreAssemblyLoaded()}");

        gameObject.AddComponent<ProbeRuntime>();
    }

    private static bool CoreAssemblyLoaded()
        => AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == "maschine-Redeploy.Core");
}

/// <summary>All scenario switches default to off: installed and untouched, the probe only observes.</summary>
internal sealed class ProbeSettings
{
    public readonly ConfigEntry<bool> RunRespawnFromReplaced;
    public readonly ConfigEntry<bool> RunRespawnDuringStopping;
    public readonly ConfigEntry<bool> RunRespawnDuringPipeline;
    public readonly ConfigEntry<bool> KillAfterRaidStartSwap;
    public readonly ConfigEntry<float> KillAfterRaidStartSwapDelaySeconds;

    public ProbeSettings(ConfigFile config)
    {
        RunRespawnFromReplaced = config.Bind("Scenarios", "RunRespawnFromReplaced", false,
            "Section 10, question 10. Once per raid, calls RunRespawn from inside a Replaced handler. "
            + "Expected: refused (reentrancy), exactly one pipeline, the camera stays on the new body.");
        RunRespawnDuringStopping = config.Bind("Scenarios", "RunRespawnDuringStopping", false,
            "Once per raid, calls RunRespawn on the first frame the raid is Stopping or SoftStopping "
            + "(extract, MIA, give-up). Expected: refused before any teardown, no event.");
        RunRespawnDuringPipeline = config.Bind("Scenarios", "RunRespawnDuringPipeline", false,
            "Section 10, question 11. From inside every replacement pipeline (a prefix on the game's player "
            + "creation, patched once this is on), calls RunRespawn for the local body. "
            + "Expected: refused (a pipeline is already running), nothing changes.");
        KillAfterRaidStartSwap = config.Bind("Scenarios", "KillAfterRaidStartSwap", false,
            "Section 10, question 3. Kills the new body a short delay after the first Replaced(swap) of a raid, "
            + "i.e. right after ArenaMode's raid-start kit swap. Watch who claims the death.");
        KillAfterRaidStartSwapDelaySeconds = config.Bind("Scenarios", "KillAfterRaidStartSwapDelaySeconds", 0.5f,
            new ConfigDescription("Delay for KillAfterRaidStartSwap.", new AcceptableValueRange<float>(0f, 5f)));
    }
}

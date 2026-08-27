using BepInEx.Logging;

namespace Redeploy.Core;

/// <summary>
/// Entry point for consumers of the shared respawn engine. Exactly one copy of
/// maschine-Redeploy.Core.dll is deployed flat to BepInEx/plugins; every consuming
/// plugin calls <see cref="Init"/> from Awake (idempotent — first caller wins the
/// log source, later callers are no-ops).
/// </summary>
public static class RedeployCore
{
    public static ManualLogSource Log { get; private set; }

    public static System.Version Version => typeof(RedeployCore).Assembly.GetName().Version;

    public static void Init(ManualLogSource log)
    {
        if (Log == null && log != null)
        {
            Log = log;
            Log.LogInfo($"Redeploy.Core {Version} initialized");
        }
    }
}

/// <summary>Null-safe logging for Core internals (Init may not have run in exotic setups).</summary>
internal static class CoreLog
{
    public static void LogInfo(string message) => RedeployCore.Log?.LogInfo(message);
    public static void LogWarning(string message) => RedeployCore.Log?.LogWarning(message);
    public static void LogError(string message) => RedeployCore.Log?.LogError(message);
}

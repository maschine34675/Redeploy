using BepInEx.Bootstrap;

namespace CorpseRun.Services;

/// <summary>
/// CorpseRun's respawn (new LocalPlayer instance, world re-registration, profile swap)
/// cannot work under Fika co-op: remote clients never learn about the replacement player
/// and the packet flow breaks. Detect Fika and stand down entirely instead of corrupting
/// the session — pattern borrowed from PlayerLives 1.2.5's FikaReviveDetector.
/// </summary>
internal static class FikaCompatCheck
{
    private static bool? _fikaLoaded;

    public static bool IsFikaLoaded
    {
        get
        {
            if (_fikaLoaded == null)
            {
                _fikaLoaded = Chainloader.PluginInfos.ContainsKey("com.fika.core");
                if (_fikaLoaded == true)
                {
                    Plugin.ModLogger.LogWarning(
                        "Fika detected — CorpseRun is disabled (respawn is incompatible with co-op).");
                }
            }

            return _fikaLoaded.Value;
        }
    }
}

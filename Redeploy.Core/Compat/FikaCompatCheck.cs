using BepInEx.Bootstrap;

namespace Redeploy.Core.Compat;

/// <summary>
/// Redeploy's respawn (new LocalPlayer instance, world re-registration, profile swap)
/// cannot work under Fika co-op: remote clients never learn about the replacement player
/// and the packet flow breaks. Detect Fika and stand down entirely instead of corrupting
/// the session — pattern borrowed from PlayerLives 1.2.5's FikaReviveDetector.
/// </summary>
public static class FikaCompatCheck
{
    private static bool _fikaLoaded;

    /// <summary>
    /// Only a positive answer is final. PluginInfos fills up while the chainloader runs, so
    /// "not loaded" asked from an early Awake may only mean "not yet"; it is re-asked.
    /// </summary>
    public static bool IsFikaLoaded
    {
        get
        {
            if (_fikaLoaded)
                return true;

            if (!Chainloader.PluginInfos.ContainsKey("com.fika.core"))
                return false;

            _fikaLoaded = true;
            CoreLog.LogWarning("Fika detected — Redeploy is disabled (respawn is incompatible with co-op).");
            return true;
        }
    }
}

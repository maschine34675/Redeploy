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
                    CoreLog.LogWarning(
                        "Fika detected — Redeploy is disabled (respawn is incompatible with co-op).");
                }
            }

            return _fikaLoaded.Value;
        }
    }
}

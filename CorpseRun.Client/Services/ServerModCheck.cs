using System;
using SPT.Common.Http;

namespace CorpseRun.Services;

/// <summary>
/// Verifies the CorpseRun server mod is installed. The client half alone corrupts
/// profiles after a respawn raid (Inventory.Equipment root is never fixed up),
/// so every respawn entry point must be gated on this check.
/// </summary>
public static class ServerModCheck
{
    private static bool? _available;

    public static bool IsServerModAvailable
    {
        get
        {
            if (_available == null)
            {
                try
                {
                    var response = RequestHandler.GetJson("/corpserun/ping");
                    _available = response != null && response.Contains("pong");
                }
                catch (Exception)
                {
                    _available = false;
                }

                if (_available != true)
                {
                    Plugin.ModLogger.LogError(
                        "Server mod not detected (/corpserun/ping failed). " +
                        "Respawn is DISABLED to protect the profile. " +
                        "Install maschine-CorpseRun.Server.dll into SPT/user/mods/CorpseRun/.");
                }
            }

            return _available.Value;
        }
    }
}

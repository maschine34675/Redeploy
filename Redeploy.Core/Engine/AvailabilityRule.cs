using System;

namespace Redeploy.Core.Engine;

/// <summary>
/// The rule behind DeathInterception.IsAvailable (and so RespawnApi.IsAvailable), pure so
/// its operand order is tested (docs/RESPAWN-API.md, S0-12): the patches must be complete
/// and a driver registered before the Fika probe is asked at all, so a plain Redeploy
/// install never pays for the probe and a probe that throws or lies cannot flip a Core
/// that has no driver to serve anyway.
/// </summary>
public static class AvailabilityRule
{
    public static bool Evaluate(bool patchesComplete, int driverCount, Func<bool> fikaLoaded)
    {
        if (!patchesComplete)
            return false;

        if (driverCount <= 0)
            return false;

        return fikaLoaded == null || !fikaLoaded();
    }
}

using System;

namespace Redeploy.Services;

/// <summary>
/// The respawn cooldown is the minimum time a player stays dead, counted from the death.
/// It used to start at the click on a respawn button, so a player who first watched a death
/// replay (or just looked around) waited the whole cooldown again, with the camera parked on
/// the corpse. Pure: linked into Redeploy.Tests.
/// </summary>
internal static class RespawnCooldown
{
    /// <summary>
    /// Seconds still to wait when a respawn is chosen at <paramref name="now"/> by a player who died
    /// at <paramref name="diedAt"/> (same clock). An unknown death time keeps the full cooldown,
    /// which is the old behaviour.
    /// </summary>
    public static float Remaining(float cooldownSeconds, float diedAt, float now)
    {
        if (float.IsNaN(cooldownSeconds) || cooldownSeconds <= 0f)
            return 0f;
        if (float.IsInfinity(cooldownSeconds))
            return float.MaxValue;
        if (float.IsNaN(diedAt) || float.IsNaN(now) || diedAt < 0f || now < diedAt)
            return cooldownSeconds;

        return Math.Max(0f, cooldownSeconds - (now - diedAt));
    }
}

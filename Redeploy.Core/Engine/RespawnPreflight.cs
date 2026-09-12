using System;

namespace Redeploy.Core.Engine;

/// <summary>
/// What RunRespawn checks before it touches anything. The first steps of the pipeline
/// (owner destroy, unregister, profile mutation) are irreversible, so a request that
/// cannot succeed is refused here with nothing changed. Pure, so the matrix is tested.
/// </summary>
public static class RespawnPreflight
{
    /// <summary>The refusal for this request, or null when the pipeline may start.</summary>
    public static Exception Check(
        bool hasRequest,
        bool hasGame,
        bool hasSpawnSelector,
        bool engineAvailable,
        bool raidTearingDown,
        int respawnSignalDepth,
        bool pipelineRunning)
    {
        if (!hasRequest)
            return new ArgumentNullException("request");

        if (!hasGame)
            return new ArgumentException("RespawnRequest.Game is null.");

        if (!hasSpawnSelector)
            return new ArgumentException("RespawnRequest.SpawnSelector is null.");

        if (!engineAvailable)
            return new InvalidOperationException("The respawn engine is unavailable (keep-alive patches incomplete, no driver registered, or Fika loaded).");

        if (raidTearingDown)
            return new OperationCanceledException("The raid is already ending; respawn refused before any teardown.");

        if (respawnSignalDepth > 0)
            return new InvalidOperationException("RunRespawn was called from inside a death or respawn event handler; defer it by one frame.");

        if (pipelineRunning)
            return new InvalidOperationException("A replacement pipeline is already running.");

        return null;
    }
}

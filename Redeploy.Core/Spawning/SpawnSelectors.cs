using EFT;
using EFT.Game.Spawning;
using System;
using UnityEngine;
using UnityEngine.AI;

namespace Redeploy.Core.Spawning;

/// <summary>Strategy for choosing where a respawned player appears.</summary>
public interface ISpawnSelector
{
    (Vector3 position, Quaternion rotation) Resolve(LocalGame game);
}

/// <summary>
/// A random player-category spawn point chosen by the game's own spawn system,
/// with an optional fallback selector when no point can be resolved.
/// </summary>
public sealed class InfilSpawnSelector : ISpawnSelector
{
    private readonly ISpawnSelector _fallback;

    public InfilSpawnSelector(ISpawnSelector fallback = null)
    {
        _fallback = fallback;
    }

    public (Vector3 position, Quaternion rotation) Resolve(LocalGame game)
    {
        var spawnSystem = SpawnSystemCache.GetOrCreate(game);
        if (spawnSystem != null)
        {
            var spawnPoint = spawnSystem.SelectSpawnPoint(
                ESpawnCategory.Player,
                game.Profile.Info.Side,
                null,
                null,
                null,
                null,
                game.Profile.Id);

            if (spawnPoint != null)
                return (spawnPoint.Position, spawnPoint.Rotation);
        }

        if (_fallback != null)
            return _fallback.Resolve(game);

        return (Vector3.zero + Vector3.up * 2f, Quaternion.identity);
    }
}

/// <summary>
/// A NavMesh-validated point on a circle around a given origin (e.g. the corpse):
/// 12 candidates every 30°, sampled within 8 m of the navmesh; falls back to
/// 2 m above the origin when nothing validates.
/// </summary>
public sealed class NearPositionSpawnSelector : ISpawnSelector
{
    private readonly Func<Vector3> _origin;
    private readonly Func<float> _distanceMeters;

    public NearPositionSpawnSelector(Func<Vector3> origin, Func<float> distanceMeters)
    {
        _origin = origin;
        _distanceMeters = distanceMeters;
    }

    public (Vector3 position, Quaternion rotation) Resolve(LocalGame game)
    {
        var origin = _origin != null ? _origin() : Vector3.zero;
        if (origin == default)
            origin = Vector3.zero;

        var distance = Mathf.Max(5f, _distanceMeters != null ? _distanceMeters() : 20f);
        for (var i = 0; i < 12; i++)
        {
            var angle = i * 30f * Mathf.Deg2Rad;
            var candidate = origin + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
            if (NavMesh.SamplePosition(candidate, out var hit, 8f, NavMesh.AllAreas))
                return (hit.position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
        }

        return (origin + Vector3.up * 2f, Quaternion.identity);
    }
}

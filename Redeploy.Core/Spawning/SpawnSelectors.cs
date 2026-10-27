using EFT;
using EFT.Game.Spawning;
using EFT.Interactive;
using System;
using System.Linq;
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
    private readonly Profile _profile;

    public InfilSpawnSelector(ISpawnSelector fallback = null)
    {
        _fallback = fallback;
    }

    /// <summary>
    /// Resolves the new identity's faction before Game.Profile is committed. An explicit
    /// profile requires a native spawn; resolve before starting a destructive replacement.
    /// The one-argument constructor retains its existing fallback behavior.
    /// </summary>
    public InfilSpawnSelector(ISpawnSelector fallback, Profile profile)
    {
        _fallback = fallback;
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public (Vector3 position, Quaternion rotation) Resolve(LocalGame game)
    {
        if (GameSpawnPoints.TryResolve(game, out var position, out var rotation, _profile))
            return (position, rotation);

        if (_profile != null)
            throw new InvalidOperationException("No native player spawn is available for the replacement profile's faction.");

        if (_fallback != null)
            return _fallback.Resolve(game);

        return (Vector3.zero + Vector3.up * 2f, Quaternion.identity);
    }
}

/// <summary>The game's own player spawn points (map-authored, always inside the map).</summary>
internal static class GameSpawnPoints
{
    public static bool TryResolve(LocalGame game, out Vector3 position, out Quaternion rotation, Profile profile = null)
    {
        position = default;
        rotation = Quaternion.identity;

        var spawnSystem = SpawnSystemCache.GetOrCreate(game);
        if (spawnSystem == null)
            return false;

        profile ??= game.Profile;
        var spawnPoint = spawnSystem.SelectSpawnPoint(
            ESpawnCategory.Player,
            profile.Info.Side,
            null,
            null,
            null,
            null,
            profile.Id);

        if (spawnPoint == null)
            return false;

        position = spawnPoint.Position;
        rotation = spawnPoint.Rotation;
        return true;
    }
}

/// <summary>
/// A point near a given origin (e.g. the corpse) that a player could have walked to: on the
/// navmesh, reachable from the origin without a long detour along a walk that does not pass
/// through walls, outside the map's sniper and mine zones, and with room to stand. Rings of 12
/// candidates (the requested distance, half of it, 3 m) from a random start angle; the decisions
/// live in <see cref="NearSpawnPolicy"/>. When nothing passes: the origin itself, then a game
/// spawn point, then 2 m above the origin (the old behaviour). Never throws for a failing query:
/// Resolve runs after the old body is gone, where an exception fails the whole respawn.
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

        var map = MapName(game);
        var facing = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
        var zones = CurrentBorderZones();
        var probe = new Probe(zones);

        var hasAnchor = false;
        var anchor = origin;
        var anchorInZone = false;
        try
        {
            hasAnchor = NavMesh.SamplePosition(origin, out var anchorHit, NearSpawnPolicy.AnchorSampleRadius, NavMesh.AllAreas);
            if (hasAnchor)
            {
                anchor = anchorHit.position;
                anchorInZone = probe.InBorderZone(anchor);
            }
        }
        catch (Exception ex)
        {
            CoreLog.LogWarning($"Respawn position: navmesh query at the corpse failed ({ex.Message}).");
            hasAnchor = false;
        }

        var searched = NearSpawnPolicy.SearchNearCorpse(hasAnchor, anchorInZone);
        string summary;
        if (!hasAnchor)
            summary = "no navmesh at the corpse";
        else if (anchorInZone)
            summary = "the corpse lies in a sniper or mine zone";
        else
        {
            var candidates = NearSpawnPolicy.BuildCandidates(
                _distanceMeters != null ? _distanceMeters() : 20f, UnityEngine.Random.Range(0f, 360f));
            var result = NearSpawnPolicy.Search(candidates, candidate => probe.Judge(anchor, candidate));

            if (result.Chosen >= 0)
            {
                var chosen = candidates[result.Chosen];
                var rejected = NearSpawnPolicy.DescribeRejections(result);
                CoreLog.LogInfo($"Respawn position on {map}: {Describe(probe.Accepted)}, {chosen.RingMeters:0} m ring around the corpse at {Describe(origin)}"
                                + (rejected.Length > 0 ? $"; rejected before it: {rejected}." : "."));
                return (probe.Accepted, facing);
            }

            summary = NearSpawnPolicy.DescribeRejections(result);
        }

        foreach (var fallback in NearSpawnPolicy.FallbackOrder(usableAnchor: searched))
        {
            switch (fallback)
            {
                case NearSpawnPolicy.Fallback.Anchor:
                    if (probe.Safe(() => !probe.Blocked(anchor)))
                    {
                        // Expected in tight rooms: the player stood here, so it is inside the map.
                        CoreLog.LogInfo($"Respawn position on {map}: the corpse's own spot {Describe(anchor)}; no point around it passed ({summary}).");
                        return (anchor, facing);
                    }
                    break;

                case NearSpawnPolicy.Fallback.SpawnPoint:
                    try
                    {
                        if (GameSpawnPoints.TryResolve(game, out var spawnPosition, out var spawnRotation))
                        {
                            CoreLog.LogWarning($"Respawn position on {map}: a map spawn point {Describe(spawnPosition)} instead of the corpse at {Describe(origin)} ({summary}).");
                            return (spawnPosition, spawnRotation);
                        }
                    }
                    catch (Exception ex)
                    {
                        CoreLog.LogWarning($"Respawn position on {map}: map spawn point unavailable ({ex.Message}).");
                    }
                    break;

                case NearSpawnPolicy.Fallback.Legacy:
                    CoreLog.LogWarning($"Respawn position on {map}: nothing validated around the corpse at {Describe(origin)} ({summary}); using 2 m above it.");
                    return (origin + Vector3.up * 2f, Quaternion.identity);
            }
        }

        return (origin + Vector3.up * 2f, Quaternion.identity);
    }

    private static string Describe(Vector3 position)
        => $"({position.x:0.0}, {position.y:0.0}, {position.z:0.0})";

    private static string MapName(LocalGame game)
    {
        try { return game?.Location?.Id ?? "an unknown map"; }
        catch (Exception) { return "an unknown map"; }
    }

    /// <summary>Sniper border zones and minefields of the loaded map; empty if they cannot be read.</summary>
    private static BorderZone[] CurrentBorderZones()
    {
        try
        {
            return LocationScene.GetAllObjects<BorderZone>().ToArray();
        }
        catch (Exception ex)
        {
            CoreLog.LogWarning($"Respawn position: sniper and mine zones unavailable ({ex.Message}); not checking them.");
            return Array.Empty<BorderZone>();
        }
    }

    /// <summary>The world queries for one Resolve, run in <see cref="NearSpawnPolicy.CheckOrder"/>.</summary>
    private sealed class Probe
    {
        private const int CornerBufferSize = 64;

        // Walls and fences only: terrain is left out (a walk over a hill is fine), and so are doors
        // and interactive objects (a closed door is not the edge of the map).
        private static readonly int WalkMask =
            LayersMaskController.PlayerStaticCollisionsMask
            & ~LayersMaskController.TerrainMask
            & ~LayersMaskController.PlayerStaticDoorMask
            & ~LayersMaskController.InteractiveMask;

        private readonly BorderZone[] _zones;
        private readonly NavMeshPath _path = new NavMeshPath();
        private readonly Vector3[] _corners = new Vector3[CornerBufferSize];
        private int _cornerCount;
        private int _pathSearches;

        public Vector3 Accepted { get; private set; }

        public Probe(BorderZone[] zones)
        {
            _zones = zones;
        }

        public bool Safe(Func<bool> query)
        {
            try { return query(); }
            catch (Exception) { return false; }
        }

        public NearSpawnPolicy.Verdict Judge(Vector3 anchor, NearSpawnPolicy.Candidate candidate)
        {
            try
            {
                var target = anchor + new Vector3(candidate.OffsetX, 0f, candidate.OffsetZ);
                var hit = default(NavMeshHit);

                foreach (var check in NearSpawnPolicy.CheckOrder)
                {
                    switch (check)
                    {
                        case NearSpawnPolicy.Verdict.NotOnNavMesh:
                            if (!NavMesh.SamplePosition(target, out hit, NearSpawnPolicy.CandidateSampleRadius, NavMesh.AllAreas))
                                return check;
                            break;

                        case NearSpawnPolicy.Verdict.BorderZone:
                            if (InBorderZone(hit.position))
                                return check;
                            break;

                        case NearSpawnPolicy.Verdict.TooFarVertically:
                            if (NearSpawnPolicy.IsTooFarVertically(hit.position.y - anchor.y))
                                return check;
                            break;

                        case NearSpawnPolicy.Verdict.NoPath:
                            if (!NearSpawnPolicy.PathBudgetLeft(_pathSearches))
                                return NearSpawnPolicy.Verdict.OverBudget;
                            _pathSearches++;
                            if (!NavMesh.CalculatePath(anchor, hit.position, NavMesh.AllAreas, _path)
                                || _path.status != NavMeshPathStatus.PathComplete)
                                return check;
                            ReadCorners();
                            break;

                        case NearSpawnPolicy.Verdict.Detour:
                            if (NearSpawnPolicy.IsDetour(PathLength(), Vector3.Distance(anchor, hit.position)))
                                return check;
                            break;

                        case NearSpawnPolicy.Verdict.CrossesGeometry:
                            if (WalkCrossesGeometry())
                                return check;
                            break;

                        case NearSpawnPolicy.Verdict.Blocked:
                            if (Blocked(hit.position))
                                return check;
                            break;
                    }
                }

                Accepted = hit.position;
                return NearSpawnPolicy.Verdict.Accept;
            }
            catch (Exception)
            {
                return NearSpawnPolicy.Verdict.Failed;
            }
        }

        private void ReadCorners()
        {
            _cornerCount = _path.GetCornersNonAlloc(_corners);
            if (_cornerCount >= CornerBufferSize)
            {
                var all = _path.corners;
                _cornerCount = Math.Min(all.Length, CornerBufferSize);
                Array.Copy(all, _corners, _cornerCount);
            }
        }

        private float PathLength()
        {
            var length = 0f;
            for (var i = 1; i < _cornerCount; i++)
                length += Vector3.Distance(_corners[i - 1], _corners[i]);
            return length;
        }

        /// <summary>
        /// The navmesh path is a graph search: a bake that runs through a fence or the map's outer wall
        /// happily connects both sides. A player-height sweep along the walk catches exactly that.
        /// </summary>
        private bool WalkCrossesGeometry()
        {
            var lift = Vector3.up * NearSpawnPolicy.WalkCheckHeight;
            for (var i = 1; i < _cornerCount; i++)
            {
                if (Physics.Linecast(_corners[i - 1] + lift, _corners[i] + lift, WalkMask, QueryTriggerInteraction.Ignore))
                    return true;
            }
            return false;
        }

        /// <summary>A standing player's capsule, lifted off the ground a little so a slope does not count.</summary>
        public bool Blocked(Vector3 feet)
        {
            const float radius = 0.3f;
            return Physics.CheckCapsule(
                feet + Vector3.up * (0.35f + radius),
                feet + Vector3.up * (1.8f - radius),
                radius,
                LayersMaskController.PlayerStaticCollisionsMask,
                QueryTriggerInteraction.Ignore);
        }

        public bool InBorderZone(Vector3 position)
        {
            foreach (var zone in _zones)
            {
                try
                {
                    if (zone != null && zone.IsInTriggerZone(position))
                        return true;
                }
                catch (Exception)
                {
                    // A broken zone must not make every position unusable.
                }
            }

            return false;
        }
    }
}

using System;
using System.Collections.Generic;

namespace Redeploy.Core.Spawning;

/// <summary>
/// The decisions behind <see cref="NearPositionSpawnSelector"/>, free of Unity and EFT so
/// Redeploy.Tests can link and check them. The selector supplies the world queries.
///
/// Why it exists: the old selector took the first of 12 fixed ring points that had any
/// navmesh within 8 m. That query ignores walls and connectivity, so a corpse near a map
/// border could resolve, every single time, to navmesh on the far side of the border (on
/// this install DrakiaXYZ-Waypoints replaces every map's navmesh with its own bake). And
/// because the next respawn starts from the next corpse, a player killed out there came
/// back out there. A candidate now has to be reachable on foot from the corpse without a
/// long detour, along a walk that does not pass through walls or fences the navmesh does
/// not know about, stay out of the map's sniper and mine zones, and leave room to stand.
/// </summary>
internal static class NearSpawnPolicy
{
    public const int CandidatesPerRing = 12;
    public const float MinRequestedMeters = 5f;
    public const float CloseRingMeters = 3f;
    public const float AnchorSampleRadius = 3f;
    public const float CandidateSampleRadius = 6f;
    public const float MaxHeightDelta = 8f;
    public const float DetourFactor = 2.5f;
    public const float DetourSlackMeters = 15f;
    public const int MaxPathSearches = 24;

    /// <summary>Height above the walk's corners at which the walk is swept for walls: above railings, below ceilings.</summary>
    public const float WalkCheckHeight = 1.3f;

    public readonly struct Candidate
    {
        public readonly int Ring;
        public readonly int Index;
        public readonly float RingMeters;
        public readonly float OffsetX;
        public readonly float OffsetZ;

        public Candidate(int ring, int index, float ringMeters, float offsetX, float offsetZ)
        {
            Ring = ring;
            Index = index;
            RingMeters = ringMeters;
            OffsetX = offsetX;
            OffsetZ = offsetZ;
        }
    }

    /// <summary>The outcome for one candidate. The array indices of the rejection counts follow this order.</summary>
    public enum Verdict
    {
        Accept,
        NotOnNavMesh,
        BorderZone,
        TooFarVertically,
        OverBudget,
        NoPath,
        Detour,
        CrossesGeometry,
        Blocked,
        Failed,
    }

    public enum Fallback
    {
        Anchor,
        SpawnPoint,
        Legacy,
    }

    /// <summary>
    /// The order the selector runs its checks in, each named by the verdict it rejects with:
    /// cheap lookups first, the path search next (bounded by <see cref="MaxPathSearches"/>, over
    /// which it rejects as OverBudget), the checks that need the path after it, physics last.
    /// </summary>
    public static readonly IReadOnlyList<Verdict> CheckOrder = new[]
    {
        Verdict.NotOnNavMesh,
        Verdict.BorderZone,
        Verdict.TooFarVertically,
        Verdict.NoPath,
        Verdict.Detour,
        Verdict.CrossesGeometry,
        Verdict.Blocked,
    };

    /// <summary>
    /// Rings from far to close: the requested distance (at least 5 m, as before), half of it when
    /// that still lies clearly outside the close ring, and a close ring of 3 m. Twelve points per
    /// ring, starting at <paramref name="startAngleDegrees"/> and stepping 30 degrees. A random start
    /// angle means a second death on the same spot does not replay the same choice.
    /// </summary>
    public static List<Candidate> BuildCandidates(float requestedMeters, float startAngleDegrees)
    {
        if (float.IsNaN(requestedMeters) || float.IsInfinity(requestedMeters))
            requestedMeters = MinRequestedMeters;
        var far = Math.Max(MinRequestedMeters, requestedMeters);

        var rings = new List<float> { far };
        var half = far * 0.5f;
        if (half >= CloseRingMeters + 2f)
            rings.Add(half);
        rings.Add(CloseRingMeters);

        var candidates = new List<Candidate>(rings.Count * CandidatesPerRing);
        for (var ring = 0; ring < rings.Count; ring++)
        {
            for (var i = 0; i < CandidatesPerRing; i++)
            {
                var radians = (startAngleDegrees + i * (360f / CandidatesPerRing)) * (Math.PI / 180.0);
                candidates.Add(new Candidate(
                    ring, i, rings[ring],
                    (float)(Math.Cos(radians) * rings[ring]),
                    (float)(Math.Sin(radians) * rings[ring])));
            }
        }

        return candidates;
    }

    /// <summary>
    /// A walk this much longer than the straight line (in 3D, so stairs and slopes are not
    /// penalised) leads around a wall, a fence or the map edge.
    /// </summary>
    public static bool IsDetour(float pathLength, float straightDistance)
        => pathLength > straightDistance * DetourFactor + DetourSlackMeters;

    public static bool IsTooFarVertically(float heightDelta)
        => Math.Abs(heightDelta) > MaxHeightDelta;

    /// <summary>Whether another path search may run. A failing search explores everything reachable, so they are capped per respawn.</summary>
    public static bool PathBudgetLeft(int searchesUsed)
        => searchesUsed < MaxPathSearches;

    /// <summary>
    /// Search around the corpse only when it lies on the navmesh and outside the sniper and mine
    /// zones. A corpse inside such a zone is where an earlier bad placement left the player: searching
    /// around it would keep the next respawn out there too.
    /// </summary>
    public static bool SearchNearCorpse(bool hasAnchor, bool anchorInBorderZone)
        => hasAnchor && !anchorInBorderZone;

    public readonly struct SearchResult
    {
        public readonly int Chosen;
        public readonly int[] Rejections;

        public SearchResult(int chosen, int[] rejections)
        {
            Chosen = chosen;
            Rejections = rejections;
        }

        public int RejectedAs(Verdict verdict) => Rejections[(int)verdict];
    }

    /// <summary>
    /// The first accepted candidate in order wins; nothing after it is judged, because judging
    /// costs a path search. Rejections are counted per verdict for the log line.
    /// </summary>
    public static SearchResult Search(IReadOnlyList<Candidate> candidates, Func<Candidate, Verdict> judge)
    {
        var rejections = new int[Enum.GetValues(typeof(Verdict)).Length];
        for (var i = 0; i < candidates.Count; i++)
        {
            var verdict = judge(candidates[i]);
            if (verdict == Verdict.Accept)
                return new SearchResult(i, rejections);
            rejections[(int)verdict]++;
        }

        return new SearchResult(-1, rejections);
    }

    /// <summary>
    /// When no candidate passes. The corpse's own spot comes first when it is usable: the player
    /// stood there, so it is inside the map. Otherwise the game's own player spawn points, which are
    /// map-authored; the unchecked corpse position is the last resort.
    /// </summary>
    public static IReadOnlyList<Fallback> FallbackOrder(bool usableAnchor)
        => usableAnchor
            ? new[] { Fallback.Anchor, Fallback.SpawnPoint, Fallback.Legacy }
            : new[] { Fallback.SpawnPoint, Fallback.Legacy };

    /// <summary>"4 no navmesh, 1 not reachable" for the log; empty when nothing was rejected.</summary>
    public static string DescribeRejections(SearchResult result)
    {
        var parts = new List<string>();
        void Add(Verdict verdict, string text)
        {
            var count = result.RejectedAs(verdict);
            if (count > 0) parts.Add(count + " " + text);
        }

        Add(Verdict.NotOnNavMesh, "no navmesh");
        Add(Verdict.BorderZone, "sniper or mine zone");
        Add(Verdict.TooFarVertically, "too far up or down");
        Add(Verdict.OverBudget, "path search budget spent");
        Add(Verdict.NoPath, "not reachable");
        Add(Verdict.Detour, "long detour");
        Add(Verdict.CrossesGeometry, "walk passes through a wall");
        Add(Verdict.Blocked, "no room to stand");
        Add(Verdict.Failed, "query failed");
        return string.Join(", ", parts);
    }
}

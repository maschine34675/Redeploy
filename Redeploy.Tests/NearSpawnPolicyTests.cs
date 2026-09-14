using Redeploy.Core.Spawning;
using Xunit;
using Verdict = Redeploy.Core.Spawning.NearSpawnPolicy.Verdict;

namespace Redeploy.Tests;

/// <summary>
/// The decisions of NearPositionSpawnSelector (a respawn near the corpse). The bug these guard:
/// a fixed candidate order with "any navmesh within 8 m" put a player who died near a map
/// border outside the map, the same way every time, and the next respawn started from there.
/// </summary>
public class NearSpawnPolicyTests
{
    private static readonly Verdict[] Rejections = Enum.GetValues<Verdict>().Where(v => v != Verdict.Accept).ToArray();

    // ---------------------------------------------------------------- candidates

    [Fact]
    public void RingsGoFromTheRequestedDistanceToHalfOfItToTheCloseRing()
    {
        var candidates = NearSpawnPolicy.BuildCandidates(20f, 0f);

        Assert.Equal(3 * NearSpawnPolicy.CandidatesPerRing, candidates.Count);
        Assert.All(candidates.Take(12), c => Assert.Equal(20f, c.RingMeters));
        Assert.All(candidates.Skip(12).Take(12), c => Assert.Equal(10f, c.RingMeters));
        Assert.All(candidates.Skip(24), c => Assert.Equal(NearSpawnPolicy.CloseRingMeters, c.RingMeters));
        Assert.All(candidates, c => Assert.Equal(c.RingMeters, MathF.Sqrt(c.OffsetX * c.OffsetX + c.OffsetZ * c.OffsetZ), 3));
    }

    [Theory]
    [InlineData(1f, 5f)]       // the old floor of 5 m stays
    [InlineData(-3f, 5f)]
    [InlineData(float.NaN, 5f)]
    [InlineData(8f, 8f)]
    public void TheRequestedDistanceKeepsItsFloor(float requested, float expectedFar)
        => Assert.Equal(expectedFar, NearSpawnPolicy.BuildCandidates(requested, 0f)[0].RingMeters);

    [Theory]
    [InlineData(5f, 2)]        // half (2.5) would sit on the close ring: skipped
    [InlineData(9f, 2)]        // half 4.5 < 3 + 2
    [InlineData(10f, 3)]       // half 5 = 3 + 2
    [InlineData(40f, 3)]
    public void TheHalfRingIsOnlyAddedWhenItIsClearlyOutsideTheCloseRing(float requested, int rings)
        => Assert.Equal(rings * NearSpawnPolicy.CandidatesPerRing, NearSpawnPolicy.BuildCandidates(requested, 0f).Count);

    [Fact]
    public void CandidatesStartAtTheGivenAngleAndStep30Degrees()
    {
        var at0 = NearSpawnPolicy.BuildCandidates(20f, 0f);
        Assert.Equal(20f, at0[0].OffsetX, 3);
        Assert.Equal(0f, at0[0].OffsetZ, 3);
        Assert.Equal(20f * MathF.Cos(MathF.PI / 6f), at0[1].OffsetX, 3);

        var at90 = NearSpawnPolicy.BuildCandidates(20f, 90f);
        Assert.Equal(0f, at90[0].OffsetX, 3);
        Assert.Equal(20f, at90[0].OffsetZ, 3);
    }

    [Fact]
    public void ADifferentStartAngleChangesTheFirstChoiceOnTheSameSpot()
    {
        // The reported bug: the same corpse spot always produced the same (bad) point. With the
        // start angle drawn per respawn, the first candidate that passes differs between deaths.
        static Verdict AcceptEastHalf(NearSpawnPolicy.Candidate c) => c.OffsetX > 0.1f ? Verdict.Accept : Verdict.NotOnNavMesh;

        var fromEast = NearSpawnPolicy.BuildCandidates(20f, 0f);
        var fromNorth = NearSpawnPolicy.BuildCandidates(20f, 60f);
        var a = fromEast[NearSpawnPolicy.Search(fromEast, AcceptEastHalf).Chosen];
        var b = fromNorth[NearSpawnPolicy.Search(fromNorth, AcceptEastHalf).Chosen];

        Assert.NotEqual((a.OffsetX, a.OffsetZ), (b.OffsetX, b.OffsetZ));
    }

    // ---------------------------------------------------------------- checks

    [Theory]
    [InlineData(20f, 20f, false)]
    [InlineData(64.9f, 20f, false)]    // 20 * 2.5 + 15 = 65
    [InlineData(65.1f, 20f, true)]
    [InlineData(22.4f, 3f, false)]     // 3 * 2.5 + 15 = 22.5
    [InlineData(22.6f, 3f, true)]
    [InlineData(25f, 6.7f, false)]     // a stairwell 3 m across and 6 m down: judged on the 3D distance
    public void ALongWalkAroundCountsAsADetour(float pathLength, float straight, bool detour)
        => Assert.Equal(detour, NearSpawnPolicy.IsDetour(pathLength, straight));

    [Theory]
    [InlineData(0f, false)]
    [InlineData(8f, false)]
    [InlineData(-8f, false)]
    [InlineData(8.01f, true)]
    [InlineData(-12f, true)]
    public void HeightDeltaIsLimitedBothWays(float delta, bool tooFar)
        => Assert.Equal(tooFar, NearSpawnPolicy.IsTooFarVertically(delta));

    [Theory]
    [InlineData(0, true)]
    [InlineData(23, true)]
    [InlineData(24, false)]
    [InlineData(100, false)]
    public void PathSearchesAreCappedPerRespawn(int used, bool allowed)
        => Assert.Equal(allowed, NearSpawnPolicy.PathBudgetLeft(used));

    [Fact]
    public void ChecksRunCheapFirstThePathBeforeWhatNeedsItAndPhysicsLast()
    {
        var order = NearSpawnPolicy.CheckOrder.ToList();

        Assert.Equal(Verdict.NotOnNavMesh, order[0]);                       // everything else needs the sampled point
        Assert.True(order.IndexOf(Verdict.BorderZone) < order.IndexOf(Verdict.NoPath), "zones before the path search");
        Assert.True(order.IndexOf(Verdict.TooFarVertically) < order.IndexOf(Verdict.NoPath), "height before the path search");
        Assert.True(order.IndexOf(Verdict.NoPath) < order.IndexOf(Verdict.Detour), "the detour needs the path");
        Assert.True(order.IndexOf(Verdict.NoPath) < order.IndexOf(Verdict.CrossesGeometry), "the wall sweep needs the path");
        Assert.Equal(Verdict.Blocked, order[^1]);                            // the capsule query last
        Assert.Equal(order.Count, order.Distinct().Count());
        Assert.DoesNotContain(Verdict.Accept, order);
        Assert.DoesNotContain(Verdict.OverBudget, order);                    // produced inside the path check
        Assert.DoesNotContain(Verdict.Failed, order);                        // produced by an exception
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]    // the corpse lies where an earlier bad placement left it: do not search there
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void TheSearchRunsOnlyAroundACorpseOnTheNavmeshAndOutsideTheZones(bool hasAnchor, bool anchorInZone, bool search)
        => Assert.Equal(search, NearSpawnPolicy.SearchNearCorpse(hasAnchor, anchorInZone));

    // ---------------------------------------------------------------- search

    [Fact]
    public void TheFirstAcceptedCandidateWinsAndNothingAfterItIsJudged()
    {
        var candidates = NearSpawnPolicy.BuildCandidates(20f, 0f);
        var judged = new List<int>();
        var verdicts = new[] { Verdict.NotOnNavMesh, Verdict.BorderZone, Verdict.NoPath, Verdict.Accept, Verdict.Accept };

        var result = NearSpawnPolicy.Search(candidates, c =>
        {
            var index = candidates.IndexOf(c);
            judged.Add(index);
            return index < verdicts.Length ? verdicts[index] : Verdict.Accept;
        });

        Assert.Equal(3, result.Chosen);
        Assert.Equal(new[] { 0, 1, 2, 3 }, judged);
        Assert.Equal(1, result.RejectedAs(Verdict.NotOnNavMesh));
        Assert.Equal(1, result.RejectedAs(Verdict.BorderZone));
        Assert.Equal(1, result.RejectedAs(Verdict.NoPath));
        Assert.Equal(0, result.RejectedAs(Verdict.Accept));
    }

    [Fact]
    public void EveryRejectionIsCountedWhenNothingPasses()
    {
        var candidates = NearSpawnPolicy.BuildCandidates(20f, 0f);
        Assert.Equal(0, candidates.Count % Rejections.Length);

        var result = NearSpawnPolicy.Search(candidates, c => Rejections[candidates.IndexOf(c) % Rejections.Length]);

        Assert.Equal(-1, result.Chosen);
        var each = candidates.Count / Rejections.Length;
        foreach (var verdict in Rejections)
            Assert.Equal(each, result.RejectedAs(verdict));
        Assert.Equal(
            $"{each} no navmesh, {each} sniper or mine zone, {each} too far up or down, {each} path search budget spent, "
            + $"{each} not reachable, {each} long detour, {each} walk passes through a wall, {each} no room to stand, {each} query failed",
            NearSpawnPolicy.DescribeRejections(result));
    }

    [Fact]
    public void FarRingsAreTriedBeforeCloseOnes()
    {
        var candidates = NearSpawnPolicy.BuildCandidates(20f, 0f);
        var result = NearSpawnPolicy.Search(candidates, c => c.RingMeters < 20f ? Verdict.Accept : Verdict.NoPath);

        Assert.Equal(10f, candidates[result.Chosen].RingMeters);
        Assert.Equal(12, result.RejectedAs(Verdict.NoPath));
    }

    // ---------------------------------------------------------------- fallbacks

    [Fact]
    public void WithAUsableAnchorTheCorpseSpotComesBeforeASpawnPointAndTheOldFallbackIsLast()
        => Assert.Equal(
            new[] { NearSpawnPolicy.Fallback.Anchor, NearSpawnPolicy.Fallback.SpawnPoint, NearSpawnPolicy.Fallback.Legacy },
            NearSpawnPolicy.FallbackOrder(usableAnchor: true));

    [Fact]
    public void WithoutAUsableAnchorASpawnPointComesFirst()
        => Assert.Equal(
            new[] { NearSpawnPolicy.Fallback.SpawnPoint, NearSpawnPolicy.Fallback.Legacy },
            NearSpawnPolicy.FallbackOrder(usableAnchor: false));

    [Fact]
    public void NoRejectionsDescribeAsEmpty()
        => Assert.Equal("", NearSpawnPolicy.DescribeRejections(
            new NearSpawnPolicy.SearchResult(0, new int[Enum.GetValues<Verdict>().Length])));
}

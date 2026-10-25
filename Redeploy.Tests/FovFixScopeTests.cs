using System;
using Redeploy.Core.Compat;
using Xunit;

namespace Redeploy.Tests;

public sealed class FovFixScopeTests
{
    private sealed class Fixture
    {
        internal readonly FovFixScope Scope = new();
        internal readonly object Game = new(), World = new(), Controller = new(), Old = new(), Next = new();
        internal void Prepare() => Scope.Prepare(Game, World, Controller, Old);
        internal void Complete() { Prepare(); Scope.CompletePreparation(); }
        internal void CommitNext()
        {
            Assert.True(Scope.TrackCandidate(Next, Game, World, true));
            Assert.True(Scope.CanBind(Next, Game, World, Next, Next, true, true, true, true, true));
            Scope.Bind(Next);
        }
        internal bool Allows(object body) => Scope.Allows(body, Game, World, Next, Next, true, true);
    }

    [Fact]
    public void RefusedFirstPreparationNeverClaimsTheOrdinaryBodyOrController()
    {
        var f = new Fixture();
        f.Prepare();
        Assert.Null(f.Scope.Active);
        Assert.False(f.Scope.Enabled);
        Assert.False(f.Scope.OwnsBody(f.Old));
        Assert.False(f.Scope.TouchedController(f.Controller));
        f.Scope.AbortPreparation();
        f.Scope.CompletePreparation();
        Assert.Null(f.Scope.Active);
        Assert.False(f.Scope.OwnsBody(f.Old));
    }

    [Fact]
    public void RefusedLaterPreparationPreservesPriorAuthorityAndTombstones()
    {
        var f = new Fixture(); f.Complete(); f.CommitNext();
        var committed = f.Scope.Active;
        f.Scope.Prepare(f.Game, f.World, f.Controller, f.Next);
        Assert.Same(committed, f.Scope.Active);
        Assert.True(f.Allows(f.Next));
        f.Scope.AbortPreparation();
        Assert.Same(committed, f.Scope.Active);
        Assert.True(f.Scope.OwnsBody(f.Old));
        Assert.True(f.Allows(f.Next));
        Assert.False(f.Allows(f.Old));
    }

    [Fact]
    public void TeardownFreezesOldBodyEvenWhileItIsStillAliveAndAuthoritative()
    {
        var f = new Fixture(); f.Complete();
        Assert.True(f.Scope.OwnsBody(f.Old));
        Assert.True(f.Scope.TouchedController(f.Controller));
        Assert.False(f.Scope.Allows(f.Old, f.Game, f.World, f.Old, f.Old, true, true));
    }

    [Theory]
    [InlineData("game")]
    [InlineData("world")]
    [InlineData("main")]
    [InlineData("local")]
    [InlineData("raid")]
    [InlineData("owner")]
    [InlineData("dead")]
    [InlineData("creating")]
    [InlineData("null")]
    public void CommitRequiresEveryAuthoritativeIdentityAndReadinessBoundary(string mismatch)
    {
        var f = new Fixture(); f.Complete();
        var other = new object();
        Assert.False(f.Scope.CanBind(mismatch == "null" ? null : f.Next,
            mismatch == "game" ? other : f.Game, mismatch == "world" ? other : f.World,
            mismatch == "main" ? f.Old : f.Next, mismatch == "local" ? f.Old : f.Next,
            mismatch != "raid", mismatch != "owner", mismatch != "dead", mismatch == "creating", false));
        Assert.Same(f.Old, f.Scope.Active.Body);
        Assert.True(f.Scope.Active.Frozen);
    }

    [Fact]
    public void PublishedReplacementCanCommitBeforeCreatingFlagIsCleared()
    {
        var f = new Fixture(); f.Complete(); f.CommitNext();
        Assert.True(f.Allows(f.Next));
        Assert.False(f.Allows(f.Old));
        Assert.True(f.Scope.OwnsBody(f.Old));
        Assert.True(f.Scope.OwnsBody(f.Next));
    }

    [Fact]
    public void FailedRewireCanFollowOnlyAProvenAuthoritativeBodyAfterCreatingEnds()
    {
        var f = new Fixture(); f.Complete();
        f.Scope.TrackCandidate(f.Next, f.Game, f.World, true);
        Assert.False(f.Scope.CanBind(f.Next, f.Game, f.World, f.Next, f.Old, true, true, true, false, false));
        Assert.True(f.Scope.CanBind(f.Next, f.Game, f.World, f.Next, f.Next, true, true, true, false, false));
        f.Scope.Bind(f.Next);
        Assert.True(f.Allows(f.Next));
    }

    [Fact]
    public void LiveBodyRollbackCanResumeTheOriginalBodyWithoutCreatingAnotherLease()
    {
        var f = new Fixture(); f.Complete();
        Assert.True(f.Scope.CanBind(f.Old, f.Game, f.World, f.Old, f.Old, true, true, true, false, false));
        f.Scope.Bind(f.Old);
        Assert.True(f.Scope.Allows(f.Old, f.Game, f.World, f.Old, f.Old, true, true));
    }

    [Fact]
    public void FailedCandidateRemainsFencedAfterWorldRetirement()
    {
        var f = new Fixture(); f.Complete();
        Assert.True(f.Scope.TrackCandidate(f.Next, f.Game, f.World, true));
        Assert.NotNull(f.Scope.RetireWorld(f.World));
        Assert.False(f.Scope.Enabled);
        Assert.True(f.Scope.OwnsBody(f.Next));
        Assert.False(f.Allows(f.Next));
        Assert.True(f.Scope.TouchedController(f.Controller));
    }

    [Fact]
    public void DelayedOldWorldDisposalCannotRetireTheNewWorld()
    {
        var f = new Fixture(); f.Complete(); f.CommitNext();
        f.Scope.RetireWorld(f.World);
        var game = new object(); var world = new object(); var body = new object();
        f.Scope.Prepare(game, world, f.Controller, body);
        f.Scope.CompletePreparation();
        var current = f.Scope.Active;
        Assert.Null(f.Scope.RetireWorld(f.World));
        Assert.Same(current, f.Scope.Active);
        Assert.True(f.Scope.OwnsBody(f.Old));
        Assert.True(f.Scope.OwnsBody(f.Next));
    }

    [Fact]
    public void WorldDisposalCancelsProvisionalAdoptionWithoutClaimingItsBody()
    {
        var f = new Fixture(); f.Prepare();
        Assert.Null(f.Scope.RetireWorld(f.World));
        f.Scope.CompletePreparation();
        Assert.Null(f.Scope.Active);
        Assert.False(f.Scope.OwnsBody(f.Old));
    }

    [Fact]
    public void CandidateTrackingNeedsExistingOperationAndPhysicalWorldOwnership()
    {
        var f = new Fixture();
        Assert.False(f.Scope.TrackCandidate(f.Next, f.Game, f.World, true));
        f.Complete();
        Assert.False(f.Scope.TrackCandidate(f.Next, f.Game, f.World, false));
        Assert.False(f.Scope.TrackCandidate(f.Next, new object(), f.World, true));
        Assert.False(f.Scope.TrackCandidate(f.Next, f.Game, new object(), true));
        Assert.False(f.Scope.OwnsBody(f.Next));
        Assert.Throws<InvalidOperationException>(() => f.Scope.Bind(f.Next));
    }

    [Theory]
    [InlineData("game")]
    [InlineData("world")]
    [InlineData("controller")]
    [InlineData("body")]
    public void ExistingHandoverCannotBeCapturedByASecondOwner(string mismatch)
    {
        var f = new Fixture(); f.Complete(); f.CommitNext();
        var current = f.Scope.Active;
        Assert.Throws<InvalidOperationException>(() => f.Scope.Prepare(
            mismatch == "game" ? new object() : f.Game, mismatch == "world" ? new object() : f.World,
            mismatch == "controller" ? new object() : f.Controller, mismatch == "body" ? new object() : f.Next));
        Assert.Same(current, f.Scope.Active);
    }

    [Fact]
    public void SubsequentReplacementFencesEveryOlderCorpse()
    {
        var f = new Fixture(); f.Complete(); f.CommitNext();
        var third = new object();
        f.Scope.Prepare(f.Game, f.World, f.Controller, f.Next);
        f.Scope.CompletePreparation();
        f.Scope.TrackCandidate(third, f.Game, f.World, true);
        f.Scope.Bind(third);
        Assert.False(f.Scope.Allows(f.Old, f.Game, f.World, third, third, true, true));
        Assert.False(f.Scope.Allows(f.Next, f.Game, f.World, third, third, true, true));
        Assert.True(f.Scope.Allows(third, f.Game, f.World, third, third, true, true));
    }

    private sealed class EqualBody
    {
        public override bool Equals(object obj) => obj is EqualBody;
        public override int GetHashCode() => 1;
    }

    [Fact]
    public void EqualProfilesOrBodyEqualityCannotImpersonatePhysicalIdentity()
    {
        var f = new Fixture(); f.Complete();
        var a = new EqualBody(); var b = new EqualBody();
        f.Scope.TrackCandidate(a, f.Game, f.World, true);
        f.Scope.Bind(a);
        Assert.False(f.Scope.OwnsBody(b));
        Assert.False(f.Scope.Allows(b, f.Game, f.World, b, b, true, true));
        Assert.False(f.Scope.CanBind(a, f.Game, f.World, b, a, true, true, true, false, false));
    }
}

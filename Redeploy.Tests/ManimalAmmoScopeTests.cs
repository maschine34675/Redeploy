using Redeploy.Core.Compat;
using System.Runtime.CompilerServices;
using Xunit;

namespace Redeploy.Tests;

public class ManimalAmmoScopeTests
{
    [Fact]
    public void ObservationAndCancelledPreparationDoNotOwnOrdinaryBody()
    {
        var scope = new ManimalAmmoScope(); var body = new object(); var callback = new object();
        var ticket = scope.Stamp(callback, body);
        scope.Prepare(body, new object(), new object(), new object());
        scope.AbortPreparation();
        Assert.False(ticket.Owner.Owned);
        Assert.True(ManimalAmmoScope.MayResume(ticket, null, null, false, false));
        Assert.Empty(scope.Active);
        Assert.Null(scope.PreparedBody);
    }

    [Fact]
    public void RetirementFencesAlreadyRunningCallbacksAndOnlyRemovesExactSession()
    {
        var scope = new ManimalAmmoScope(); var world = new object(); var old = new object();
        var current = new object(); var session = new object();
        var iterator = scope.Stamp(new object(), old);
        scope.Prepare(old, world, new object(), session);
        var retired = scope.CommitCandidate(current, world, new object(), session, true);
        Assert.True(retired.Retired);
        Assert.False(ManimalAmmoScope.MayResume(iterator, world, current, true, true));
        Assert.True(ManimalAmmoScope.MayRemoveSession(retired, session));
        Assert.False(ManimalAmmoScope.MayRemoveSession(retired, new object()));
        Assert.True(ManimalAmmoScope.MayRun(scope.Find(current), world, current, true, true));
        Assert.False(scope.Retire(retired));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void InvalidCandidateDoesNotRetirePreparedBody(bool authorized, bool sameBody, bool differentSession)
    {
        var scope = new ManimalAmmoScope(); var old = new object(); var world = new object(); var session = new object();
        scope.Prepare(old, world, new object(), session);
        Assert.Throws<InvalidOperationException>(() => scope.CommitCandidate(sameBody ? old : new object(), world,
            new object(), differentSession ? new object() : session, authorized));
        Assert.Empty(scope.Active);
        Assert.Null(scope.Find(old));
        scope.AbortPreparation();
    }

    [Fact]
    public void EqualProfileOrValueDoesNotGrantPhysicalOwnership()
    {
        var scope = new ManimalAmmoScope(); var old = new SameValue(); var other = new SameValue();
        var world = new object(); var session = new object();
        scope.Prepare(old, world, new object(), session);
        scope.CommitCandidate(new object(), world, new object(), session, true);
        Assert.Null(scope.Find(other));
        Assert.True(ManimalAmmoScope.MayRun(scope.Find(other), null, null, false, false));
    }

    [Fact]
    public void FirstOrdinarySessionCreatedDuringConstructionIsRetiredWithItsExactBody()
    {
        var scope = new ManimalAmmoScope(); var body = new object(); var world = new object();
        scope.Prepare(body, world, new object(), null);
        var appeared = new object(); var callback = scope.Stamp(new object(), body);
        var retired = scope.CommitCandidate(new object(), world, new object(), appeared, true);
        Assert.Same(appeared, retired.Session);
        Assert.True(ManimalAmmoScope.MayRemoveSession(retired, appeared));
        Assert.False(ManimalAmmoScope.MayResume(callback, world, body, true, true));
    }

    [Fact]
    public void OldWorldTeardownCannotRetireNewWorld()
    {
        var scope = new ManimalAmmoScope(); var world = new object(); var current = new object();
        scope.Prepare(new object(), world, new object(), null);
        scope.CommitCandidate(current, world, new object(), null, true);
        Assert.Empty(scope.RetireWorld(new object()));
        Assert.Single(scope.Active);
        Assert.Single(scope.RetireWorld(world));
        Assert.Empty(scope.Active);
        Assert.False(ManimalAmmoScope.MayRun(scope.Find(current), world, current, true, true));
    }

    [Fact]
    public void RecoveryStartsFreshGenerationWithoutRearmingOldDropIteratorOrTask()
    {
        var scope = new ManimalAmmoScope(); var old = new object(); var world = new object(); var inventory = new object();
        var session = new object(); var callback = new object(); var oldTicket = scope.Stamp(callback, old);
        scope.Prepare(old, world, inventory, session);
        var retired = scope.CommitCandidate(new object(), world, new object(), session, true);
        scope.ForgetSession(retired);
        Assert.Null(scope.Recover(old, world, inventory, false));
        var recovered = scope.Recover(old, world, inventory, true);
        Assert.NotNull(recovered);
        Assert.False(ManimalAmmoScope.MayResume(oldTicket, world, old, true, true));
        var nextSession = new object(); scope.CaptureSession(recovered, nextSession);
        var fresh = scope.Stamp(new object(), old, nextSession);
        Assert.True(ManimalAmmoScope.MayResume(fresh, world, old, true, true));
        Assert.False(ManimalAmmoScope.MayRemoveSession(recovered, nextSession));
    }

    [Fact]
    public void AFactoryStampedCurrentOwnedIteratorNeedsNoSessionOrOrdinaryFallback()
    {
        var scope = new ManimalAmmoScope(); var world = new object(); var old = new object(); var current = new object();
        scope.Prepare(old, world, new object(), null);
        scope.CommitCandidate(current, world, new object(), null, true);
        var iterator = new object();
        Assert.Null(scope.ForCallback(iterator));
        var ticket = scope.Stamp(iterator, current);
        Assert.Same(ticket, scope.ForCallback(iterator));
        Assert.Null(ticket.Session);
        Assert.True(ManimalAmmoScope.MayResume(ticket, world, current, true, true));
        scope.RetireWorld(world);
        Assert.False(ManimalAmmoScope.MayResume(ticket, world, current, true, true));
    }

    [Fact]
    public void CurrentCompletionRequiresExactSessionAndLiveCurrentBody()
    {
        var scope = new ManimalAmmoScope(); var world = new object(); var current = new object();
        scope.Prepare(new object(), world, new object(), null);
        scope.CommitCandidate(current, world, new object(), null, true);
        var lease = scope.Find(current); var session = new object(); scope.CaptureSession(lease, session);
        var callback = new object(); var task = scope.Stamp(callback, current, session);
        Assert.True(ManimalAmmoScope.MayResume(task, world, current, true, true));
        Assert.False(ManimalAmmoScope.MayResume(task, world, current, true, false));
        Assert.False(ManimalAmmoScope.MayResume(task, world, new object(), true, true));
        Assert.False(ManimalAmmoScope.MayResume(task, new object(), current, true, true));
        Assert.Throws<InvalidOperationException>(() => scope.CaptureSession(lease, new object()));
        Assert.Throws<InvalidOperationException>(() => scope.Stamp(callback, new object()));
    }

    [Fact]
    public void RetiredBodyAndSessionAreNotRetainedByTheAdapter()
    {
        var scope = new ManimalAmmoScope();
        var references = RetireAndRelease(scope);
        for (int i = 0; i < 3; ++i) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Assert.False(references.Body.IsAlive);
        Assert.False(references.Session.IsAlive);
        GC.KeepAlive(scope);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Body, WeakReference Session) RetireAndRelease(ManimalAmmoScope scope)
    {
        var old = new object(); var session = new object(); var world = new object();
        scope.Stamp(new object(), old);
        scope.Prepare(old, world, new object(), session);
        var lease = scope.CommitCandidate(new object(), world, new object(), session, true);
        scope.ForgetSession(lease);
        return (new WeakReference(old), new WeakReference(session));
    }

    private sealed class SameValue
    {
        public override bool Equals(object obj) => obj is SameValue;
        public override int GetHashCode() => 1;
    }
}

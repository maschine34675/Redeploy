using System.Runtime.CompilerServices;
using Redeploy.Core.Compat;
using Xunit;

namespace Redeploy.Tests;

public class MagazineCheckScopeTests
{
    [Fact]
    public void ObservingOrdinaryPanelAndCancellingPreparationDoesNotTakeAuthority()
    {
        var scope = new MagazineCheckScope(); var game = new object(); var world = new object();
        var body = new object(); var panel = new object();
        var observed = scope.Observe(panel, body, game, world);
        scope.Prepare(game, world, body, panel);
        scope.AbortPreparation();
        Assert.False(scope.Enabled);
        Assert.False(scope.OwnsBody(body));
        Assert.False(observed.Owned);
        Assert.True(scope.Allows(observed, null, null, null, null, false));
        Assert.Null(scope.BeginHide(observed));
        Assert.Empty(scope.RetireWorld(world));
    }

    [Fact]
    public void ChangedPanelDuringPreparationRefusesWithoutTakingOrdinaryBody()
    {
        var scope = new MagazineCheckScope(); var body = new object();
        scope.Prepare(new object(), new object(), body, new object());
        Assert.Throws<InvalidOperationException>(() => scope.CompletePreparation(new object()));
        Assert.False(scope.Enabled);
        Assert.False(scope.OwnsBody(body));
        Assert.Empty(scope.ActivePanels);
        scope.AbortPreparation();
    }

    [Fact]
    public void FirstPanelAppearingDuringReadOnlyPreparationCanBeAdoptedOnCommit()
    {
        var scope = new MagazineCheckScope(); var game = new object(); var world = new object(); var body = new object();
        scope.Prepare(game, world, body, null);
        var panel = new object();
        scope.CompletePreparation(panel);
        Assert.True(scope.Find(panel).Owned);
        Assert.True(scope.Frozen);
        Assert.False(scope.Allows(scope.Find(panel), game, world, body, body, true));
    }

    [Fact]
    public void ObservedForeignPanelCannotPartiallyCommitBodyAuthority()
    {
        var scope = new MagazineCheckScope(); var game = new object(); var world = new object(); var body = new object();
        var panel = new object(); var ordinary = scope.Observe(panel, new object(), game, world);
        scope.Prepare(game, world, body, panel);
        Assert.Throws<InvalidOperationException>(() => scope.CompletePreparation(panel));
        Assert.False(scope.Enabled);
        Assert.False(scope.OwnsBody(body));
        Assert.False(ordinary.Owned);
        Assert.Empty(scope.ActivePanels);
        scope.AbortPreparation();
    }

    [Fact]
    public void ConfirmedReplacementRetiresOnlyOwnedPanelAndAllowsNewPhysicalBody()
    {
        var scope = new MagazineCheckScope(); var game = new object(); var world = new object();
        var old = new object(); var body = new object(); var panel = new object();
        var ordinary = scope.Observe(new object(), new object(), game, world);
        scope.Prepare(game, world, old, panel); scope.CompletePreparation(panel);
        Assert.True(scope.Frozen);
        var retired = Assert.Single(scope.Bind(body, game, world, true));
        Assert.Same(panel, retired.Instance);
        Assert.True(retired.Retired);
        Assert.False(scope.Retire(ordinary));
        Assert.False(scope.Allows(retired, game, world, body, body, true));
        Assert.False(scope.AllowsBody(old, game, world, body, body, true));
        Assert.True(scope.AllowsBody(body, game, world, body, body, true));
        var fresh = scope.Adopt(new object(), body, game, world);
        Assert.True(scope.Allows(fresh, game, world, body, body, true));
        Assert.True(scope.Allows(ordinary, null, null, null, null, false));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void InvalidCandidateCannotRetireCurrentPanel(bool authorized, bool differentWorld)
    {
        var scope = new MagazineCheckScope(); var game = new object(); var world = new object(); var body = new object();
        var panel = new object(); scope.Prepare(game, world, body, panel); scope.CompletePreparation(panel);
        Assert.Throws<InvalidOperationException>(() => scope.Bind(new object(), game, differentWorld ? new object() : world, authorized));
        Assert.False(scope.Find(panel).Retired);
        Assert.Same(body, scope.Body);
        Assert.True(scope.Frozen);
    }

    [Fact]
    public void CancelledRetryPreservesExistingPanelAndItsCurrentHideTicket()
    {
        var setup = Current(); var scope = setup.Scope;
        var ticket = scope.BeginHide(setup.Panel);
        scope.Prepare(setup.Game, setup.World, setup.Body, setup.Panel.Instance);
        scope.AbortPreparation();
        Assert.Same(setup.Panel, ticket.Current());
        Assert.False(scope.Frozen);
        Assert.True(scope.Allows(setup.Panel, setup.Game, setup.World, setup.Body, setup.Body, true));
    }

    [Fact]
    public void LaterShowOrHideInvalidatesAlreadyQueuedFinishWithoutBlockingFreshFinish()
    {
        var setup = Current(); var scope = setup.Scope;
        var first = scope.BeginHide(setup.Panel);
        Assert.Same(setup.Panel, first.Current());
        scope.Shown(setup.Panel);
        Assert.Null(first.Current());
        var second = scope.BeginHide(setup.Panel);
        var third = scope.BeginHide(setup.Panel);
        Assert.Null(second.Current());
        Assert.Same(setup.Panel, third.Current());
        scope.Retire(setup.Panel);
        Assert.Null(third.Current());
        Assert.Null(scope.BeginHide(setup.Panel));
    }

    [Fact]
    public void FailedSwapRecoveredToSameBodyStillRetiresOldPanelAndOldCallbackGeneration()
    {
        var setup = Current(); var scope = setup.Scope;
        var ticket = scope.BeginHide(setup.Panel);
        scope.Prepare(setup.Game, setup.World, setup.Body, setup.Panel.Instance);
        scope.CompletePreparation(setup.Panel.Instance);
        Assert.False(scope.Allows(setup.Panel, setup.Game, setup.World, setup.Body, setup.Body, true));
        Assert.Single(scope.Bind(setup.Body, setup.Game, setup.World, true));
        Assert.Null(ticket.Current());
        Assert.Throws<InvalidOperationException>(() => scope.Adopt(setup.Panel.Instance, setup.Body, setup.Game, setup.World));
        var fresh = scope.Adopt(new object(), setup.Body, setup.Game, setup.World);
        Assert.True(scope.Allows(fresh, setup.Game, setup.World, setup.Body, setup.Body, true));
    }

    [Fact]
    public void OldWorldTeardownCannotRetireNewWorldOrItsPendingCallbacks()
    {
        var setup = Current(); var scope = setup.Scope; var oldWorld = setup.World;
        var oldTicket = scope.BeginHide(setup.Panel);
        Assert.Single(scope.RetireWorld(oldWorld));
        Assert.Null(oldTicket.Current());
        Assert.False(scope.Enabled);
        var game = new object(); var world = new object(); var old = new object(); var body = new object();
        scope.Prepare(game, world, old, null); scope.CompletePreparation(null); scope.Bind(body, game, world, true);
        var panel = scope.Adopt(new object(), body, game, world); var ticket = scope.BeginHide(panel);
        Assert.Empty(scope.RetireWorld(oldWorld));
        Assert.Same(panel, ticket.Current());
        Assert.True(scope.Allows(panel, game, world, body, body, true));
    }

    [Fact]
    public void MatchingProfileValueOrOneCurrentReferenceDoesNotGrantOwnership()
    {
        var setup = Current(); var scope = setup.Scope;
        var impostor = new object();
        Assert.False(scope.Allows(setup.Panel, setup.Game, setup.World, impostor, setup.Body, true));
        Assert.False(scope.Allows(setup.Panel, setup.Game, setup.World, setup.Body, impostor, true));
        Assert.False(scope.Allows(setup.Panel, setup.Game, setup.World, setup.Body, setup.Body, false));
        Assert.False(scope.Allows(setup.Panel, new object(), setup.World, setup.Body, setup.Body, true));
        var a = new SameValue(); var b = new SameValue(); var other = new MagazineCheckScope();
        other.Prepare(setup.Game, setup.World, a, null); other.CompletePreparation(null);
        Assert.False(other.OwnsBody(b));
    }

    [Fact]
    public void PanelCannotBeReassignedToAnotherBodyOrWorld()
    {
        var setup = Current(); var scope = setup.Scope;
        Assert.Throws<InvalidOperationException>(() => scope.Observe(setup.Panel.Instance, new object(), setup.Game, setup.World));
        Assert.Throws<InvalidOperationException>(() => scope.Observe(setup.Panel.Instance, setup.Body, setup.Game, new object()));
        Assert.Throws<InvalidOperationException>(() => scope.Adopt(new object(), new object(), setup.Game, setup.World));
        Assert.False(setup.Panel.Retired);
    }

    [Fact]
    public void RetiredPanelAndBodyAreNotKeptAliveByWeakCompletionTicket()
    {
        var scope = new MagazineCheckScope(); var values = Retire(scope);
        for (var i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        Assert.False(values.Panel.IsAlive);
        Assert.False(values.Body.IsAlive);
        Assert.Null(values.Ticket.Current());
        GC.KeepAlive(scope);
    }

    private static (MagazineCheckScope Scope, object Game, object World, object Body, MagazineCheckScope.Panel Panel) Current()
    {
        var scope = new MagazineCheckScope(); var game = new object(); var world = new object(); var body = new object();
        scope.Prepare(game, world, new object(), null); scope.CompletePreparation(null); scope.Bind(body, game, world, true);
        return (scope, game, world, body, scope.Adopt(new object(), body, game, world));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Panel, WeakReference Body, MagazineCheckScope.Ticket Ticket) Retire(MagazineCheckScope scope)
    {
        var game = new object(); var world = new object(); var body = new object(); var panel = new object();
        scope.Prepare(game, world, new object(), null); scope.CompletePreparation(null); scope.Bind(body, game, world, true);
        var entry = scope.Adopt(panel, body, game, world); var ticket = scope.BeginHide(entry);
        scope.RetireWorld(world);
        return (new WeakReference(panel), new WeakReference(body), ticket);
    }
    private sealed class SameValue
    {
        public override bool Equals(object obj) => obj is SameValue;
        public override int GetHashCode() => 1;
    }
}

using System;
using Redeploy.Core.Compat;
using Xunit;

namespace Redeploy.Tests;

public class SimpleCrosshairScopeTests
{
    private sealed class Body { public string ProfileId = "same-profile"; }
    [Fact]
    public void ReadOnlyPreparationAndCancellationNeverAdoptOrdinaryObjects()
    {
        var s = new SimpleCrosshairScope(); var body = new Body(); var panel = new object();
        s.Prepare(new object(), new object(), body, panel); s.CompletePreparation(panel);
        Assert.False(s.Enabled); Assert.False(s.OwnsBody(body)); Assert.False(s.OwnsPanel(panel));
        Assert.True(s.AllowsPanel(panel, null, null, null, null, false));
        s.AbortPreparation(); Assert.False(s.Start(body)); Assert.Null(s.PreparedBody);
    }
    [Fact]
    public void ChangedPanelRefusesBeforeTakingAuthority()
    {
        var s = new SimpleCrosshairScope(); var body = new Body(); var panel = new object();
        s.Prepare(new object(), new object(), body, panel);
        Assert.Throws<InvalidOperationException>(() => s.CompletePreparation(new object()));
        Assert.False(s.Enabled); Assert.False(s.OwnsBody(body)); Assert.False(s.OwnsPanel(panel));
        s.AbortPreparation();
    }
    [Fact]
    public void WrongPhysicalBodyCannotStartPreparedHandoverEvenWithSameProfile()
    {
        var s = new SimpleCrosshairScope(); var body = new Body(); var duplicate = new Body();
        s.Prepare(new object(), new object(), body, null); s.CompletePreparation(null);
        Assert.Equal(body.ProfileId, duplicate.ProfileId);
        Assert.False(s.Start(duplicate)); Assert.False(s.OwnsBody(duplicate));
        Assert.Same(body, s.PreparedBody); Assert.True(s.Start(body));
        Assert.True(s.Frozen); Assert.True(s.OwnsBody(body));
    }
    [Fact]
    public void ReplacementKeepsOnePanelAndRetiresOldBodyCallbacks()
    {
        var (s, game, world, old, panel) = Frozen(); var body = new Body();
        Assert.False(s.AllowsPanel(panel, game, world, old, old, true));
        s.Bind(game, world, body, panel, true);
        Assert.Same(panel, s.Panel); Assert.Same(body, s.Body);
        Assert.False(s.Frozen); Assert.True(s.OwnsBody(old));
        Assert.False(s.AllowsBody(old, game, world, body, body, true));
        Assert.True(s.AllowsBody(body, game, world, body, body, true));
        Assert.True(s.AllowsPanel(panel, game, world, body, body, true));
        Assert.False(s.Freeze(old)); Assert.False(s.Frozen);
    }
    [Fact]
    public void AuthoritativeWorldPlayerCameraAndGameAreAllRequiredForOwnedCallbacks()
    {
        var (s, game, world, old, panel) = Frozen(); var body = new Body(); s.Bind(game, world, body, panel, true);
        Assert.False(s.AllowsBody(body, game, world, body, old, true));
        Assert.False(s.AllowsBody(body, game, world, old, body, true));
        Assert.False(s.AllowsBody(body, new object(), world, body, body, true));
        Assert.False(s.AllowsBody(body, game, new object(), body, body, true));
        Assert.False(s.AllowsPanel(panel, game, world, body, body, false));
        Assert.True(s.AllowsBody(new Body(), null, null, null, null, false));
        Assert.True(s.AllowsPanel(new object(), null, null, null, null, false));
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void InvalidBindCannotRetireTheCurrentBody(bool authoritative, bool wrongWorld)
    {
        var (s, game, world, old, panel) = Frozen(); var candidate = new Body();
        Assert.Throws<InvalidOperationException>(() => s.Bind(game, wrongWorld ? new object() : world, candidate, panel, authoritative));
        Assert.Same(old, s.Body); Assert.Same(panel, s.Panel); Assert.True(s.Frozen);
        Assert.False(s.OwnsBody(candidate));
    }
    [Fact]
    public void ActualSameBodyRecoveryCanRebindAfterFailedConstruction()
    {
        var (s, game, world, body, panel) = Frozen();
        s.Bind(game, world, body, panel, true);
        Assert.False(s.Frozen); Assert.True(s.AllowsBody(body, game, world, body, body, true));
        Assert.True(s.Freeze(body)); Assert.False(s.AllowsPanel(panel, game, world, body, body, true));
        s.Bind(game, world, body, panel, true);
        Assert.True(s.AllowsPanel(panel, game, world, body, body, true));
    }
    [Fact]
    public void CancellingRetryPreservesCurrentBodyAndRetiredBodyFences()
    {
        var (s, game, world, old, panel) = Frozen(); var body = new Body(); s.Bind(game, world, body, panel, true);
        s.Prepare(game, world, body, panel); s.CompletePreparation(panel); s.AbortPreparation();
        Assert.False(s.Frozen); Assert.Same(body, s.Body);
        Assert.True(s.AllowsPanel(panel, game, world, body, body, true));
        Assert.False(s.AllowsBody(old, game, world, body, body, true));
        Assert.False(s.Start(body));
    }
    [Fact]
    public void NewPanelRetiresOnlyThePreviousOwnedPanel()
    {
        var (s, game, world, old, panel) = Frozen(); var body = new Body(); var fresh = new object();
        s.Bind(game, world, body, fresh, true);
        Assert.False(s.AllowsPanel(panel, game, world, body, body, true));
        Assert.True(s.AllowsPanel(fresh, game, world, body, body, true));
        Assert.True(s.AllowsPanel(new object(), null, null, null, null, false));
    }
    [Fact]
    public void OldWorldTeardownNeverTouchesTheNewWorld()
    {
        var (s, game, world, old, panel) = Frozen(); s.Bind(game, world, old, panel, true);
        Assert.True(s.RetireWorld(world)); Assert.False(s.Enabled);
        Assert.False(s.AllowsBody(old, game, world, old, old, true));
        Assert.False(s.AllowsPanel(panel, game, world, old, old, true));
        var secondGame = new object(); var secondWorld = new object(); var secondBody = new Body(); var secondPanel = new object();
        s.Prepare(secondGame, secondWorld, secondBody, secondPanel); s.CompletePreparation(secondPanel); s.Start(secondBody);
        s.Bind(secondGame, secondWorld, secondBody, secondPanel, true);
        Assert.False(s.RetireWorld(world)); Assert.Same(secondWorld, s.World);
        Assert.True(s.AllowsPanel(secondPanel, secondGame, secondWorld, secondBody, secondBody, true));
        Assert.False(s.AllowsPanel(panel, secondGame, secondWorld, secondBody, secondBody, true));
    }
    [Fact]
    public void OldWorldPanelCannotBeBorrowedOrPartiallyCommitANewBody()
    {
        var (s, game, world, old, oldPanel) = Frozen(); s.RetireWorld(world);
        var freshGame = new object(); var freshWorld = new object(); var body = new Body(); var panel = new object();
        s.Prepare(freshGame, freshWorld, body, panel); s.CompletePreparation(panel); s.Start(body);
        var candidate = new Body();
        Assert.Throws<InvalidOperationException>(() => s.Bind(freshGame, freshWorld, candidate, oldPanel, true));
        Assert.Same(body, s.Body); Assert.Same(panel, s.Panel); Assert.True(s.Frozen);
        Assert.False(s.OwnsBody(candidate));
        Assert.Throws<InvalidOperationException>(() => s.AdoptPanel(oldPanel));
        Assert.Same(panel, s.Panel);
    }
    [Fact]
    public void OrdinaryOverlayAbsentAtPrepareCanAppearDuringConstruction()
    {
        var s = new SimpleCrosshairScope(); var game = new object(); var world = new object(); var old = new Body();
        s.Prepare(game, world, old, null); s.CompletePreparation(null); s.Start(old);
        var panel = new object(); s.AdoptPanel(panel);
        Assert.False(s.AllowsPanel(panel, game, world, old, old, true));
        var body = new Body(); s.Bind(game, world, body, panel, true);
        Assert.True(s.AllowsPanel(panel, game, world, body, body, true));
    }
    private static (SimpleCrosshairScope, object, object, Body, object) Frozen()
    {
        var s = new SimpleCrosshairScope(); var game = new object(); var world = new object(); var body = new Body(); var panel = new object();
        s.Prepare(game, world, body, panel); s.CompletePreparation(panel); Assert.True(s.Start(body));
        return (s, game, world, body, panel);
    }
}

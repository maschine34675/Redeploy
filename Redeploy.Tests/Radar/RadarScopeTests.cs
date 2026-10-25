using Redeploy.Core.Compat;
using Xunit;

namespace Redeploy.Tests;

public sealed class RadarScopeTests
{
    private readonly object _game = new(), _world = new(), _body = new(), _hud = new(), _manager = new();
    private RadarScope Owned()
    {
        var scope = new RadarScope();
        scope.Prepare(_game, _world, _body, true);
        scope.Adopt(_hud, _manager, _game, _world, _body, true);
        scope.Bind(_game, _world, _body, true);
        return scope;
    }
    [Fact]
    public void MerelyKnowingManagerDoesNotGrantAuthority()
    {
        var scope = new RadarScope();
        Assert.False(scope.Enabled);
        Assert.False(scope.OwnsManager(_manager));
        Assert.False(scope.OwnsHud(_hud));
        Assert.Throws<InvalidOperationException>(() => scope.Prepare(_game, _world, _body, false));
        Assert.Throws<InvalidOperationException>(() => scope.Adopt(_hud, _manager, _game, _world, _body, true));
        Assert.False(scope.Enabled);
    }
    [Fact]
    public void InitialBodySwapCanPrecedeRadarCreation()
    {
        var scope = new RadarScope(); var replacement = new object();
        scope.Prepare(_game, _world, _body, true);
        Assert.True(scope.Frozen);
        Assert.Null(scope.Hud);
        scope.Bind(_game, _world, replacement, true);
        scope.Adopt(_hud, _manager, _game, _world, replacement, true);
        Assert.Same(replacement, scope.Body);
        Assert.Same(_hud, scope.Hud);
        Assert.True(scope.ActiveManager(_manager));
        Assert.False(scope.Frozen);
    }
    [Fact]
    public void FailedFirstPreparationRestoresUnownedOrdinaryWorld()
    {
        var scope = new RadarScope();
        scope.Prepare(_game, _world, _body, true);
        scope.Adopt(_hud, _manager, _game, _world, _body, true);
        scope.AbortPreparation();
        Assert.False(scope.Enabled);
        Assert.False(scope.OwnsManager(_manager));
        Assert.Null(scope.Hud);
    }
    [Fact]
    public void FailedLaterPreparationPreservesPriorSuccessfulOwnership()
    {
        var scope = Owned();
        scope.Prepare(_game, _world, _body, true);
        Assert.True(scope.Frozen);
        scope.AbortPreparation();
        Assert.Same(_body, scope.Body);
        Assert.Same(_hud, scope.Hud);
        Assert.True(scope.ActiveManager(_manager));
        Assert.False(scope.Frozen);
    }
    [Fact]
    public void BodyReplacementKeepsOneHudAndDoesNotAliasPhysicalBodies()
    {
        var scope = Owned(); var replacement = new object();
        scope.Prepare(_game, _world, _body, true);
        scope.Freeze(_game, _world, replacement, true);
        scope.Bind(_game, _world, replacement, true);
        Assert.Same(_hud, scope.Hud);
        Assert.Same(_manager, scope.Manager);
        Assert.Same(replacement, scope.Body);
        Assert.Throws<InvalidOperationException>(() => scope.Prepare(_game, _world, _body, true));
        Assert.True(RadarScope.ReplaceContact(_body, replacement));
        Assert.False(RadarScope.ReplaceContact(replacement, replacement));
    }
    [Fact]
    public void SameBodyCameraChangeCanFreezeAndResumeWithoutRebuilding()
    {
        var scope = Owned();
        scope.Freeze(_game, _world, _body, true);
        Assert.True(scope.Frozen);
        scope.Freeze(_game, _world, _body, true);
        scope.Bind(_game, _world, _body, true);
        Assert.Same(_hud, scope.Hud);
        Assert.False(scope.Frozen);
    }
    [Fact]
    public void DifferentWorldOrMissingAuthorityCannotCommitCandidate()
    {
        var scope = Owned(); var replacement = new object();
        Assert.Throws<InvalidOperationException>(() => scope.Freeze(_game, _world, replacement, false));
        Assert.Throws<InvalidOperationException>(() => scope.Bind(_game, new object(), replacement, true));
        Assert.Throws<InvalidOperationException>(() => scope.Bind(new object(), _world, replacement, true));
        Assert.Throws<InvalidOperationException>(() => scope.Bind(_game, _world, replacement, false));
        Assert.Same(_body, scope.Body);
        Assert.False(scope.Frozen);
    }
    [Fact]
    public void RetiredManagerCannotControlNewWorldOrBeReAdopted()
    {
        var scope = Owned(); var nextWorld = new object(); var nextBody = new object();
        var nextHud = new object(); var nextManager = new object();
        scope.RetireWorld(_world);
        Assert.True(scope.OwnsManager(_manager)); // Late callback remains fenced.
        Assert.True(scope.OwnsHud(_hud));
        Assert.False(scope.ActiveHud(_hud));
        Assert.False(scope.ActiveManager(_manager));
        scope.Prepare(_game, nextWorld, nextBody, true);
        Assert.Throws<InvalidOperationException>(() => scope.Adopt(nextHud, _manager, _game, nextWorld, nextBody, true));
        scope.Adopt(nextHud, nextManager, _game, nextWorld, nextBody, true);
        scope.Bind(_game, nextWorld, nextBody, true);
        scope.RetireWorld(_world); // Late old-world end must not clear this lease.
        Assert.True(scope.ActiveManager(nextManager));
        Assert.False(scope.ActiveManager(_manager));
        Assert.Same(nextWorld, scope.World);
    }
    [Fact]
    public void DuplicateOrForeignHudAdoptionDoesNotReplaceTheLease()
    {
        var scope = Owned();
        Assert.Throws<InvalidOperationException>(() => scope.Adopt(new object(), _manager, _game, _world, _body, true));
        Assert.Throws<InvalidOperationException>(() => scope.Adopt(_hud, new object(), _game, _world, _body, true));
        Assert.Throws<InvalidOperationException>(() => scope.Adopt(_hud, _manager, _game, _world, new object(), true));
        Assert.Same(_hud, scope.Hud);
        Assert.Same(_manager, scope.Manager);
    }
    [Fact]
    public void DuplicatePrepareCannotOverwriteRollbackSnapshot()
    {
        var scope = new RadarScope();
        scope.Prepare(_game, _world, _body, true);
        Assert.Throws<InvalidOperationException>(() => scope.Prepare(_game, _world, _body, true));
        scope.AbortPreparation();
        Assert.False(scope.Enabled);
    }
}

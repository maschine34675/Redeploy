using Redeploy.Core.Compat;
using Xunit;

namespace Redeploy.Tests;

public sealed class EnemyMarkersTests
{
    [Fact]
    public void PreparationIsReadOnlyAndCancellationDoesNotAcquireMarkers()
    {
        var scope = new EnemyMarkersScope(); var game = new object(); var world = new object(); var body = new object(); var component = new object();
        scope.Prepare(game, world, body, true);
        Assert.False(scope.Enabled);
        Assert.Throws<InvalidOperationException>(() => scope.AdoptComponent(component, game, world, true));
        scope.AbortPreparation();
        Assert.False(scope.Start(game, world, body, true));
        Assert.True(scope.AllowsPerson(component, false, true));
    }

    [Fact]
    public void ActivationRequiresExactPhysicalWorldGameAndBody()
    {
        var scope = new EnemyMarkersScope(); var game = new object(); var world = new object(); var body = new object();
        Assert.Throws<InvalidOperationException>(() => scope.Prepare(game, world, body, false));
        scope.Prepare(game, world, body, true);
        Assert.False(scope.Start(new object(), world, body, true));
        Assert.False(scope.Start(game, new object(), body, true));
        Assert.False(scope.Start(game, world, new object(), true));
        Assert.False(scope.Start(game, world, body, false));
        Assert.True(scope.Start(game, world, body, true));
    }

    [Fact]
    public void LocalBodyAndCameraGatesPreserveBotsAndUnownedObjects()
    {
        var scope = new EnemyMarkersScope(); var game = new object(); var world = new object(); var body = new object(); var component = new object(); var billboard = new object();
        scope.Prepare(game, world, body, true); scope.Start(game, world, body, true);
        scope.AdoptComponent(component, game, world, true); scope.AdoptBillboard(component, billboard);
        Assert.False(scope.AllowsPerson(component, true, true));
        Assert.True(scope.AllowsPerson(component, true, false));
        Assert.False(scope.AllowsPerson(component, false, false));
        Assert.True(scope.AllowsPerson(new object(), false, true));
        Assert.False(scope.AllowsBillboard(billboard, true, false));
        Assert.False(scope.AllowsBillboard(billboard, false, true));
        Assert.True(scope.AllowsBillboard(billboard, true, true));
        Assert.True(scope.AllowsBillboard(new object(), false, false));
    }

    [Fact]
    public void FailedRetryKeepsPriorWorldAndItsPersistentMarkers()
    {
        var scope = new EnemyMarkersScope(); var game = new object(); var world = new object(); var body = new object(); var component = new object(); var billboard = new object();
        scope.Prepare(game, world, body, true); scope.Start(game, world, body, true);
        scope.AdoptComponent(component, game, world, true); scope.AdoptBillboard(component, billboard);
        scope.Prepare(game, world, new object(), true); scope.AbortPreparation();
        Assert.Same(world, scope.World); Assert.True(scope.ActiveComponent(component)); Assert.True(scope.ActiveBillboard(billboard));
        Assert.True(scope.AllowsBillboard(billboard, true, true));
    }

    [Fact]
    public void RetiredCallbacksStayFencedAcrossNewWorldAndLateOldRetirement()
    {
        var scope = new EnemyMarkersScope(); var game = new object(); var world = new object(); var body = new object(); var component = new object(); var billboard = new object();
        scope.Prepare(game, world, body, true); scope.Start(game, world, body, true);
        scope.AdoptComponent(component, game, world, true); scope.AdoptBillboard(component, billboard);
        scope.RetireWorld(world);
        Assert.True(scope.OwnsComponent(component)); Assert.True(scope.OwnsBillboard(billboard));
        Assert.False(scope.AllowsPerson(component, true, false)); Assert.False(scope.AllowsBillboard(billboard, true, true));
        var nextWorld = new object(); var nextGame = new object(); var nextBody = new object(); var nextComponent = new object();
        scope.Prepare(nextGame, nextWorld, nextBody, true); scope.Start(nextGame, nextWorld, nextBody, true);
        scope.AdoptComponent(nextComponent, nextGame, nextWorld, true);
        Assert.Throws<InvalidOperationException>(() => scope.AdoptComponent(component, nextGame, nextWorld, true));
        Assert.Throws<InvalidOperationException>(() => scope.AdoptBillboard(nextComponent, billboard));
        scope.RetireWorld(world);
        Assert.Same(nextWorld, scope.World); Assert.True(scope.ActiveComponent(nextComponent));
    }
}

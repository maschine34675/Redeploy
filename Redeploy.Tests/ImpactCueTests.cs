using Redeploy.Core.Compat;
using Xunit;

namespace Redeploy.Tests;

public sealed class ImpactCueTests
{
    [Fact]
    public void PreparationAndCancellationDoNotOwnAnOrdinaryRaid()
    {
        var scope = new ImpactCueScope(); var game = new object(); var world = new object(); var body = new object();
        scope.Prepare(game, world, body);
        Assert.False(scope.Enabled); Assert.False(scope.OwnsBody(body));
        Assert.False(scope.Start(new object(), world, body));
        Assert.False(scope.Start(game, new object(), body));
        Assert.False(scope.Start(game, world, new object()));
        scope.AbortPreparation();
        Assert.False(scope.Start(game, world, body));
        Assert.True(scope.Allows(body, null, null, null, null, false));
    }

    [Fact]
    public void ExactCurrentPhysicalBodyWinsOverRetainedCorpseAndReusedProfile()
    {
        var scope = new ImpactCueScope(); var game = new object(); var world = new object(); var old = new object(); var current = new object();
        scope.Prepare(game, world, old); Assert.True(scope.Start(game, world, old));
        Assert.False(scope.Allows(old, game, world, old, old, true));
        scope.Bind(game, world, current, true);
        Assert.False(scope.Allows(old, game, world, current, current, true));
        Assert.True(scope.Allows(current, game, world, current, current, true));
        Assert.False(scope.Allows(current, game, world, old, current, true));
        Assert.False(scope.Allows(current, new object(), world, current, current, true));
        Assert.False(scope.Allows(current, game, new object(), current, current, true));
        Assert.False(scope.Allows(current, game, world, current, current, false));
    }

    [Fact]
    public void FailedReplacementMayRecoverTheSameOldBodyWithoutRetiringItsSuccessor()
    {
        var scope = new ImpactCueScope(); var game = new object(); var world = new object(); var body = new object();
        scope.Prepare(game, world, body); scope.Start(game, world, body);
        Assert.Throws<InvalidOperationException>(() => scope.Bind(game, world, body, false));
        scope.Bind(game, world, body, true);
        Assert.True(scope.Allows(body, game, world, body, body, true));
        scope.Prepare(game, world, body); scope.AbortPreparation();
        Assert.True(scope.Allows(body, game, world, body, body, true));
    }

    [Fact]
    public void TombstonesFenceDelayedCallbacksAcrossRaidsAndLateDisposalCannotRetireNewRaid()
    {
        var scope = new ImpactCueScope(); var game = new object(); var world = new object(); var old = new object();
        scope.Prepare(game, world, old); scope.Start(game, world, old); scope.Bind(game, world, old, true);
        scope.RetireWorld(world);
        Assert.False(scope.Allows(old, game, world, old, old, true));
        var game2 = new object(); var world2 = new object(); var current = new object();
        Assert.Throws<InvalidOperationException>(() => scope.Prepare(game2, world2, old));
        scope.Prepare(game2, world2, current); scope.Start(game2, world2, current); scope.Bind(game2, world2, current, true);
        scope.RetireWorld(world);
        Assert.True(scope.Allows(current, game2, world2, current, current, true));
        Assert.False(scope.Allows(old, game2, world2, current, current, true));
    }
}

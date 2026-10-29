using Redeploy.Core.Compat;
using Xunit;

namespace Redeploy.Tests;

public sealed class ReplacementProfileRuleTests
{
    private static readonly object Pmc = new(), Scav = new(), Stranger = new();

    [Fact]
    public void AnOrdinaryRespawnBodyCarriesTheGamesProfile()
        => Assert.True(ReplacementProfileRule.CarriesRaidProfile(Pmc, Pmc, Pmc));

    [Fact]
    public void AnOwnScavBodyCarriesTheReplacementProfileWhileTheGameStillHoldsThePmcProfile()
    {
        // RespawnEngine creates the body with the replacement profile and commits game.Profile afterwards.
        Assert.True(ReplacementProfileRule.CarriesRaidProfile(Scav, Pmc, Scav));
    }

    [Fact]
    public void AnOwnScavBodyStillQualifiesOnceTheGameCommittedTheProfile()
        => Assert.True(ReplacementProfileRule.CarriesRaidProfile(Scav, Scav, Scav));

    [Fact]
    public void AnUnrelatedProfileNeverQualifiesDuringAReplacement()
    {
        Assert.False(ReplacementProfileRule.CarriesRaidProfile(Stranger, Pmc, Scav));
        Assert.False(ReplacementProfileRule.CarriesRaidProfile(Stranger, Pmc, Pmc));
    }

    [Fact]
    public void WithoutAReplacementOnlyTheGamesProfileQualifies()
    {
        Assert.True(ReplacementProfileRule.CarriesRaidProfile(Pmc, Pmc, null));
        Assert.False(ReplacementProfileRule.CarriesRaidProfile(Scav, Pmc, null));
    }

    [Fact]
    public void ANullBodyProfileNeverQualifiesEvenWhenTheOtherSidesAreNull()
    {
        Assert.False(ReplacementProfileRule.CarriesRaidProfile(null, null, null));
        Assert.False(ReplacementProfileRule.CarriesRaidProfile(null, Pmc, Scav));
    }
}

using Redeploy.Server.Scav;
using SPTarkov.Server.Core.Models.Eft.Match;
using Xunit;

namespace Redeploy.Server.Tests;

public sealed partial class ScavContinuationTests
{
    private static ScavProbeRequest ProbeFor(string raid) => new() { InitialServerId = raid };

    [Fact] public async Task ProbeAcceptsFreshIssuedRaidWithoutReservingOrWriting()
    {
        using var f = new Fixture();
        f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = f.Raid });
        string before = f.Json.Serialize(f.Full)!;

        var probe = await f.Service.Probe(f.Session, ProbeFor(f.Raid), default);

        Assert.True(probe.Success, probe.Error);
        Assert.Null(probe.Token); Assert.Null(probe.ScavProfile);
        Assert.Null(f.Journal.Read(f.Session.ToString(), f.Raid));
        Assert.Empty(f.Journal.ForSession(f.Session.ToString()));
        Assert.Equal(before, f.Json.Serialize(f.Full)); Assert.Equal(0, f.Writes);
        // The probe is not a reservation: a real preparation still succeeds afterwards.
        Assert.True((await f.Service.Prepare(f.Session, f.PrepareRequest(), default)).Success);
    }

    [Theory]
    [InlineData("unissued", "no-issued-pmc-raid")]
    [InlineData("wrong-identity", "no-issued-pmc-raid")]
    [InlineData("transit-arrival", "no-issued-pmc-raid")]
    [InlineData("incoming-transition", "no-issued-pmc-raid")]
    [InlineData("cooldown", "scav-cooldown")]
    [InlineData("scav-unavailable", "regular-scav-unavailable")]
    [InlineData("pending-continuation", "previous-continuation-pending")]
    [InlineData("same-raid-reserved", "continuation-already-prepared")]
    [InlineData("foreign-server-id", "invalid-prepare-request")]
    public async Task ProbeRejectsExactlyWhatPrepareWouldRejectAndChangesNothing(string state, string expected)
    {
        using var f = new Fixture();
        string raid = f.Raid;
        switch (state)
        {
            case "unissued": break;
            case "wrong-identity":
                f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = "factory4_day.pmc 123456790" });
                break;
            case "transit-arrival":
                // SPT's StartLocalRaid gives an arrival from another map a nonzero count and history.
                f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new()
                    { ServerId = raid, Transition = new() { TransitionCount = 1, VisitedLocations = ["bigmap"] } });
                break;
            case "incoming-transition":
                f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc", Transition = new() { TransitionCount = 0, VisitedLocations = [] } },
                    new() { ServerId = raid });
                break;
            case "cooldown":
                f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = raid });
                f.Full.CharacterData!.ScavData!.Info!.SavageLockTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
                break;
            case "scav-unavailable":
                f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = raid });
                f.Full.CharacterData!.ScavData!.Info!.SavageLockTime = null;
                break;
            case "pending-continuation":
                f.Journal.Write(f.Entry());
                raid = "factory4_day.pmc 123456790";
                f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = raid });
                break;
            case "same-raid-reserved":
                f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = raid });
                Assert.True((await f.Service.Prepare(f.Session, f.PrepareRequest(), default)).Success);
                break;
            case "foreign-server-id":
                raid = "factory4_day.Savage 123456789";
                break;
            default: throw new ArgumentOutOfRangeException(nameof(state));
        }
        int journalBefore = f.Journal.ForSession(f.Session.ToString()).Count();
        string profileBefore = f.Json.Serialize(f.Full)!;

        var probe = await f.Service.Probe(f.Session, ProbeFor(raid), default);

        Assert.False(probe.Success); Assert.Equal(expected, probe.Error);
        Assert.Equal(journalBefore, f.Journal.ForSession(f.Session.ToString()).Count());
        Assert.Equal(profileBefore, f.Json.Serialize(f.Full)); Assert.Equal(0, f.Writes);
        if (state == "cooldown")
            Assert.Equal(f.Full.CharacterData!.ScavData!.Info!.SavageLockTime, probe.AvailableAt);
        // Whatever the probe refuses, a real preparation for the same raid refuses with the same code
        // (except the two probe-only guards that Prepare answers earlier in its own request validation).
        if (state is not ("foreign-server-id" or "same-raid-reserved"))
        {
            var request = f.PrepareRequest(); request.InitialServerId = raid;
            var prepare = await f.Service.Prepare(f.Session, request, default);
            Assert.False(prepare.Success); Assert.Equal(expected, prepare.Error);
        }
    }

    [Fact] public async Task ProbeFailsClosedWhenTheAccountProfileIsMissing()
    {
        using var f = new Fixture();
        f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = f.Raid });
        f.Full.CharacterData!.ScavData = null;
        var probe = await f.Service.Probe(f.Session, ProbeFor(f.Raid), default);
        Assert.False(probe.Success); Assert.Equal("regular-scav-unavailable", probe.Error);
    }
}

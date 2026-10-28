using System.Reflection;
using Redeploy.Server.Scav;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.InRaid;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using SPTarkov.Server.Core.Utils.Json;
using Xunit;
using Path = System.IO.Path;
using ServerCloner = SPTarkov.Server.Core.Utils.Cloners.FastCloner;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Redeploy.Server.Tests;

public sealed class ScavContinuationTests
{
    private static readonly MongoId Template = new("55d7217a4bdc2d86028b456d");
    private static Item Item(string? parent = null, string? slot = null) => new()
        { Id = new MongoId(), Template = Template, ParentId = parent, SlotId = slot };
    private static PmcData Profile(string side = "Savage")
    {
        var root = Item();
        return new PmcData
        {
            Id = new MongoId(), Info = new() { Side = side, Experience = 123, SavageLockTime = 0 }, InsuredItems = [],
            Inventory = new() { Equipment = root.Id, Items = [root] }
        };
    }

    [Theory]
    [InlineData(ExitStatus.SURVIVED, true)] [InlineData(ExitStatus.RUNNER, true)]
    [InlineData(ExitStatus.KILLED, false)] [InlineData(ExitStatus.MISSINGINACTION, false)]
    [InlineData(ExitStatus.LEFT, false)] [InlineData(ExitStatus.TRANSIT, false)]
    public void OnlySuccessfulExitsDeliverLoot(ExitStatus status, bool expected)
        => Assert.Equal(expected, ScavInventoryPolicy.Extracted(status));

    [Fact] public void PocketSpecialSlotsRetainedButOrdinaryPocketAmmoIsNot()
    {
        var p = Profile("Usec"); string root = p.Inventory!.Equipment.ToString()!;
        var pockets = Item(root, "Pockets"); var special = Item(pockets.Id.ToString(), "SpecialSlot1");
        var child = Item(special.Id.ToString(), "mod"); var ammo = Item(pockets.Id.ToString(), "pocket1");
        p.Inventory.Items!.AddRange([pockets, special, child, ammo]);
        var result = ScavInventoryPolicy.RetainedIds(p.Inventory.Items, [ammo]);
        Assert.Contains(special.Id.ToString(), result); Assert.Contains(child.Id.ToString(), result);
        Assert.Contains(pockets.Id.ToString(), result); Assert.DoesNotContain(ammo.Id.ToString(), result);
    }

    [Fact] public void RecoveredAttachmentIsNotReturnedWithLostInsuredWeapon()
    {
        var gun = Item("corpse", "FirstPrimaryWeapon"); var scope = Item(gun.Id.ToString(), "mod_scope");
        var lost = ScavInventoryPolicy.LostInsurance([gun, scope], [gun.Id.ToString(), scope.Id.ToString()], [scope.Id.ToString()], []);
        Assert.Same(gun, Assert.Single(lost));
    }

    [Fact] public void RecoveredContainerExcludesItsInsuredContents()
    {
        var bag = Item("corpse", "Backpack"); var gun = Item(bag.Id.ToString(), "main");
        Assert.Empty(ScavInventoryPolicy.LostInsurance([bag, gun], [bag.Id.ToString(), gun.Id.ToString()],
            [bag.Id.ToString(), gun.Id.ToString()], []));
    }

    [Fact] public void UnregisteredAndDuplicateInsuranceAreRejected()
    {
        var gun = Item();
        Assert.Throws<InvalidDataException>(() => ScavInventoryPolicy.LostInsurance([gun], [], [], []));
        Assert.Throws<InvalidDataException>(() => ScavInventoryPolicy.LostInsurance([gun, gun], [gun.Id.ToString()], [], []));
    }

    [Fact] public void CarriedGraphRejectsCycleOrphanDuplicateAndUnknownTemplate()
    {
        var p = Profile(); var a = Item("missing", "mod"); p.Inventory!.Items!.Add(a);
        Assert.Throws<InvalidDataException>(() => ScavInventoryPolicy.Carried(p, _ => true));
        a.ParentId = a.Id.ToString();
        Assert.Throws<InvalidDataException>(() => ScavInventoryPolicy.Carried(p, _ => true));
        a.ParentId = p.Inventory.Equipment.ToString(); p.Inventory.Items.Add(a);
        Assert.Throws<InvalidDataException>(() => ScavInventoryPolicy.Carried(p, _ => true));
        Assert.Throws<InvalidDataException>(() => ScavInventoryPolicy.Carried(Profile(), _ => false));
    }

    [Fact] public void JournalRoundtripUsesSafeFilenameAndKeepsRecoveryData()
    {
        using var f = new Fixture();
        var record = f.Entry(); record.InitialServerId = "../../foreign.pmc 123";
        record.Phase = ScavPhase.RecoveryRequired; record.BeforeProfileJson = "private";
        f.Journal.Write(record);
        Assert.Equal("private", f.Journal.Read(f.Session.ToString(), record.InitialServerId)!.BeforeProfileJson);
        Assert.Single(Directory.GetFiles(f.JournalDirectory, "*.json", SearchOption.AllDirectories));
        Assert.Throws<InvalidOperationException>(() => ScavStatePolicy.CompactCommitted(record));
        record.Phase = ScavPhase.Committed; ScavStatePolicy.CompactCommitted(record); f.Journal.Write(record);
        var compact = f.Journal.Read(f.Session.ToString(), record.InitialServerId)!;
        Assert.Null(compact.BeforeProfileJson); Assert.Empty(compact.FrozenPmcJson); Assert.Empty(compact.Token);
        Assert.Equal(record.ScavEquipmentId, compact.ScavEquipmentId);
    }

    [Fact] public void CorruptOtherProfileJournalCannotBlockOrdinaryRaid()
    {
        using var f = new Fixture();
        var other = f.Entry(); other.SessionId = new MongoId().ToString(); f.Journal.Write(other);
        string foreignFile = Assert.Single(Directory.GetFiles(f.JournalDirectory, "*.json", SearchOption.AllDirectories));
        File.WriteAllText(foreignFile, "corrupt");
        Assert.Empty(f.Journal.ForSession(f.Session.ToString()));
        f.Service.BeforeStart(f.Session);
    }

    [Theory] [InlineData(ScavPhase.Prepared, true)] [InlineData(ScavPhase.Active, false)]
    [InlineData(ScavPhase.Settling, false)] [InlineData(ScavPhase.Committed, false)]
    [InlineData(ScavPhase.RecoveryRequired, false)] [InlineData(ScavPhase.Cancelled, true)]
    public void CancelCannotUndoAuthoritativeBody(ScavPhase phase, bool allowed)
        => Assert.Equal(allowed, ScavStatePolicy.CanCancel(phase));

    [Theory] [InlineData(ScavPhase.Prepared, true)] [InlineData(ScavPhase.Active, true)]
    [InlineData(ScavPhase.Settling, true)] [InlineData(ScavPhase.RecoveryRequired, true)]
    [InlineData(ScavPhase.Committed, false)] [InlineData(ScavPhase.Cancelled, false)]
    public void EveryUnresolvedBodyPhaseFencesNewRaids(ScavPhase phase, bool blocks)
        => Assert.Equal(blocks, ScavStatePolicy.BlocksNewRaid(phase));

    [Fact] public async Task LostActivatePreparedJournalBlocksNewRaidEvenAfterExpiry()
    {
        using var f = new Fixture(); var entry = f.Entry(); entry.ExpiresAt = 1; f.Journal.Write(entry);
        Assert.Throws<InvalidOperationException>(() => f.Service.BeforeStart(f.Session));
        // Explicit cancellation remains available when the caller knows its body did not commit.
        var cancelled = await f.Service.Control(f.Session, new() { InitialServerId = f.Raid, Token = entry.Token }, true, default);
        Assert.True(cancelled.Success); f.Service.BeforeStart(f.Session);
    }

    [Fact] public async Task UnresolvedPreparedJournalRejectsAnotherGeneration()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        string nextRaid = "factory4_day.pmc 123456790";
        f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = nextRaid });
        var request = f.Json.Deserialize<EndLocalRaidRequestData>(f.Json.Serialize(f.Frozen))!;
        request.ServerId = nextRaid;
        var result = await f.Service.Prepare(f.Session, new() { ProtocolVersion = 2, InitialServerId = nextRaid,
            ClientRequestId = Guid.NewGuid().ToString("N"), FrozenPmcDeath = request }, default);
        Assert.Equal("previous-continuation-pending", result.Error);
        Assert.Null(f.Journal.Read(f.Session.ToString(), nextRaid));
    }

    [Fact] public async Task PrepareWithoutServerIssuedRaidDoesNotArmAnything()
    {
        using var f = new Fixture();
        var result = await f.Service.Prepare(f.Session, new() { ProtocolVersion = 2, InitialServerId = f.Raid,
            ClientRequestId = Guid.NewGuid().ToString("N"), FrozenPmcDeath = f.Frozen }, default);
        Assert.False(result.Success); Assert.Equal("no-issued-pmc-raid", result.Error);
        Assert.Null(f.Journal.Read(f.Session.ToString(), f.Raid));
        Assert.False(f.Service.ShouldInterceptEnd(f.Session, f.Frozen));
    }

    [Theory]
    [InlineData(null)] [InlineData(TransitionType.NONE)]
    [InlineData(TransitionType.COMMON)] [InlineData(TransitionType.EVENT)]
    [InlineData(TransitionType.COMMON | TransitionType.EVENT)]
    public async Task FreshPmcWithAvailableTransitExitsCanReserveRegularScav(TransitionType? flags)
    {
        using var f = new Fixture();
        f.Service.RegisterStart(f.Session, new() { PlayerSide = "Pmc", TransitionType = flags }, new()
        {
            ServerId = f.Raid, TransitionType = flags,
            Transition = new() { TransitionType = flags, TransitionRaidId = new MongoId().ToString(),
                TransitionCount = 0, VisitedLocations = [] }
        });

        var result = await f.Service.Prepare(f.Session, f.PrepareRequest(), default);

        Assert.True(result.Success, result.Error);
        Assert.Equal(f.Scav.Id, result.ScavProfile!.Id);
        Assert.Equal(ScavPhase.Prepared, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
        Assert.True(f.Service.ShouldInterceptEnd(f.Session, f.Frozen));
        Assert.Equal(0, f.Writes);
    }

    [Theory]
    [InlineData("incoming-transition")] [InlineData("positive-count")] [InlineData("visited-location")]
    [InlineData("negative-count")] [InlineData("missing-count")] [InlineData("missing-history")]
    [InlineData("unknown-flags")] [InlineData("scav")] [InlineData("request-arena")]
    [InlineData("response-arena")] [InlineData("invalid-server")]
    public async Task IneligibleStartRevokesPreviousIssuedAuthority(string reason)
    {
        using var f = new Fixture();
        f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = f.Raid });
        var request = new StartLocalRaidRequestData { PlayerSide = "pmc", TransitionType = TransitionType.COMMON };
        var response = new StartLocalRaidResponseData { ServerId = f.Raid,
            Transition = new() { TransitionCount = 0, VisitedLocations = [] } };
        switch (reason)
        {
            // Even an incoming object claiming zero hops cannot authorize a fresh raid.
            case "incoming-transition": request.Transition = new() { TransitionCount = 0, VisitedLocations = [] }; break;
            case "positive-count": response.Transition.TransitionCount = 1; break;
            case "visited-location": response.Transition.VisitedLocations = ["bigmap"]; break;
            case "negative-count": response.Transition.TransitionCount = -1; break;
            case "missing-count": response.Transition.TransitionCount = null; break;
            case "missing-history": response.Transition.VisitedLocations = null; break;
            case "unknown-flags": request.TransitionType = (TransitionType)4; break;
            case "scav": request.PlayerSide = "Savage"; break;
            case "request-arena": request.ServerId = "arenamode-start:test"; break;
            case "response-arena": response.ServerId += "|arenamode:test"; break;
            case "invalid-server": response.ServerId = "factory4_day.Savage 123456789"; break;
            default: throw new ArgumentOutOfRangeException(nameof(reason));
        }
        f.Service.RegisterStart(f.Session, request, response);

        var result = await f.Service.Prepare(f.Session, f.PrepareRequest(), default);

        Assert.False(result.Success);
        Assert.Equal("no-issued-pmc-raid", result.Error);
        Assert.Empty(f.Journal.ForSession(f.Session.ToString()));
        Assert.False(f.Service.ShouldInterceptEnd(f.Session, f.Frozen));
        Assert.Equal(0, f.Writes);
    }

    [Fact] public async Task FreshTransitEnabledStartStillRequiresExactIssuedRaidIdentity()
    {
        using var f = new Fixture();
        f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc", TransitionType = TransitionType.COMMON },
            new() { ServerId = "factory4_day.pmc 123456790",
                Transition = new() { TransitionCount = 0, VisitedLocations = [] } });

        var result = await f.Service.Prepare(f.Session, f.PrepareRequest(), default);

        Assert.False(result.Success);
        Assert.Equal("no-issued-pmc-raid", result.Error);
        Assert.Empty(f.Journal.ForSession(f.Session.ToString()));
        Assert.Equal(0, f.Writes);
    }

    [Fact] public async Task OrdinaryEndSerializesAndRejectsDelayedPrepareWithoutCreatingJournal()
    {
        using var f = new Fixture();
        f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = f.Raid });
        Assert.True(f.Service.ShouldInterceptEnd(f.Session, f.Frozen));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string originalProfile = f.Json.Serialize(f.Full)!;
        var ending = f.Service.End(f.Session, f.Frozen, async request =>
        {
            Assert.Same(f.Frozen, request);
            // The raw-native recursion guard still bypasses this wrapper.
            Assert.False(f.Service.ShouldInterceptEnd(f.Session, request));
            entered.SetResult(); await finish.Task;
        }, f.NativeScavOutcome, default);
        await entered.Task;
        var delayedPrepare = f.Service.Prepare(f.Session, new()
        {
            ProtocolVersion = 2, InitialServerId = f.Raid, ClientRequestId = Guid.NewGuid().ToString("N"), FrozenPmcDeath = f.Frozen
        }, default);
        Assert.False(delayedPrepare.IsCompleted);
        finish.SetResult(); await ending;
        Assert.Equal("no-issued-pmc-raid", (await delayedPrepare).Error);
        Assert.False(f.Service.ShouldInterceptEnd(f.Session, f.Frozen));
        Assert.Null(f.Journal.Read(f.Session.ToString(), f.Raid));
        Assert.Equal(originalProfile, f.Json.Serialize(f.Full));
        f.Service.BeforeStart(f.Session);
    }

    [Fact] public async Task FailedOrdinaryEndCannotReopenStalePreparationAuthority()
    {
        using var f = new Fixture();
        f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = f.Raid });
        await Assert.ThrowsAsync<IOException>(() => f.Service.End(f.Session, f.Frozen,
            _ => throw new IOException("native failure"), f.NativeScavOutcome, default));
        var result = await f.Service.Prepare(f.Session, new() { ProtocolVersion = 2, InitialServerId = f.Raid,
            ClientRequestId = Guid.NewGuid().ToString("N"), FrozenPmcDeath = f.Frozen }, default);
        Assert.Equal("no-issued-pmc-raid", result.Error);
        Assert.Null(f.Journal.Read(f.Session.ToString(), f.Raid));
    }

    [Fact] public void UnrelatedNormalEndRemainsOutsideContinuationWrapper()
    {
        using var f = new Fixture();
        f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = f.Raid });
        var otherEnd = new EndLocalRaidRequestData { ServerId = "factory4_day.pmc unrelated", Results = f.Frozen.Results };
        Assert.False(f.Service.ShouldInterceptEnd(f.Session, otherEnd));
        var scavEnd = new EndLocalRaidRequestData { ServerId = "factory4_day.scav 123456789", Results = f.Final.Results };
        Assert.False(f.Service.ShouldInterceptEnd(f.Session, scavEnd));
        Assert.True(f.Service.ShouldInterceptEnd(f.Session, f.Frozen));
    }

    [Fact] public async Task RegisteredGenerationRetryAndWrongControlToken()
    {
        using var f = new Fixture(); var entry = f.Entry(); f.Journal.Write(entry);
        var response = await f.Service.Prepare(f.Session, new() { ProtocolVersion = 2, InitialServerId = f.Raid,
            ClientRequestId = entry.RequestId, FrozenPmcDeath = f.Frozen }, default);
        Assert.True(response.Success); Assert.Equal(entry.ScavProfileId, response.ScavProfile!.Id.ToString());
        var bad = await f.Service.Control(f.Session, new() { InitialServerId = f.Raid, Token = new string('1',64) }, false, default);
        Assert.False(bad.Success); Assert.Equal(ScavPhase.Prepared, f.Journal.Read(f.Session.ToString(),f.Raid)!.Phase);
    }

    [Fact] public async Task OrdinaryPmcEndAfterFailedBodyIsPassedThroughWithoutHybridEffects()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry()); int calls = 0;
        async Task Native(EndLocalRaidRequestData request) { calls++; Assert.Same(f.Frozen, request); await f.NativeEnd(request); }
        Task NoScav(PmcData _, PmcData __, EndLocalRaidRequestData ___)
            => throw new InvalidOperationException("A failed replacement must not settle the regular Scav.");
        await f.Service.End(f.Session, f.Frozen, Native, NoScav, default);
        await f.Service.End(f.Session, f.Frozen, Native, NoScav, default);
        Assert.Equal(1, calls); Assert.Equal(ScavPhase.Committed, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
        Assert.True(f.Journal.Read(f.Session.ToString(), f.Raid)!.PmcOnly);
        Assert.False(f.Service.OwnsRegeneration(f.Session));
        Assert.False(f.Service.BlocksScavSave(f.Session, f.Scav.Inventory!.Equipment.ToString()));
        Assert.Empty(f.Full.DialogueRecords!);
    }

    [Fact] public async Task CommitSettlesPmcThenActualScavAndNeverMailsOrRepeats()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        var scavGear = f.Json.Serialize(f.Scav.Inventory);
        await f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default);
        await f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default);
        Assert.Equal(new[] { "pmc", "scav" }, f.NativeStages);
        Assert.Equal(1, f.Writes); Assert.Empty(f.Full.DialogueRecords!);
        Assert.Equal(scavGear, f.Json.Serialize(f.Full.CharacterData!.ScavData!.Inventory));
        Assert.Equal(99999, f.Full.CharacterData.ScavData.Info!.Experience);
        Assert.Equal(123, f.Full.CharacterData.PmcData!.Info!.Experience);
        var entry = f.Journal.Read(f.Session.ToString(), f.Raid)!;
        Assert.Equal(ScavPhase.Committed, entry.Phase);
        Assert.Equal(ScavTransferPhase.Pending, entry.TransferPhase);
        Assert.Empty(entry.ScavJson); Assert.Null(entry.BeforeProfileJson);
        Assert.Throws<InvalidOperationException>(() => f.Service.BeforeStart(f.Session));
    }

    [Fact] public void NativeScavHandlerContractMatchesReferencedSpt()
        => NativeScavOutcome.ValidateContract();

    [Fact] public async Task CombinedResultDoesNotReenterAnOuterExactRaidGateWithInventedIdentity()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        using var outerGate = new SemaphoreSlim(1, 1);
        await outerGate.WaitAsync();
        int outerCalls = 0;
        async Task OuterEnd(EndLocalRaidRequestData request)
        {
            outerCalls++;
            if (request.ServerId != f.Raid)
            {
                // Arena's ordinary end owns this gate for the issued identity. Its
                // lower prefix can bypass it only for that exact original identity.
                // A zero-time probe reproduces the blocked wait without hanging tests.
                Assert.False(await outerGate.WaitAsync(0));
                throw new InvalidOperationException("outer exact-raid gate would deadlock");
            }
            await f.NativeEnd(request);
        }

        await f.Service.End(f.Session, f.Final, OuterEnd, f.NativeScavOutcome, default);
        Assert.Equal(1, outerCalls);
        Assert.Equal(new[] { "pmc", "scav" }, f.NativeStages);
        Assert.Equal(ScavPhase.Committed, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
        Assert.Equal(1, f.Writes);

        // Negative control: the previous implementation routed its second stage
        // through that outer pipeline with a synthetic Scav ServerId.
        var mutant = new EndLocalRaidRequestData { ServerId = "factory4_day.scav redeploy", Results = f.Final.Results };
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => OuterEnd(mutant));
        Assert.Equal("outer exact-raid gate would deadlock", failure.Message);
    }

    [Theory] [InlineData(ExitStatus.KILLED)] [InlineData(ExitStatus.LEFT)] [InlineData(ExitStatus.MISSINGINACTION)]
    public async Task FailedScavExitCreatesSuccessorAndCooldownWithoutTransfer(ExitStatus outcome)
    {
        using var f = new Fixture(); f.Final.Results!.Result = outcome; f.Journal.Write(f.Entry());
        string oldRoot = f.Scav.Inventory!.Equipment.ToString()!;
        await f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default);
        Assert.Empty(f.Full.DialogueRecords!);
        Assert.NotEqual(oldRoot, f.Full.CharacterData!.ScavData!.Inventory!.Equipment.ToString());
        Assert.True(f.Full.CharacterData.ScavData.Info!.SavageLockTime > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Assert.Equal(ScavTransferPhase.Complete, f.Journal.Read(f.Session.ToString(), f.Raid)!.TransferPhase);
        f.Service.BeforeStart(f.Session);
    }

    [Fact] public async Task WriteFailureQuarantinesPartialStateAndNeverReplaysNative()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry()); int calls = 0;
        Task Native(EndLocalRaidRequestData _) { calls++; throw new IOException("simulated before write"); }
        await Assert.ThrowsAsync<IOException>(() => f.Service.End(f.Session, f.Final, Native, f.NativeScavOutcome, default));
        Assert.Equal(ScavPhase.RecoveryRequired, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.End(f.Session, f.Final, Native, f.NativeScavOutcome, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.Save(f.Session, f.WriteProfile, default));
        Assert.Equal(1, calls); Assert.False(File.Exists(f.ProfilePath));
    }

    [Fact] public async Task NativeErrorBeforeJointSaveCannotCommitOnlyPmc()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        await Assert.ThrowsAsync<IOException>(() => f.Service.End(f.Session, f.Final, async request =>
        {
            await f.NativeEnd(request);
            throw new IOException("backup failed before Scav settlement");
        }, f.NativeScavOutcome, default));
        Assert.Equal(0, f.Writes); Assert.False(File.Exists(f.ProfilePath));
        Assert.Equal(ScavPhase.RecoveryRequired, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
    }

    [Fact] public async Task NativeScavFailurePropagatesAndCannotCommitOnlyPmc()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        var expected = new IOException("Scav outcome failed");
        var actual = await Assert.ThrowsAsync<IOException>(() => f.Service.End(f.Session, f.Final,
            f.NativeEnd, async (pmc, scav, request) =>
            {
                await f.NativeScavOutcome(pmc, scav, request);
                throw expected;
            }, default));
        Assert.Same(expected, actual);
        Assert.Equal(new[] { "pmc", "scav" }, f.NativeStages);
        Assert.Equal(0, f.Writes); Assert.False(File.Exists(f.ProfilePath));
        Assert.Equal(ScavPhase.RecoveryRequired, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
    }

    [Fact] public async Task RestartWithSettlingJournalAndExactDiskStateDoesNotReplayNative()
    {
        using var f = new Fixture(); var entry = f.Entry(); entry.Phase = ScavPhase.Settling;
        entry.FinalHash = ScavJournal.Hash(f.Json.Serialize(f.Final)!);
        entry.ExpectedProfileHash = ScavJournal.Hash(f.Json.Serialize(f.Full)!);
        entry.TransferPhase = ScavTransferPhase.Pending; f.Journal.Write(entry); await f.WriteProfile();
        await f.Service.End(f.Session, f.Final, _ => throw new Exception("must not replay"), f.NativeScavOutcome, default);
        Assert.Equal(ScavPhase.Committed, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
        Assert.Throws<InvalidOperationException>(() => f.Service.BeforeStart(f.Session));
    }

    [Fact] public async Task ConflictingReplayAndProtectedItemLeakFailBeforeNative()
    {
        using var f = new Fixture(); var entry = f.Entry(); entry.FinalHash = "different"; f.Journal.Write(entry);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.End(f.Session, f.Final, _ => throw new Exception("native"), f.NativeScavOutcome, default));
        entry.FinalHash = null; f.Journal.Write(entry);
        // Pockets root is retained by native PMC deletion, cannot be extracted as Scav loot.
        var kept = Item(f.Pmc.Inventory!.Equipment.ToString(), "Pockets"); f.Pmc.Inventory.Items!.Add(kept);
        entry.FrozenPmcJson = f.Json.Serialize(f.Frozen)!; f.Journal.Write(entry);
        f.Scav.Inventory!.Items!.Add(kept with { ParentId = f.Scav.Inventory.Equipment.ToString() });
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Service.End(f.Session, f.Final, _ => throw new Exception("native"), f.NativeScavOutcome, default));
    }

    [Fact] public async Task ConcurrentAutosaveWaitsUntilSettlementCompletes()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var end = f.Service.End(f.Session, f.Final, async _ => { entered.TrySetResult(); await release.Task;
            await f.NativeEnd(_); }, f.NativeScavOutcome, default);
        await entered.Task;
        var save = f.Service.Save(f.Session, f.WriteProfile, default);
        Assert.False(save.IsCompleted);
        release.SetResult(); await end; await save;
        Assert.Equal(ScavPhase.Committed, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
    }

    [Fact] public async Task NativeProtectionRejectsSpecialSlotDescendantInsidePockets()
    {
        using var f = new Fixture();
        var pockets = Item(f.Pmc.Inventory!.Equipment.ToString(), "Pockets");
        var special = Item(pockets.Id.ToString(), "SpecialSlot1"); var child = Item(special.Id.ToString(), "mod");
        f.Pmc.Inventory.Items!.AddRange([pockets, special, child]);
        f.Journal.Write(f.Entry());
        f.Scav.Inventory!.Items!.Add(child with { ParentId = f.Scav.Inventory.Equipment.ToString(), SlotId = "Holster" });
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Service.End(f.Session, f.Final,
            _ => throw new Exception("native must not execute"), f.NativeScavOutcome, default));
        Assert.Equal(ScavPhase.Prepared, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
    }

    [Fact] public async Task EndAcceptsEmptyNativeTransferStashAndRejectsRealContentsBeforeSettlement()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        string key = ScavTransferPolicy.TransferRootId + "_btr";
        var root = new Item { Id = new MongoId(ScavTransferPolicy.TransferRootId), Template = new MongoId(ScavTransferPolicy.TransferRootTemplate) };
        f.Final.TransferItems = new() { [key] = [root, Item(root.Id.ToString(), "main")] };
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Service.End(f.Session, f.Final,
            _ => throw new Exception("native must not execute"), f.NativeScavOutcome, default));
        Assert.Equal(ScavPhase.Prepared, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
        f.Final.TransferItems[key] = [root];
        await f.Service.End(f.Session, f.Final, async request =>
        {
            Assert.Null(request.TransferItems);
            f.Service.NativePmcHandled(f.Session, f.Full);
            await f.Service.Save(f.Session, f.WriteProfile, default);
        }, f.NativeScavOutcome, default);
        Assert.Equal(ScavPhase.Committed, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
    }

    public class NullLog : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method!.ReturnType == typeof(void) ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
    }
    private static ISptLogger<T> Logger<T>() => DispatchProxy.Create<ISptLogger<T>, NullLog>();

    [Fact] public async Task PrepareUsesActualAccountScavAndPreservesGearSkillsAndCooldown()
    {
        using var f = new Fixture();
        f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = f.Raid });
        string before = f.Json.Serialize(f.Full)!;
        var request = f.PrepareRequest();
        var response = await f.Service.Prepare(f.Session, request, default);
        Assert.True(response.Success, response.Error);
        Assert.Equal(f.Full.CharacterData!.ScavData!.Id, response.ScavProfile!.Id);
        Assert.Equal(f.Json.Serialize(f.Full.CharacterData.ScavData), f.Json.Serialize(response.ScavProfile));
        Assert.NotSame(f.Full.CharacterData.ScavData, response.ScavProfile);
        Assert.NotSame(f.Full.CharacterData.ScavData.Inventory, response.ScavProfile.Inventory);
        Assert.Equal(before, f.Json.Serialize(f.Full));
        var retry = await f.Service.Prepare(f.Session, request, default);
        Assert.True(retry.Success); Assert.Equal(response.Token, retry.Token);
        Assert.Equal(response.GenerationHash, retry.GenerationHash);
        Assert.Equal(0, f.Writes);
    }

    [Fact] public async Task CooldownRefusesPreparationWithoutConsumingScavOrWritingJournal()
    {
        using var f = new Fixture();
        f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = f.Raid });
        f.Full.CharacterData!.ScavData!.Info!.SavageLockTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 3600;
        string before = f.Json.Serialize(f.Full)!;
        var response = await f.Service.Prepare(f.Session, f.PrepareRequest(), default);
        Assert.False(response.Success); Assert.Equal("scav-cooldown", response.Error);
        Assert.Equal(f.Full.CharacterData.ScavData.Info.SavageLockTime, response.AvailableAt);
        Assert.Null(f.Journal.Read(f.Session.ToString(), f.Raid));
        Assert.Equal(before, f.Json.Serialize(f.Full)); Assert.Equal(0, f.Writes);
    }

    [Theory] [InlineData("id")] [InlineData("side")] [InlineData("lock")]
    public async Task MalformedAccountScavCannotBecomeAReplacement(string mutation)
    {
        using var f = new Fixture();
        f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = f.Raid });
        var scav = f.Full.CharacterData!.ScavData!;
        if (mutation == "id") scav.Id = new MongoId();
        if (mutation == "side") scav.Info!.Side = "Usec";
        if (mutation == "lock") scav.Info!.SavageLockTime = null;
        Assert.Equal("regular-scav-unavailable", (await f.Service.Prepare(f.Session, f.PrepareRequest(), default)).Error);
        Assert.Null(f.Journal.Read(f.Session.ToString(), f.Raid));
    }

    [Fact] public async Task CancelledPreparationPreservesAccountScavAndDoesNotFenceOrdinaryUpload()
    {
        using var f = new Fixture(); var entry = f.Entry(); f.Journal.Write(entry);
        string before = f.Json.Serialize(f.Full)!;
        var response = await f.Service.Control(f.Session, new() { InitialServerId = f.Raid, Token = entry.Token }, true, default);
        Assert.True(response.Success); Assert.Equal(before, f.Json.Serialize(f.Full));
        Assert.False(f.Service.OwnsRegeneration(f.Session));
        Assert.False(f.Service.BlocksScavSave(f.Session, entry.ScavEquipmentId));
        f.Service.BeforeStart(f.Session);
    }

    [Fact] public async Task ChangedReservedLoadoutRefusesActivationAndSettlementBeforeSideEffects()
    {
        using var f = new Fixture(); var entry = f.Entry(); f.Journal.Write(entry);
        f.Full.CharacterData!.ScavData!.Inventory!.Items!.Add(Item(entry.ScavEquipmentId, "Backpack"));
        var activation = await f.Service.Control(f.Session, new() { InitialServerId = f.Raid, Token = entry.Token,
            ScavProfileId = entry.ScavProfileId, GenerationHash = entry.GenerationHash }, false, default);
        Assert.False(activation.Success);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default));
        Assert.Empty(f.NativeStages); Assert.Equal(0, f.Writes);
    }

    [Fact] public async Task LateActivationStillUsesReservedBodyAndCannotCancelIt()
    {
        using var f = new Fixture(); var entry = f.Entry(); entry.ExpiresAt = 1; f.Journal.Write(entry);
        var request = new ScavControlRequest { InitialServerId = f.Raid, Token = entry.Token,
            ScavProfileId = entry.ScavProfileId, GenerationHash = entry.GenerationHash };
        Assert.True((await f.Service.Control(f.Session, request, false, default)).Success);
        Assert.True((await f.Service.Control(f.Session, request, false, default)).Success);
        Assert.False((await f.Service.Control(f.Session, request, true, default)).Success);
    }

    [Fact] public async Task TransferCompletionGeneratesOnceAndRetryCannotRecreateLoot()
    {
        using var f = new Fixture(); var entry = f.Entry(); f.Journal.Write(entry);
        await f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default);
        var transferred = f.Full.CharacterData!.ScavData!.Inventory!.Items!.Last();
        f.Full.CharacterData.ScavData.Inventory.Items!.Remove(transferred);
        f.Pmc.Inventory!.Items!.Add(transferred with { ParentId = f.Pmc.Inventory.Equipment.ToString(), SlotId = "Backpack" });
        int generations = 0;
        ValueTask<string> Regenerate()
        {
            generations++; Assert.False(f.Service.OwnsRegeneration(f.Session));
            var next = Profile(); next.Id = f.Scav.Id; next.Info!.SavageLockTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds()+1200;
            f.Full.CharacterData.ScavData = next;
            return ValueTask.FromResult("native-response");
        }
        Assert.Equal("native-response", await f.Service.FinishTransfer(f.Session, Regenerate, f.WriteProfile, default));
        var after = f.Json.Serialize(f.Full);
        Assert.Equal("native-response", await f.Service.FinishTransfer(f.Session, Regenerate, f.WriteProfile, default));
        Assert.Equal(1, generations); Assert.Equal(2, f.Writes); Assert.Equal(after, f.Json.Serialize(f.Full));
        Assert.True(f.Service.BlocksScavSave(f.Session, entry.ScavEquipmentId));
        Assert.False(f.Service.BlocksScavSave(f.Session, f.Full.CharacterData.ScavData.Inventory!.Equipment.ToString()));
        f.Service.BeforeStart(f.Session);
        f.Service.RegisterStart(f.Session, new() { PlayerSide = "scav" }, new() { ServerId = "factory4_day.scav 2" });
        Assert.False(f.Service.OwnsRegeneration(f.Session));
        Assert.True(f.Service.BlocksScavSave(f.Session, entry.ScavEquipmentId));
    }

    [Theory] [InlineData(ScavPhase.Prepared)] [InlineData(ScavPhase.Active)]
    public async Task RegenerationCannotChangeAReservedScav(ScavPhase phase)
    {
        using var f = new Fixture(); var entry = f.Entry(); entry.Phase = phase; f.Journal.Write(entry);
        Assert.True(f.Service.OwnsRegeneration(f.Session));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Service.FinishTransfer(f.Session,
            () => throw new Exception("native must not run"), f.WriteProfile, default));
        Assert.Equal(0, f.Writes);
    }

    [Fact] public async Task TransferWriteFailureIsQuarantinedAndNeverRegeneratesTwice()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        await f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default);
        int generations = 0;
        ValueTask<string> Regenerate()
        {
            generations++; var next = Profile(); next.Id = f.Scav.Id; f.Full.CharacterData!.ScavData = next;
            return ValueTask.FromResult("next");
        }
        await Assert.ThrowsAsync<IOException>(async () => await f.Service.FinishTransfer(f.Session, Regenerate,
            () => throw new IOException("disk full"), default));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Service.FinishTransfer(f.Session, Regenerate, f.WriteProfile, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.Save(f.Session, f.WriteProfile, default));
        Assert.Equal(1, generations);
    }

    [Fact] public async Task LegacyPendingJournalCannotBeReinterpretedOrMutated()
    {
        using var f = new Fixture(); var entry = f.Entry(); entry.Version = 1; f.Journal.Write(entry);
        Assert.Throws<InvalidOperationException>(() => f.Service.BeforeStart(f.Session));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default));
        var result = await f.Service.Prepare(f.Session, f.PrepareRequest(), default);
        Assert.False(result.Success); Assert.Equal(1, f.Journal.Read(f.Session.ToString(),f.Raid)!.Version);
        Assert.Equal(0, f.Writes); Assert.Empty(f.NativeStages);
    }

    [Fact] public async Task SavedProfileReadbackMustContainBothResults()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        await f.WriteProfile(); f.Writes = 0;
        await Assert.ThrowsAsync<IOException>(() => f.Service.End(f.Session, f.Final, async request =>
        {
            await f.NativeEnd(request);
            await f.Service.Save(f.Session, () => Task.FromResult(0L), default);
        }, async (pmc, scav, request) =>
        {
            await f.NativeScavOutcome(pmc, scav, request);
            await f.Service.Save(f.Session, () => Task.FromResult(0L), default);
        }, default));
        Assert.Equal(ScavPhase.RecoveryRequired, f.Journal.Read(f.Session.ToString(),f.Raid)!.Phase);
        Assert.Equal(0, f.Writes);
    }

    [Fact] public async Task ReservedInventoryRefusesMenuMutations()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Service.WithTransferInventory<int>(f.Session,
            () => throw new Exception("native must not run"), default));
        Assert.Equal(0, f.Writes);
    }

    [Fact] public async Task TransferMoveAndNestedSaveCompleteBeforeRegeneration()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        await f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool moved = false, generated = false;
        var move = f.Service.WithTransferInventory(f.Session, async () =>
        {
            Assert.False(f.Service.OwnsInventoryWindow(f.Session)); entered.SetResult(); await release.Task;
            moved = true; await f.Service.Save(f.Session, f.WriteProfile, default); return 42;
        }, default).AsTask();
        await entered.Task;
        var finish = f.Service.FinishTransfer(f.Session, () =>
        {
            Assert.True(moved); generated = true;
            var next = Profile(); next.Id = f.Scav.Id; f.Full.CharacterData!.ScavData = next;
            return ValueTask.FromResult("next");
        }, f.WriteProfile, default).AsTask();
        Assert.False(finish.IsCompleted); Assert.False(generated);
        release.SetResult(); Assert.Equal(42, await move); Assert.Equal("next", await finish);
        Assert.Equal(3, f.Writes);
    }

    [Fact] public async Task ScavStandingDeltaIsAddedAfterPmcStandingSettlement()
    {
        using var f = new Fixture(); var fence = Traders.FENCE;
        f.Pmc.TradersInfo = new() { [fence] = new() { Standing = 1 } };
        f.Full.CharacterData!.ScavData!.TradersInfo = new() { [fence] = new() { Standing = 1 } };
        f.Scav.TradersInfo = new() { [fence] = new() { Standing = 1.3 } };
        f.Journal.Write(f.Entry());
        await f.Service.End(f.Session, f.Final, async request =>
        {
            f.Pmc.TradersInfo[fence].Standing = 1.2;
            await f.NativeEnd(request);
        }, async (pmc, scav, request) =>
        {
            Assert.Equal(1.5, request.Results!.Profile!.TradersInfo![fence].Standing!.Value, 6);
            await f.NativeScavOutcome(pmc, scav, request);
        }, default);
    }

    [Fact] public async Task FinalScavSaveMustBeObservedNotOnlyPmcSave()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.End(f.Session, f.Final,
            f.NativeEnd, (_, _, _) => Task.CompletedTask, default));
        Assert.Equal(0, f.Writes); Assert.False(File.Exists(f.ProfilePath));
    }

    [Theory]
    [InlineData(null)] [InlineData(-1d)] [InlineData(86401d)]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(double.NegativeInfinity)]
    public async Task InvalidScavDurationCannotReachNativeSettlement(double? seconds)
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        f.Final.Results!.PlayTime = seconds;
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default));
        Assert.Empty(f.NativeStages); Assert.Equal(0, f.Writes);
        Assert.Equal(ScavPhase.Prepared, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
    }

    [Theory] [InlineData(0)] [InlineData(188)] [InlineData(86400)]
    public async Task ScavBodyDurationPassesThroughWithoutSubtractingPmcAgain(double seconds)
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        f.Final.Results!.PlayTime = seconds;
        await f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default);
        Assert.Equal(new[] { "pmc", "scav" }, f.NativeStages);
        Assert.Equal(1, f.Writes);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string originalDirectory = Environment.CurrentDirectory;
        public readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "redeploy-scav-tests-" + Guid.NewGuid().ToString("N"));
        public string JournalDirectory => Path.Combine(DirectoryPath, "journal");
        public string ProfilePath => Path.Combine(DirectoryPath, "user", "profiles", Session + ".json");
        public readonly MongoId Session = new(); public readonly string Raid = "factory4_day.pmc 123456789";
        public readonly JsonUtil Json = new([new SptJsonConverterRegistrator()]);
        public readonly PmcData Pmc = Profile("Usec"); public readonly PmcData Scav = Profile();
        public readonly SptProfile Full; public readonly ScavJournal Journal; public readonly ScavContinuationService Service;
        public readonly EndLocalRaidRequestData Frozen; public readonly EndLocalRaidRequestData Final;
        public Fixture()
        {
            Directory.CreateDirectory(DirectoryPath); Environment.CurrentDirectory = DirectoryPath;
            Pmc.Savage = Scav.Id;
            Full = new() { ProfileInfo = new() { ProfileId = Session }, CharacterData = new() { PmcData = Pmc, ScavData = new ServerCloner().Clone(Scav) }, DialogueRecords = [] };
            var saves = new SaveServer(new FileUtil(), [], Json, null!, null!, null!, Logger<SaveServer>(), null!);
            saves.AddProfile(Full);
            var templates = new TemplateTable { Character = [], CustomisationStorage = [], Items = new() { [Template] = null! },
                Prestige = null!, Quests = [], RepeatableQuests = null!, Handbook = null!, Customization = [], Dialogue = null!,
                Profiles = [], Prices = [], DefaultEquipmentPresets = [], Achievements = [], CustomAchievements = [], LocationServices = null! };
            var inRaid = new InRaidHelper(Logger<InRaidHelper>(), templates, null!, null!,
                new LostOnDeathConfig { Equipment = new() { FirstPrimaryWeapon = true, Backpack = true, PocketItems = true },
                    SpecialSlotItems = true, QuestItems = true }, new ServerCloner());
            Service = new(saves, Json, new ServerCloner(), inRaid, templates, Logger<ScavContinuationService>());
            Journal = new ScavJournal(JournalDirectory);
            typeof(ScavContinuationService).GetField("journal", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Service, Journal);
            Frozen = new() { ServerId = Raid, LostInsuredItems = [], Results = new() { Profile = Pmc, Result = ExitStatus.KILLED, PlayTime = 12 } };
            Scav.Inventory!.Items!.Add(Item(Scav.Inventory.Equipment.ToString(), "FirstPrimaryWeapon"));
            Scav.Info!.Experience = 99999;
            Final = new() { ServerId = Raid, LostInsuredItems = [], Results = new() { Profile = Scav, Result = ExitStatus.SURVIVED,
                PlayTime = 188, ExitName = "car-extract" } };
        }
        public ScavJournalEntry Entry() => new()
        {
            SessionId = Session.ToString(), InitialServerId = Raid, PmcProfileId = Pmc.Id.ToString()!, ScavProfileId = Scav.Id.ToString()!, ScavEquipmentId = Scav.Inventory!.Equipment.ToString()!,
            RequestId = Guid.NewGuid().ToString("N"), PrepareHash = ScavJournal.Hash(Json.Serialize(Frozen)!),
            FrozenPmcJson = Json.Serialize(Frozen)!, OriginalInsuredJson = "[]", ScavJson = Json.Serialize(Full.CharacterData!.ScavData)!,
            GenerationHash = ScavJournal.Hash(Json.Serialize(Full.CharacterData!.ScavData)!), Token = new string('0',64), ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()+600,
            Phase = ScavPhase.Prepared
        };
        public ScavPrepareRequest PrepareRequest() => new() { ProtocolVersion = 2, InitialServerId = Raid,
            ClientRequestId = Guid.NewGuid().ToString("N"), FrozenPmcDeath = Frozen };
        public readonly List<string> NativeStages = [];
        public int Writes;
        public async Task NativeEnd(EndLocalRaidRequestData request)
        {
            Assert.Equal(Raid, request.ServerId);
            NativeStages.Add("pmc");
            Assert.Equal(Pmc.Id, request.Results!.Profile!.Id);
            Assert.Equal(ExitStatus.KILLED, request.Results.Result);
            Assert.Equal(12, request.Results.PlayTime);
            Assert.Equal(123, request.Results.Profile.Info!.Experience);
            Assert.Null(request.Results.ExitName);
            Service.NativePmcHandled(Session, Full);
            await Service.Save(Session, WriteProfile, default);
            Assert.Equal(0, Writes);
        }
        public async Task NativeScavOutcome(PmcData pmc, PmcData scav, EndLocalRaidRequestData request)
        {
            Assert.Equal(Raid, request.ServerId);
            Assert.Same(Full.CharacterData!.PmcData, pmc);
            Assert.Same(Full.CharacterData.ScavData, scav);
            NativeStages.Add("scav");
            Assert.Equal(Final.Results!.PlayTime, request.Results!.PlayTime);
            Assert.Equal(Scav.Id, request.Results!.Profile!.Id);
            Assert.Null(request.LostInsuredItems);
            scav.Info!.Experience = request.Results.Profile.Info!.Experience;
            if (!ScavInventoryPolicy.Extracted(request.Results.Result))
            {
                var next = Profile(); next.Id = Scav.Id;
                next.Info!.Experience = request.Results.Profile.Info.Experience;
                next.Info.SavageLockTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1200;
                Full.CharacterData.ScavData = next;
            }
            await Service.Save(Session, WriteProfile, default);
            Assert.Equal(0, Writes); // Both handlers' saves are deferred.
        }
        public async Task<long> WriteProfile()
        {
            Writes++;
            Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath)!);
            await new FileUtil().WriteFileAsync(ProfilePath, Json.Serialize(Full)!);
            return 1;
        }
        public void Dispose()
        {
            Environment.CurrentDirectory = originalDirectory;
            // This fixture owns this fixed temp prefix and exact full path only.
            if (!Path.GetFullPath(DirectoryPath).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unexpected test directory.");
            Directory.Delete(DirectoryPath, true);
        }
    }
}

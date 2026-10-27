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
            Id = new MongoId(), Info = new() { Side = side, Experience = 123 }, InsuredItems = [],
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

    [Fact] public void MailDropsPocketShellAndRemapsContentsAndModsWithoutChangingCondition()
    {
        var p = Profile(); var pockets = Item(p.Inventory!.Equipment.ToString(), "Pockets");
        var gun = Item(pockets.Id.ToString(), "pocket1"); var mod = Item(gun.Id.ToString(), "mod_scope");
        mod.Upd = new() { SpawnedInSession = true, StackObjectsCount = 9 };
        p.Inventory.Items!.AddRange([pockets, gun, mod]);
        var carried = ScavInventoryPolicy.Carried(p, _ => true); var stash = new MongoId();
        var old = new[] { gun.Id, mod.Id };
        var mail = ScavInventoryPolicy.MailItems(ScavInventoryPolicy.LootItems(carried, p.Inventory.Equipment.ToString()!), stash);
        Assert.Equal(2, mail.Count); Assert.Equal(stash.ToString(), gun.ParentId);
        Assert.Equal(gun.Id.ToString(), mod.ParentId); Assert.DoesNotContain(gun.Id, old); Assert.DoesNotContain(mod.Id, old);
        Assert.True(mod.Upd.SpawnedInSession); Assert.Equal(9, mod.Upd.StackObjectsCount);
    }

    [Theory] [InlineData(ExitStatus.RUNNER, false)] [InlineData(ExitStatus.SURVIVED, true)]
    public void RunThroughRemovesFirOnlyWhenRequired(ExitStatus result, bool expected)
    {
        var item = Item(); item.Upd = new() { SpawnedInSession = true };
        ScavInventoryPolicy.ApplyRunThrough([item], result);
        Assert.Equal(expected, item.Upd.SpawnedInSession);
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
        Assert.NotEmpty(compact.ReceiptId);
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
        var result = await f.Service.Prepare(f.Session, new() { ProtocolVersion = 1, InitialServerId = nextRaid,
            ClientRequestId = Guid.NewGuid().ToString("N"), FrozenPmcDeath = request }, default);
        Assert.Equal("previous-continuation-pending", result.Error);
        Assert.Null(f.Journal.Read(f.Session.ToString(), nextRaid));
    }

    [Fact] public async Task PrepareWithoutServerIssuedRaidDoesNotArmAnything()
    {
        using var f = new Fixture();
        var result = await f.Service.Prepare(f.Session, new() { ProtocolVersion = 1, InitialServerId = f.Raid,
            ClientRequestId = Guid.NewGuid().ToString("N"), FrozenPmcDeath = f.Frozen }, default);
        Assert.False(result.Success); Assert.Equal("no-issued-pmc-raid", result.Error);
        Assert.Null(f.Journal.Read(f.Session.ToString(), f.Raid));
        Assert.False(f.Service.ShouldInterceptEnd(f.Session, f.Frozen));
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
        }, default);
        await entered.Task;
        var delayedPrepare = f.Service.Prepare(f.Session, new()
        {
            ProtocolVersion = 1, InitialServerId = f.Raid, ClientRequestId = Guid.NewGuid().ToString("N"), FrozenPmcDeath = f.Frozen
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
            _ => throw new IOException("native failure"), default));
        var result = await f.Service.Prepare(f.Session, new() { ProtocolVersion = 1, InitialServerId = f.Raid,
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
        var response = await f.Service.Prepare(f.Session, new() { ProtocolVersion = 1, InitialServerId = f.Raid,
            ClientRequestId = entry.RequestId, FrozenPmcDeath = f.Frozen }, default);
        Assert.True(response.Success); Assert.Equal(entry.ScavProfileId, response.ScavProfile!.Id.ToString());
        var bad = await f.Service.Control(f.Session, new() { InitialServerId = f.Raid, Token = new string('1',64) }, false, default);
        Assert.False(bad.Success); Assert.Equal(ScavPhase.Prepared, f.Journal.Read(f.Session.ToString(),f.Raid)!.Phase);
    }

    [Fact] public async Task OrdinaryPmcEndAfterFailedBodyIsPassedThroughWithoutHybridEffects()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry()); int calls = 0;
        await f.Service.End(f.Session, f.Frozen, request => { calls++; Assert.Same(f.Frozen, request); return Task.CompletedTask; }, default);
        Assert.Equal(1, calls); Assert.Equal(ScavPhase.Cancelled, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
        Assert.Empty(f.Full.DialogueRecords!);
    }

    [Fact] public async Task CommitFreezesPmcPreservesRegularScavAndDeliversExactlyOnce()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry()); string beforeScav = f.Json.Serialize(f.Full.CharacterData!.ScavData)!;
        int calls = 0;
        async Task Native(EndLocalRaidRequestData request)
        {
            calls++;
            Assert.Equal(f.Pmc.Id, request.Results!.Profile!.Id); Assert.Equal(ExitStatus.KILLED, request.Results.Result);
            Assert.Equal(123, request.Results.Profile.Info!.Experience);
            Assert.Equal(12, request.Results.PlayTime); Assert.Null(request.Results.ExitName);
            f.Full.CharacterData!.ScavData!.Info!.Experience = 9876;
            f.Service.NativePmcHandled(f.Session, f.Full);
            await f.Service.Save(f.Session, f.WriteProfile, default);
        }
        await f.Service.End(f.Session, f.Final, Native, default);
        await f.Service.End(f.Session, f.Final, Native, default);
        Assert.Equal(1, calls); Assert.Equal(beforeScav, f.Json.Serialize(f.Full.CharacterData!.ScavData));
        Assert.True(Assert.Single(Assert.Single(f.Full.DialogueRecords!).Value.Messages!).HasRewards);
        var committed = f.Journal.Read(f.Session.ToString(), f.Raid)!;
        Assert.Equal(ScavPhase.Committed, committed.Phase); Assert.Empty(committed.ScavJson); Assert.Null(committed.BeforeProfileJson);
        // Collect/delete mail cannot make a committed request deliver twice.
        f.Full.DialogueRecords!.Clear(); await f.WriteProfile();
        await f.Service.End(f.Session, f.Final, Native, default); Assert.Equal(1, calls);
    }

    [Theory] [InlineData(ExitStatus.KILLED)] [InlineData(ExitStatus.LEFT)] [InlineData(ExitStatus.MISSINGINACTION)]
    public async Task FailedScavExitProducesReceiptWithoutLoot(ExitStatus outcome)
    {
        using var f = new Fixture(); f.Final.Results!.Result = outcome; f.Journal.Write(f.Entry());
        await f.Service.End(f.Session, f.Final, async _ => { f.Service.NativePmcHandled(f.Session, f.Full);
            await f.Service.Save(f.Session, f.WriteProfile, default); }, default);
        Assert.False(Assert.Single(Assert.Single(f.Full.DialogueRecords!).Value.Messages!).HasRewards);
    }

    [Fact] public async Task WriteFailureQuarantinesPartialStateAndNeverReplaysNative()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry()); int calls = 0;
        Task Native(EndLocalRaidRequestData _) { calls++; throw new IOException("simulated before write"); }
        await Assert.ThrowsAsync<IOException>(() => f.Service.End(f.Session, f.Final, Native, default));
        Assert.Equal(ScavPhase.RecoveryRequired, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.End(f.Session, f.Final, Native, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.Save(f.Session, f.WriteProfile, default));
        Assert.Equal(1, calls); Assert.False(File.Exists(f.ProfilePath));
    }

    [Fact] public async Task BackupFailureAfterVerifiedDiskReceiptDoesNotReplayNative()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry()); int calls = 0;
        async Task Native(EndLocalRaidRequestData _) { calls++; f.Service.NativePmcHandled(f.Session, f.Full);
            await f.Service.Save(f.Session, f.WriteProfile, default); throw new IOException("simulated backup failure"); }
        await f.Service.End(f.Session, f.Final, Native, default);
        await f.Service.End(f.Session, f.Final, Native, default);
        Assert.Equal(1, calls); Assert.Equal(ScavPhase.Committed, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
    }

    [Fact] public async Task RestartWithSettlingJournalAndDiskReceiptRecoversWithoutNativeReplay()
    {
        using var f = new Fixture(); var entry = f.Entry(); entry.Phase = ScavPhase.Settling;
        entry.FinalHash = ScavJournal.Hash(f.Json.Serialize(f.Final)!); f.Journal.Write(entry);
        ScavContinuationService.AddReceipt(f.Full, new Message { Id = new MongoId(entry.ReceiptId), HasRewards = false });
        await f.WriteProfile();
        await f.Service.End(f.Session, f.Final, _ => throw new Exception("must not replay"), default);
        Assert.Equal(ScavPhase.Committed, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
    }

    [Fact] public async Task ConflictingReplayAndProtectedItemLeakFailBeforeNative()
    {
        using var f = new Fixture(); var entry = f.Entry(); entry.FinalHash = "different"; f.Journal.Write(entry);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.End(f.Session, f.Final, _ => throw new Exception("native"), default));
        entry.FinalHash = null; f.Journal.Write(entry);
        // Pockets root is retained by native PMC deletion, cannot be extracted as Scav loot.
        var kept = Item(f.Pmc.Inventory!.Equipment.ToString(), "Pockets"); f.Pmc.Inventory.Items!.Add(kept);
        entry.FrozenPmcJson = f.Json.Serialize(f.Frozen)!; f.Journal.Write(entry);
        f.Scav.Inventory!.Items!.Add(kept with { ParentId = f.Scav.Inventory.Equipment.ToString() });
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Service.End(f.Session, f.Final, _ => throw new Exception("native"), default));
    }

    [Fact] public async Task ConcurrentAutosaveWaitsUntilSettlementCompletes()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var end = f.Service.End(f.Session, f.Final, async _ => { entered.SetResult(); await release.Task;
            f.Service.NativePmcHandled(f.Session, f.Full); await f.Service.Save(f.Session, f.WriteProfile, default); }, default);
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
            _ => throw new Exception("native must not execute"), default));
        Assert.Equal(ScavPhase.Prepared, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
    }

    [Fact] public async Task EndAcceptsEmptyNativeTransferStashAndRejectsRealContentsBeforeSettlement()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        string key = ScavTransferPolicy.TransferRootId + "_btr";
        var root = new Item { Id = new MongoId(ScavTransferPolicy.TransferRootId), Template = new MongoId(ScavTransferPolicy.TransferRootTemplate) };
        f.Final.TransferItems = new() { [key] = [root, Item(root.Id.ToString(), "main")] };
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Service.End(f.Session, f.Final,
            _ => throw new Exception("native must not execute"), default));
        Assert.Equal(ScavPhase.Prepared, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
        f.Final.TransferItems[key] = [root];
        await f.Service.End(f.Session, f.Final, async request =>
        {
            Assert.Null(request.TransferItems);
            f.Service.NativePmcHandled(f.Session, f.Full);
            await f.Service.Save(f.Session, f.WriteProfile, default);
        }, default);
        Assert.Equal(ScavPhase.Committed, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
    }

    public class NullLog : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method!.ReturnType == typeof(void) ? null : method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
    }
    private static ISptLogger<T> Logger<T>() => DispatchProxy.Create<ISptLogger<T>, NullLog>();

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
            Full = new() { ProfileInfo = new() { ProfileId = Session }, CharacterData = new() { PmcData = Pmc, ScavData = Profile() }, DialogueRecords = [] };
            var saves = new SaveServer(new FileUtil(), [], Json, null!, null!, null!, Logger<SaveServer>(), null!);
            saves.AddProfile(Full);
            var templates = new TemplateTable { Character = [], CustomisationStorage = [], Items = new() { [Template] = null! },
                Prestige = null!, Quests = [], RepeatableQuests = null!, Handbook = null!, Customization = [], Dialogue = null!,
                Profiles = [], Prices = [], DefaultEquipmentPresets = [], Achievements = [], CustomAchievements = [], LocationServices = null! };
            var inRaid = new InRaidHelper(Logger<InRaidHelper>(), templates, null!, null!,
                new LostOnDeathConfig { Equipment = new() { FirstPrimaryWeapon = true, Backpack = true, PocketItems = true },
                    SpecialSlotItems = true, QuestItems = true }, new ServerCloner());
            Service = new(saves, Json, new ServerCloner(), null!, null!, null!, inRaid, templates, Logger<ScavContinuationService>());
            Journal = new ScavJournal(JournalDirectory);
            typeof(ScavContinuationService).GetField("journal", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Service, Journal);
            Frozen = new() { ServerId = Raid, LostInsuredItems = [], Results = new() { Profile = Pmc, Result = ExitStatus.KILLED, PlayTime = 12 } };
            Scav.Inventory!.Items!.Add(Item(Scav.Inventory.Equipment.ToString(), "FirstPrimaryWeapon"));
            Scav.Info!.Experience = 99999;
            Final = new() { ServerId = Raid, LostInsuredItems = [], Results = new() { Profile = Scav, Result = ExitStatus.SURVIVED,
                PlayTime = 200, ExitName = "car-extract" } };
        }
        public ScavJournalEntry Entry() => new()
        {
            SessionId = Session.ToString(), InitialServerId = Raid, PmcProfileId = Pmc.Id.ToString()!, ScavProfileId = Scav.Id.ToString()!,
            RequestId = Guid.NewGuid().ToString("N"), PrepareHash = ScavJournal.Hash(Json.Serialize(Frozen)!),
            FrozenPmcJson = Json.Serialize(Frozen)!, OriginalInsuredJson = "[]", ScavJson = Json.Serialize(Scav)!,
            GenerationHash = "generation", Token = new string('0',64), ExpiresAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()+600,
            ReceiptId = new MongoId().ToString(), Phase = ScavPhase.Prepared
        };
        public async Task<long> WriteProfile()
        {
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

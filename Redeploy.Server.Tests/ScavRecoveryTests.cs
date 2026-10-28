using Redeploy.Server.Scav;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using Xunit;

namespace Redeploy.Server.Tests;

public sealed partial class ScavContinuationTests
{
    private static Task<ScavRecoveryResponse> Recover(Fixture f, string id,
        Func<EndLocalRaidRequestData, Task>? pmc = null,
        Func<PmcData, PmcData, EndLocalRaidRequestData, Task>? scav = null,
        Func<Task<long>>? save = null)
        => f.Service.RecoverAbandon(f.Session, new() { RecoveryId = id }, pmc ?? f.NativeEnd,
            scav ?? f.NativeScavOutcome, save ?? f.WriteProfile, default,
            outerEnd: request => f.Service.End(f.Session, request, pmc ?? f.NativeEnd, scav ?? f.NativeScavOutcome, default));

    [Theory] [InlineData(ScavPhase.Prepared)] [InlineData(ScavPhase.Active)]
    public async Task RestartRecoveryForfeitsBothLivesOnceAndUnblocksRaid(ScavPhase phase)
    {
        using var f = new Fixture(); var entry = f.Entry(); entry.Phase = phase; entry.ExpiresAt = 1;
        f.Journal.Write(entry); await f.WriteProfile(); f.Writes = 0;
        f.RestartService(true);
        string before = f.Json.Serialize(f.Full)!;
        var status = await f.Service.RecoveryStatus(f.Session, default);
        Assert.True(status.Success); Assert.Equal("abandon", status.State); Assert.NotNull(status.RecoveryId);
        Assert.Equal("abandon", status.Action);
        Assert.Equal(before, f.Json.Serialize(f.Full)); Assert.Equal(0, f.Writes);
        Assert.DoesNotContain(entry.Token, f.Json.Serialize(status)!);
        f.RestartService();
        Assert.Equal(status.RecoveryId, (await f.Service.RecoveryStatus(f.Session, default)).RecoveryId);
        f.Final.Results!.PlayTime = 0;
        var recovered = await Recover(f, status.RecoveryId!);
        Assert.True(recovered.Success, recovered.Message); Assert.Equal("none", recovered.State);
        Assert.Equal(["pmc", "scav"], f.NativeStages); Assert.Equal(1, f.Writes);
        Assert.Equal(123, f.Full.CharacterData!.PmcData!.Info!.Experience);
        Assert.Equal(123, f.Full.CharacterData.ScavData!.Info!.Experience);
        Assert.True(f.Full.CharacterData.ScavData.Info.SavageLockTime > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var cooldown = f.Full.CharacterData.ScavData.Info.SavageLockTime;
        string equipment = f.Full.CharacterData.ScavData.Inventory!.Equipment.ToString()!;
        var repeated = await Recover(f, status.RecoveryId!);
        Assert.True(repeated.Success); Assert.Equal(1, f.Writes); Assert.Equal(2, f.NativeStages.Count);
        Assert.Equal(cooldown, f.Full.CharacterData.ScavData.Info.SavageLockTime);
        Assert.Equal(equipment, f.Full.CharacterData.ScavData.Inventory.Equipment.ToString());
        f.Service.BeforeStart(f.Session);
        Assert.True(f.Service.BlocksScavSave(f.Session, entry.ScavEquipmentId));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default));
    }

    [Theory] [InlineData("account")] [InlineData("pmc")] [InlineData("scav")] [InlineData("equipment")]
    public async Task RecoveryRejectsForeignOrChangedIdentityWithoutNativeCalls(string changed)
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        var status = await f.Service.RecoveryStatus(f.Session, default);
        if (changed == "pmc") f.Full.CharacterData!.PmcData!.Id = new MongoId();
        if (changed == "scav") f.Full.CharacterData!.ScavData!.Id = new MongoId();
        if (changed == "equipment") f.Full.CharacterData!.ScavData!.Inventory!.Equipment = new MongoId();
        var result = changed == "account"
            ? await f.Service.RecoverAbandon(new MongoId(), new() { RecoveryId = status.RecoveryId! }, f.NativeEnd, f.NativeScavOutcome, f.WriteProfile, default)
            : await Recover(f, status.RecoveryId!);
        Assert.False(result.Success); Assert.Equal("blocked", result.State);
        Assert.Equal(0, f.Writes); Assert.Empty(f.NativeStages);
        Assert.Equal(ScavPhase.Prepared, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
    }

    [Fact] public async Task CapturedInsuranceIncludesDroppedGraphsButExcludesRetainedAndAlreadyScheduledItems()
    {
        using var f = new Fixture();
        var dropped = Item("former-corpse", "FirstPrimaryWeapon");
        var attachment = Item(dropped.Id.ToString(), "mod_scope");
        var kept = Item(f.Pmc.Inventory!.Equipment.ToString(), "Scabbard");
        var keptChild = Item(kept.Id.ToString(), "mod");
        var alreadyScheduled = Item("old-corpse", "Backpack");
        f.Pmc.Inventory.Items!.AddRange([kept, keptChild]);
        Item[] all = [dropped, attachment, kept, keptChild, alreadyScheduled];
        f.Pmc.InsuredItems = all.Select(x => new InsuredItem { ItemId = x.Id, TId = Traders.PRAPOR }).ToList();
        f.Frozen.LostInsuredItems = all;
        f.Full.InsuranceList = [new() { Items = [alreadyScheduled] }];
        var entry = f.Entry(); entry.PmcInsuranceCaptured = true;
        entry.OriginalInsuredJson = f.Json.Serialize(f.Pmc.InsuredItems)!; f.Journal.Write(entry);
        var status = await f.Service.RecoveryStatus(f.Session, default);
        Assert.Equal("abandon", status.State);
        f.Final.Results!.PlayTime = 0;
        var result = await Recover(f, status.RecoveryId!, async request =>
        {
            Assert.Equal(new[] { dropped.Id, attachment.Id }, request.LostInsuredItems!.Select(x => x.Id));
            Assert.Equal(dropped.Id.ToString(), request.LostInsuredItems!.Last().ParentId);
            f.Full.InsuranceList.Add(new() { Items = request.LostInsuredItems!.ToList() });
            await f.NativeEnd(request);
        });
        Assert.True(result.Success, result.Message);
        await Recover(f, status.RecoveryId!);
        Assert.Equal(2, f.Full.InsuranceList.Count); Assert.Equal(1, f.Writes);
    }

    [Theory] [InlineData(1, false)] [InlineData(2, false)] [InlineData(2, true)]
    public async Task LegacyInsuranceCaptureFailsClosedUnlessProvenZero(int version, bool zeroInsurance)
    {
        using var f = new Fixture(); var entry = f.Entry(); entry.Version = version;
        if (!zeroInsurance) entry.OriginalInsuredJson = f.Json.Serialize(new[] { new InsuredItem { ItemId = new MongoId(), TId = Traders.PRAPOR } })!;
        f.Journal.Write(entry);
        var result = await f.Service.RecoveryStatus(f.Session, default);
        Assert.Equal(version == 2 && zeroInsurance ? "abandon" : "blocked", result.State);
        Assert.Equal(0, f.Writes); Assert.Empty(f.NativeStages);
        Assert.Equal(version, f.Journal.Read(f.Session.ToString(), f.Raid)!.Version);
    }

    [Fact] public async Task PrepareValidatesAndPersistsExplicitInsuranceCapture()
    {
        using var f = new Fixture(); var lost = Item("corpse", "FirstPrimaryWeapon");
        f.Pmc.InsuredItems = [new() { ItemId = lost.Id, TId = Traders.PRAPOR }];
        f.Frozen.LostInsuredItems = [lost];
        f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = f.Raid });
        var request = f.PrepareRequest(); request.PmcInsuranceCaptured = true;
        Assert.True((await f.Service.Prepare(f.Session, request, default)).Success);
        var entry = f.Journal.Read(f.Session.ToString(), f.Raid)!;
        Assert.True(entry.PmcInsuranceCaptured);
        Assert.Equal(lost.Id, Assert.Single(f.Json.Deserialize<EndLocalRaidRequestData>(entry.FrozenPmcJson)!.LostInsuredItems!).Id);
        request.PmcInsuranceCaptured = false;
        Assert.Equal("continuation-already-prepared", (await f.Service.Prepare(f.Session, request, default)).Error);
    }

    [Theory] [InlineData("missing")] [InlineData("foreign")] [InlineData("duplicate")]
    public async Task InvalidInsuranceCaptureCannotReserveScav(string invalid)
    {
        using var f = new Fixture(); var lost = Item("corpse", "FirstPrimaryWeapon");
        f.Pmc.InsuredItems = [new() { ItemId = lost.Id, TId = Traders.PRAPOR }];
        f.Frozen.LostInsuredItems = invalid switch { "missing" => null, "foreign" => [Item()], _ => [lost, lost] };
        f.Service.RegisterStart(f.Session, new() { PlayerSide = "pmc" }, new() { ServerId = f.Raid });
        var request = f.PrepareRequest(); request.PmcInsuranceCaptured = true;
        Assert.False((await f.Service.Prepare(f.Session, request, default)).Success);
        Assert.Null(f.Journal.Read(f.Session.ToString(), f.Raid)); Assert.Equal(0, f.Writes);
    }

    [Fact] public async Task RestartPartialTransferUsesCurrentScavAndRetainsMovedStashItems()
    {
        using var f = new Fixture(); var entry = f.Entry(); f.Journal.Write(entry);
        await f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default);
        var loot = f.Full.CharacterData!.ScavData!.Inventory!.Items!.Last();
        var stash = Item(); f.Full.CharacterData.PmcData!.Inventory!.Items!.Add(stash);
        await f.Service.WithTransferInventory(f.Session, async () =>
        {
            f.Full.CharacterData.ScavData!.Inventory!.Items!.Remove(loot);
            loot.ParentId = stash.Id.ToString(); loot.SlotId = "hideout";
            f.Full.CharacterData.PmcData!.Inventory!.Items!.Add(loot);
            await f.Service.Save(f.Session, f.WriteProfile, default); return true;
        }, default);
        f.RestartService(true);
        var status = await f.Service.RecoveryStatus(f.Session, default);
        Assert.Equal("transfer", status.State); Assert.Equal(entry.ScavEquipmentId, status.ScavEquipmentId);
        Assert.Equal(entry.ScavProfileId, status.ScavProfileId);
        Assert.DoesNotContain(f.Full.CharacterData.ScavData!.Inventory!.Items!, x => x.Id == loot.Id);
        Assert.Single(f.Full.CharacterData.PmcData!.Inventory!.Items!, x => x.Id == loot.Id);
        int calls = 0;
        ValueTask<string> Regenerate()
        {
            calls++; var next = Profile(); next.Id = f.Scav.Id; f.Full.CharacterData.ScavData = next;
            return ValueTask.FromResult("recovered-native-transfer");
        }
        Assert.Equal("recovered-native-transfer", await f.Service.FinishTransfer(f.Session, Regenerate, f.WriteProfile, default));
        Assert.Equal("recovered-native-transfer", await f.Service.FinishTransfer(f.Session, Regenerate, f.WriteProfile, default));
        Assert.Equal(1, calls); Assert.Equal("none", (await f.Service.RecoveryStatus(f.Session, default)).State);
        Assert.Single(f.Full.CharacterData.PmcData.Inventory.Items!, x => x.Id == loot.Id);
        Assert.True(f.Service.BlocksScavSave(f.Session, entry.ScavEquipmentId)); f.Service.BeforeStart(f.Session);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task CompleteOutcomeSaveFailureRollsForwardWithoutRepeatingNative(bool restart)
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        await f.WriteProfile(); f.Writes = 0;
        f.Final.Results!.Result = ExitStatus.KILLED;
        await Assert.ThrowsAsync<IOException>(() => f.Service.End(f.Session, f.Final, f.NativeEnd, async (pmc, scav, request) =>
        {
            await f.NativeScavOutcome(pmc, scav, request);
            f.Full.InsuranceList = [new() { Items = [Item()] }];
            await f.Service.Save(f.Session, () => throw new IOException("disk full"), default);
        }, default));
        string recorded = f.Journal.Read(f.Session.ToString(), f.Raid)!.ExpectedProfileJson!;
        Assert.NotNull(recorded);
        if (restart) f.RestartService(true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.Save(f.Session, f.WriteProfile, default));
        var status = await f.Service.RecoveryStatus(f.Session, default);
        Assert.Equal("abandon", status.State);
        var result = await Recover(f, status.RecoveryId!, _ => throw new Exception("must not replay"), (_, _, _) => throw new Exception("must not replay"));
        Assert.Equal("save", status.Action);
        Assert.True(result.Success, result.Message); Assert.Equal("none", result.State);
        Assert.Equal(recorded, f.Json.Serialize(f.Full)); Assert.Equal(1, f.Writes);
        Assert.Single(f.Full.InsuranceList!); Assert.Equal(2, f.NativeStages.Count);
        Assert.Equal("none", (await Recover(f, status.RecoveryId!)).State);
        Assert.Equal(1, f.Writes); f.Service.BeforeStart(f.Session);
    }

    [Fact] public async Task TransferCompletionSaveFailureCanRecoverRecordedSuccessorAfterRestart()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        await f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default);
        int calls = 0;
        ValueTask<string> Regenerate()
        {
            calls++; var next = Profile(); next.Id = f.Scav.Id;
            next.Info!.SavageLockTime = 9876543210; f.Full.CharacterData!.ScavData = next;
            return ValueTask.FromResult("native-recovered-response");
        }
        await Assert.ThrowsAsync<IOException>(async () => await f.Service.FinishTransfer(f.Session, Regenerate,
            () => throw new IOException("disk full"), default));
        string expected = f.Journal.Read(f.Session.ToString(), f.Raid)!.TransferExpectedProfileJson!;
        f.RestartService(true);
        var status = await f.Service.RecoveryStatus(f.Session, default); Assert.Equal("abandon", status.State);
        Assert.True((await Recover(f, status.RecoveryId!, _ => throw new Exception("must not replay"), (_, _, _) => throw new Exception("must not replay"))).Success);
        Assert.Equal(expected, f.Json.Serialize(f.Full)); Assert.Equal(1, calls);
        Assert.Equal("native-recovered-response", await f.Service.FinishTransfer(f.Session, Regenerate, f.WriteProfile, default));
        Assert.Equal(1, calls); f.Service.BeforeStart(f.Session);
    }

    [Theory] [InlineData("partial-native")] [InlineData("foreign-disk")] [InlineData("changed-memory")]
    public async Task RecoveryNeverReplaysAmbiguousPartialNativeOutcome(string fault)
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry()); await f.WriteProfile(); f.Writes = 0;
        await Assert.ThrowsAsync<IOException>(() => f.Service.End(f.Session, f.Final, async request =>
        {
            await f.NativeEnd(request);
            if (fault == "partial-native") throw new IOException("interrupted native handler");
        }, async (pmc, scav, request) =>
        {
            await f.NativeScavOutcome(pmc, scav, request);
            await f.Service.Save(f.Session, () => throw new IOException("disk full"), default);
        }, default));
        if (fault == "foreign-disk")
        {
            var changed = f.Json.Deserialize<SptProfile>(File.ReadAllText(f.ProfilePath))!;
            changed.CharacterData!.PmcData!.Info!.Experience = 999;
            File.WriteAllText(f.ProfilePath, f.Json.Serialize(changed)!);
        }
        if (fault == "changed-memory") f.Full.CharacterData!.PmcData!.Info!.Experience = 999;
        var status = await f.Service.RecoveryStatus(f.Session, default);
        Assert.Equal("blocked", status.State); Assert.Equal(0, f.Writes);
        Assert.Throws<InvalidOperationException>(() => f.Service.BeforeStart(f.Session));
    }

    [Fact] public async Task StatusReconcilesExactSavedOutcomeWithoutAnotherSaveOrNativeCall()
    {
        using var f = new Fixture(); var entry = f.Entry(); entry.Phase = ScavPhase.Settling;
        entry.ExpectedProfileHash = ScavJournal.Hash(f.Json.Serialize(f.Full)!);
        entry.TransferPhase = ScavTransferPhase.Complete; f.Journal.Write(entry); await f.WriteProfile(); f.Writes = 0;
        f.RestartService(true);
        var status = await f.Service.RecoveryStatus(f.Session, default);
        Assert.Equal("none", status.State); Assert.Equal(0, f.Writes); Assert.Empty(f.NativeStages);
        f.Service.BeforeStart(f.Session);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task TransferMoveIsPersistedBeforeAcknowledgmentEvenWithoutNativeSave(bool lostSaveAcknowledgement)
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        await f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default);
        var loot = f.Full.CharacterData!.ScavData!.Inventory!.Items!.Last();
        var result = await f.Service.WithTransferInventory(f.Session, () =>
        {
            f.Full.CharacterData.ScavData!.Inventory!.Items!.Remove(loot);
            loot.ParentId = f.Full.CharacterData.PmcData!.Inventory!.Equipment.ToString();
            f.Full.CharacterData.PmcData.Inventory.Items!.Add(loot);
            return ValueTask.FromResult(42);
        }, default, async () =>
        {
            var entry = f.Journal.Read(f.Session.ToString(), f.Raid)!;
            Assert.True(entry.InventoryMutationPending); Assert.NotNull(entry.InventoryExpectedProfileJson);
            long written = await f.WriteProfile();
            if (lostSaveAcknowledgement) throw new IOException("lost save acknowledgement");
            return written;
        });
        Assert.Equal(42, result); Assert.Equal(2, f.Writes);
        Assert.False(f.Journal.Read(f.Session.ToString(), f.Raid)!.InventoryMutationPending);
        f.RestartService(true);
        Assert.Single(f.Full.CharacterData.PmcData!.Inventory!.Items!, x => x.Id == loot.Id);
        Assert.DoesNotContain(f.Full.CharacterData.ScavData!.Inventory!.Items!, x => x.Id == loot.Id);
        Assert.Equal("transfer", (await f.Service.RecoveryStatus(f.Session, default)).State);
    }

    [Fact] public async Task TransferMoveSaveFailureResumesRecordedMoveWithoutReplayingIt()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        await f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default);
        var loot = f.Full.CharacterData!.ScavData!.Inventory!.Items!.Last();
        int moves = 0;
        await Assert.ThrowsAsync<IOException>(async () => await f.Service.WithTransferInventory(f.Session, () =>
        {
            moves++;
            f.Full.CharacterData.ScavData!.Inventory!.Items!.Remove(loot);
            loot.ParentId = f.Full.CharacterData.PmcData!.Inventory!.Equipment.ToString();
            f.Full.CharacterData.PmcData.Inventory.Items!.Add(loot);
            return ValueTask.FromResult(42);
        }, default, () => throw new IOException("disk full")));
        f.RestartService(true);
        Assert.Contains(f.Full.CharacterData.ScavData!.Inventory!.Items!, x => x.Id == loot.Id);
        var status = await f.Service.RecoveryStatus(f.Session, default);
        Assert.Equal("abandon", status.State);
        Assert.Equal("transfer", (await Recover(f, status.RecoveryId!, _ => throw new Exception("no replay"),
            (_, _, _) => throw new Exception("no replay"))).State);
        Assert.Equal(1, moves); Assert.Single(f.Full.CharacterData.PmcData!.Inventory!.Items!, x => x.Id == loot.Id);
        Assert.DoesNotContain(f.Full.CharacterData.ScavData.Inventory.Items!, x => x.Id == loot.Id);
        Assert.False(f.Journal.Read(f.Session.ToString(), f.Raid)!.InventoryMutationPending);
    }

    [Fact] public async Task NativeTransferMutationFailureWithoutCompleteOutcomeRemainsBlocked()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        await f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default);
        await Assert.ThrowsAsync<IOException>(async () => await f.Service.WithTransferInventory<int>(f.Session, () =>
        {
            f.Full.CharacterData!.ScavData!.Inventory!.Items!.RemoveAt(1);
            throw new IOException("native item move failed partway through");
        }, default, f.WriteProfile));
        f.RestartService(true);
        Assert.Equal("blocked", (await f.Service.RecoveryStatus(f.Session, default)).State);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Service.WithTransferInventory(f.Session,
            () => ValueTask.FromResult(1), default, f.WriteProfile));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Service.Save(f.Session, f.WriteProfile, default));
        Assert.Equal(1, f.Writes);
    }

    [Fact] public async Task ActualNativeSaveCacheAfterFailedAtomicWriteDoesNotPreventRecoveryRetry()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        await f.Service.SaveNativeProfile(f.Session);
        f.Final.Results!.Result = ExitStatus.KILLED;
        // FileUtil writes to this exact temporary path before replacing the real
        // profile. A directory makes that attempted atomic write fail reliably.
        string blockedWrite = f.ProfilePath + ".bak";
        Directory.CreateDirectory(blockedWrite);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Service.End(f.Session, f.Final,
            f.NativeEnd, async (pmc, scav, request) =>
            {
                await f.NativeScavOutcome(pmc, scav, request);
                await f.Service.Save(f.Session, () => f.Service.SaveNativeProfile(f.Session), default);
            }, default));
        Directory.Delete(blockedWrite); // Exact empty fixture-owned directory, never recursive.
        string expected = f.Journal.Read(f.Session.ToString(), f.Raid)!.ExpectedProfileJson!;
        Assert.NotEqual(expected, File.ReadAllText(f.ProfilePath));
        // Prove the native bug: cached attempted MD5 suppresses this write.
        await f.Service.SaveNativeProfile(f.Session);
        Assert.NotEqual(expected, File.ReadAllText(f.ProfilePath));
        var status = await f.Service.RecoveryStatus(f.Session, default);
        Assert.Equal("save", status.Action);
        Assert.True((await Recover(f, status.RecoveryId!, _ => throw new Exception("no native replay"),
            (_, _, _) => throw new Exception("no native replay"), () => f.Service.SaveNativeProfile(f.Session))).Success);
        Assert.Equal(expected, File.ReadAllText(f.ProfilePath));
        Assert.Equal(2, f.NativeStages.Count);
    }

    [Theory] [InlineData(ScavPhase.Prepared)] [InlineData(ScavPhase.Active)]
    public async Task LoginNeverImplicitlyAbandonsAnUnfinishedLife(ScavPhase phase)
    {
        using var f = new Fixture(); var entry = f.Entry(); entry.Phase = phase; f.Journal.Write(entry);
        int logins = 0;
        Assert.Equal("native-login", await f.Service.BeforeLogin(f.Session, () =>
        {
            logins++;
            return ValueTask.FromResult("native-login");
        }, default));
        Assert.Equal(1, logins); Assert.Equal(0, f.Writes); Assert.Empty(f.NativeStages);
        Assert.Equal(phase, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
        Assert.Equal("abandon", (await f.Service.RecoveryStatus(f.Session, default)).State);
    }

    [Fact] public async Task LoginPersistsCompleteRecordedOutcomeBeforeNativeProfileTimestampMutations()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry()); await f.WriteProfile(); f.Writes = 0;
        f.Final.Results!.Result = ExitStatus.KILLED;
        await Assert.ThrowsAsync<IOException>(() => f.Service.End(f.Session, f.Final, f.NativeEnd,
            async (pmc, scav, request) =>
            {
                await f.NativeScavOutcome(pmc, scav, request);
                await f.Service.Save(f.Session, () => throw new IOException("interrupted save"), default);
            }, default));
        var entry = f.Journal.Read(f.Session.ToString(), f.Raid)!;
        string outcome = entry.ExpectedProfileJson!;
        f.RestartService(true);
        Assert.Equal("native-login", await f.Service.BeforeLogin(f.Session, () =>
        {
            Assert.Equal(outcome, f.Json.Serialize(f.Full));
            Assert.Equal(outcome, f.Json.Serialize(f.Json.DeserializeFromFile<SptProfile>(f.ProfilePath)));
            // Equivalent to GameStart's health/time/hideout changes: these must
            // happen after proof, not make a recoverable restart ambiguous.
            f.Full.CharacterData!.PmcData!.Info!.LastTimePlayedAsSavage = 444;
            return ValueTask.FromResult("native-login");
        }, default, outerEnd: request => f.Service.End(f.Session, request, f.NativeEnd, f.NativeScavOutcome, default)));
        Assert.Equal("none", (await f.Service.RecoveryStatus(f.Session, default)).State);
        Assert.Equal(444, f.Full.CharacterData!.PmcData!.Info!.LastTimePlayedAsSavage);
        Assert.Equal(2, f.NativeStages.Count);
    }

    [Fact] public async Task RaidStartWaitsForTransferCommitAndRetiresItsTombstoneAfterNativeStart()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        await f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default);
        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = f.Service.FinishTransfer(f.Session, () =>
        {
            var next = Profile(); next.Id = f.Scav.Id; f.Full.CharacterData!.ScavData = next;
            return ValueTask.FromResult("complete");
        }, async () =>
        {
            long result = await f.WriteProfile(); saved.SetResult(); await release.Task; return result;
        }, default).AsTask();
        await saved.Task;
        bool started = false;
        var start = f.Service.WithRaidStart(f.Session, new() { PlayerSide = "pmc" }, async () =>
        {
            started = true;
            await f.Service.Save(f.Session, f.WriteProfile, default); // writer lock must be free
            return new StartLocalRaidResponseData { ServerId = "factory4_day.pmc next-raid" };
        }, default);
        Assert.False(started); Assert.False(start.IsCompleted);
        release.SetResult(); await finish; await start;
        Assert.True(started);
        Assert.Equal(ScavTransferPhase.Retired, f.Journal.Read(f.Session.ToString(), f.Raid)!.TransferPhase);
        Assert.False(f.Service.OwnsRegeneration(f.Session));
    }

    [Fact] public async Task OwnedTransferRejectsSellAllBeforeMailOrInventoryMutation()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        await f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default);
        string before = f.Json.Serialize(f.Full)!; int writes = f.Writes;
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await f.Service.WithTransferInventory<int>(f.Session,
            () => throw new Exception("native sale mail must not run"), default,
            f.WriteProfile, ItemEventActions.SELL_ALL_FROM_SAVAGE));
        Assert.Equal(before, f.Json.Serialize(f.Full)); Assert.Equal(writes, f.Writes);
        Assert.False(f.Journal.Read(f.Session.ToString(), f.Raid)!.InventoryMutationPending);
        Assert.Equal("transfer", (await f.Service.RecoveryStatus(f.Session, default)).State);
    }

    [Fact] public async Task OrdinaryScavSellAllRemainsNative()
    {
        using var f = new Fixture(); int sales = 0;
        Assert.Equal("native-sale", await f.Service.WithTransferInventory(f.Session, () =>
        {
            sales++; return ValueTask.FromResult("native-sale");
        }, default, f.WriteProfile, ItemEventActions.SELL_ALL_FROM_SAVAGE));
        Assert.Equal(1, sales); Assert.Equal(0, f.Writes); Assert.Empty(f.Journal.ForSession(f.Session.ToString()));
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task RecoveryEndsTheExactOuterLifecycleAfterClientOrServerRestart(bool serverRestart)
    {
        using var f = new Fixture(); var entry = f.Entry(); entry.Phase = ScavPhase.Active; f.Journal.Write(entry);
        await f.WriteProfile(); f.Writes = 0;
        if (serverRestart) f.RestartService(true);
        bool outerActive = !serverRestart; int outerCalls = 0;
        var status = await f.Service.RecoveryStatus(f.Session, default); f.Final.Results!.PlayTime = 0;
        async Task Outer(EndLocalRaidRequestData result)
        {
            outerCalls++; Assert.Equal(f.Raid, result.ServerId);
            // Models the existing ordinary lifecycle holding its own gate while
            // the inner production service takes Redeploy's account gate.
            await f.Service.End(f.Session, result, f.NativeEnd, f.NativeScavOutcome, default);
            outerActive = false;
        }
        var recovered = await f.Service.RecoverAbandon(f.Session, new() { RecoveryId = status.RecoveryId! },
            f.NativeEnd, f.NativeScavOutcome, f.WriteProfile, default, Outer).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(recovered.Success, recovered.Message); Assert.Equal("none", recovered.State);
        Assert.False(outerActive); Assert.Equal(1, outerCalls); Assert.Equal(1, f.Writes);
        Assert.Equal(2, f.NativeStages.Count); f.Service.BeforeStart(f.Session);
    }

    [Fact] public async Task DurableAbandonIntentRejectsDelayedCancelAndPrepareBeforeOuterSettlement()
    {
        using var f = new Fixture(); var entry = f.Entry(); f.Journal.Write(entry);
        var status = await f.Service.RecoveryStatus(f.Session, default); f.Final.Results!.PlayTime = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovery = f.Service.RecoverAbandon(f.Session, new() { RecoveryId = status.RecoveryId! },
            f.NativeEnd, f.NativeScavOutcome, f.WriteProfile, default, async result =>
            {
                entered.SetResult(); await release.Task;
                await f.Service.End(f.Session, result, f.NativeEnd, f.NativeScavOutcome, default);
            });
        await entered.Task;
        var control = await f.Service.Control(f.Session, new() { InitialServerId = f.Raid, Token = entry.Token }, true, default);
        Assert.False(control.Success); Assert.Equal("recovery-already-requested", control.Error);
        var request = f.PrepareRequest(); request.ClientRequestId = entry.RequestId;
        var prepare = await f.Service.Prepare(f.Session, request, default);
        Assert.False(prepare.Success); Assert.Null(prepare.Token);
        Assert.Equal(ScavPhase.Prepared, f.Journal.Read(f.Session.ToString(), f.Raid)!.Phase);
        release.SetResult(); Assert.True((await recovery).Success); Assert.Equal(1, f.Writes);
    }

    [Fact] public async Task ARealLateEndMayWinWithoutBeingOverwrittenByRecoveryForfeit()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        var status = await f.Service.RecoveryStatus(f.Session, default);
        var recovered = await f.Service.RecoverAbandon(f.Session, new() { RecoveryId = status.RecoveryId! },
            f.NativeEnd, f.NativeScavOutcome, f.WriteProfile, default, async _ =>
            {
                await f.Service.End(f.Session, f.Final, f.NativeEnd, f.NativeScavOutcome, default);
                // The real outer task succeeded, while the synthetic conflicting
                // request could receive a transport/conflict failure afterwards.
                await f.Service.AcknowledgeOuterEnd(f.Session, f.Final, default);
                throw new IOException("conflicting recovery callback response");
            }).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(recovered.Success); Assert.Equal("transfer", recovered.State);
        Assert.Equal(99999, f.Full.CharacterData!.ScavData!.Info!.Experience);
        Assert.Equal(2, f.NativeStages.Count); Assert.Equal(1, f.Writes);
    }

    [Fact] public async Task OuterAcknowledgementFailureRetainsExactReplayUntilFullCallbackSuccess()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry()); f.Final.Results!.PlayTime = 0;
        var status = await f.Service.RecoveryStatus(f.Session, default); int outerCalls = 0;
        async Task FailedOuter(EndLocalRaidRequestData result)
        {
            outerCalls++;
            await f.Service.End(f.Session, result, f.NativeEnd, f.NativeScavOutcome, default);
            throw new IOException("outer lifecycle completion failed");
        }
        var failed = await f.Service.RecoverAbandon(f.Session, new() { RecoveryId = status.RecoveryId! },
            f.NativeEnd, f.NativeScavOutcome, f.WriteProfile, default, FailedOuter);
        Assert.Equal("save", failed.Action);
        var pending = f.Journal.Read(f.Session.ToString(), f.Raid)!;
        Assert.Equal(ScavPhase.Committed, pending.Phase); Assert.NotNull(pending.OuterEndReplayJson);
        Assert.Throws<InvalidOperationException>(() => f.Service.BeforeStart(f.Session));
        var wrong = f.Json.Deserialize<EndLocalRaidRequestData>(pending.OuterEndReplayJson!)!;
        wrong.Results!.PlayTime = 17;
        await f.Service.AcknowledgeOuterEnd(f.Session, wrong, default);
        Assert.NotNull(f.Journal.Read(f.Session.ToString(), f.Raid)!.OuterEndReplayJson);
        async Task CompletedOuter(EndLocalRaidRequestData result)
        {
            outerCalls++;
            Assert.Equal(pending.FinalHash, ScavJournal.Hash(f.Json.Serialize(result)!));
            await f.Service.End(f.Session, result, _ => throw new Exception("must not repeat PMC"),
                (_, _, _) => throw new Exception("must not repeat Scav"), default);
        }
        var complete = await f.Service.RecoverAbandon(f.Session, new() { RecoveryId = status.RecoveryId! },
            f.NativeEnd, f.NativeScavOutcome, f.WriteProfile, default, CompletedOuter);
        Assert.Equal("none", complete.State); Assert.Equal(2, outerCalls); Assert.Equal(1, f.Writes);
        Assert.Null(f.Journal.Read(f.Session.ToString(), f.Raid)!.OuterEndReplayJson);
        f.Service.BeforeStart(f.Session);
    }

    [Fact] public async Task OuterSkipCannotAcknowledgeAnUnsettledRecoveryAndReleasesAccountGate()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry());
        var status = await f.Service.RecoveryStatus(f.Session, default);
        var skipped = await f.Service.RecoverAbandon(f.Session, new() { RecoveryId = status.RecoveryId! },
            f.NativeEnd, f.NativeScavOutcome, f.WriteProfile, default, _ => Task.CompletedTask);
        Assert.False(skipped.Success); Assert.Equal("blocked", skipped.State);
        var retry = await f.Service.RecoveryStatus(f.Session, default).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal("abandon", retry.Action); Assert.Equal(0, f.Writes); Assert.Empty(f.NativeStages);
    }

    [Fact] public async Task ConcurrentOuterRecoveryCoalescesAndKeepsRaidAdmissionFencedUntilCallbackFinishes()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry()); f.Final.Results!.PlayTime = 0;
        var status = await f.Service.RecoveryStatus(f.Session, default);
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int outerCalls = 0;
        async Task Outer(EndLocalRaidRequestData result)
        {
            outerCalls++;
            await f.Service.End(f.Session, result, f.NativeEnd, f.NativeScavOutcome, default);
            settled.SetResult(); await release.Task;
        }
        Task<ScavRecoveryResponse> RecoverOuter() => f.Service.RecoverAbandon(f.Session,
            new() { RecoveryId = status.RecoveryId! }, f.NativeEnd, f.NativeScavOutcome, f.WriteProfile, default, Outer);
        var first = RecoverOuter(); await settled.Task;
        var second = RecoverOuter();
        Assert.False(first.IsCompleted); Assert.False(second.IsCompleted); Assert.Equal(1, outerCalls);
        Assert.Throws<InvalidOperationException>(() => f.Service.BeforeStart(f.Session));
        release.SetResult();
        Assert.All(await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3)), response => Assert.Equal("none", response.State));
        Assert.Equal(1, outerCalls); Assert.Equal(1, f.Writes); f.Service.BeforeStart(f.Session);
    }

    [Fact] public async Task StaleOuterRecoverySnapshotCannotReplayAfterAcknowledgementAndNewRaidRetirement()
    {
        using var f = new Fixture(); f.Journal.Write(f.Entry()); f.Final.Results!.PlayTime = 0;
        var status = await f.Service.RecoveryStatus(f.Session, default);
        ScavJournalEntry? stale = null;
        var response = await f.Service.RecoverAbandon(f.Session, new() { RecoveryId = status.RecoveryId! },
            f.NativeEnd, f.NativeScavOutcome, f.WriteProfile, default, async result =>
            {
                await f.Service.End(f.Session, result, f.NativeEnd, f.NativeScavOutcome, default);
                stale = f.Journal.Read(f.Session.ToString(), f.Raid);
            });
        Assert.Equal("none", response.State); Assert.NotNull(stale!.OuterEndReplayJson);
        await f.Service.WithRaidStart(f.Session, new() { PlayerSide = "pmc" },
            () => Task.FromResult(new StartLocalRaidResponseData { ServerId = "factory4_day.pmc later" }), default);
        var replay = typeof(ScavContinuationService).GetMethod("CompleteOuterRecovery",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        Func<EndLocalRaidRequestData, Task> mustNotRun = _ => throw new Exception("stale outer callback must not run");
        await (Task)replay.Invoke(f.Service, [f.Session, stale, mustNotRun])!;
        Assert.Equal(ScavTransferPhase.Retired, f.Journal.Read(f.Session.ToString(), f.Raid)!.TransferPhase);
        Assert.Equal(1, f.Writes);
    }
}

using System.Collections.Concurrent;
using System.Reflection;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Servers;

namespace Redeploy.Server.Scav;

public sealed partial class ScavContinuationService
{
    private readonly ConcurrentDictionary<string, OuterRecoveryExecution> outerRecoveries = new();
    private sealed class OuterRecoveryExecution(string recoveryId)
    {
        public readonly string RecoveryId = recoveryId;
        public Task Task = Task.CompletedTask;
    }
    public async ValueTask<T> BeforeLogin<T>(MongoId sessionId, Func<ValueTask<T>> native, CancellationToken ct,
        Func<EndLocalRaidRequestData, Task>? outerEnd = null)
    {
        using var lease = await AcquireLogin(sessionId, ct, outerEnd);
        return await native();
    }

    public async Task<AccountOperationLease?> AcquireLogin(MongoId sessionId, CancellationToken ct,
        Func<EndLocalRaidRequestData, Task>? outerEnd = null)
    {
        string id = sessionId.ToString();
        bool needsRecovery;
        try { needsRecovery = journal.ForSession(id).Any(ScavStatePolicy.BlocksNewRaid); }
        catch (Exception) { needsRecovery = true; } // Keep the menu available for a corrupt-journal explanation.
        if (!needsRecovery) return null;
        await Gate(id).WaitAsync(ct);
        bool acquiredSave = false;
        ScavJournalEntry? replay = null;
        try
        {
            await SaveGate(id).WaitAsync(ct); acquiredSave = true;
            var entries = journal.ForSession(id).Where(ScavStatePolicy.BlocksNewRaid).ToList();
            if (entries.Count == 1)
            {
                var entry = entries[0];
                ReconcileRecovery(sessionId, entry);
                if (CanRollForward(sessionId, entry))
                    await PersistRecordedOutcome(sessionId, entry, () => SaveNativeProfile(sessionId));
                if (entry.Phase == ScavPhase.Committed && entry.OuterEndReplayJson != null) replay = entry;
            }
        }
        catch (Exception)
        {
            // Keep the menu reachable so it can display the blocked recovery. No
            // unproven profile is restored and ordinary saves retain their fence.
            logger.Warning("Redeploy: an interrupted continuation still needs menu recovery; no native outcome was repeated.");
        }
        finally
        {
            if (acquiredSave) SaveGate(id).Release();
        }
        if (replay != null)
        {
            Gate(id).Release();
            try { await CompleteOuterRecovery(sessionId, replay, outerEnd); }
            catch (Exception) { logger.Warning("Redeploy: the exact outer raid acknowledgement remains pending for menu recovery."); }
            finally { await Gate(id).WaitAsync(CancellationToken.None); }
        }
        // Native GameStart updates health/time/hideout. Reconcile complete durable
        // outcomes first, while disk and the freshly loaded profile still agree.
        // An unfinished life is never abandoned implicitly during login.
        // Keep the account gate until native startup completes; only SaveGate is
        // released here, allowing native saves without admitting a late raid end.
        return new AccountOperationLease(this, sessionId, null);
    }

    // This identifies an operation, not a credential. The authenticated route's
    // session owns the journal lookup; neither client tokens nor snapshots leave it.
    private static string RecoveryId(ScavJournalEntry entry)
        => ScavJournal.Hash(entry.SessionId + "\n" + entry.InitialServerId + "\n" + entry.RequestId);

    public async Task<ScavRecoveryResponse> RecoveryStatus(MongoId sessionId, CancellationToken ct)
    {
        string id = sessionId.ToString();
        await Gate(id).WaitAsync(ct);
        bool acquiredSave = false;
        try
        {
            await SaveGate(id).WaitAsync(ct); acquiredSave = true;
            return RecoveryStatusCore(sessionId);
        }
        catch (Exception ex) { return RecoveryBlocked(ex); }
        finally { if (acquiredSave) SaveGate(id).Release(); Gate(id).Release(); }
    }

    private ScavRecoveryResponse RecoveryStatusCore(MongoId sessionId)
    {
        var entries = journal.ForSession(sessionId.ToString()).Where(ScavStatePolicy.BlocksNewRaid).ToList();
        foreach (var entry in entries) ReconcileRecovery(sessionId, entry);
        entries = entries.Where(ScavStatePolicy.BlocksNewRaid).ToList();
        if (entries.Count == 0) return new() { Success = true, Message = "No interrupted Scav continuation remains." };
        if (entries.Count != 1) throw new InvalidDataException("Multiple interrupted continuations require manual inspection.");
        var pending = entries[0];
        RequireCurrentVersion(pending);
        AssertRecoveryAccount(sessionId, pending);
        if (pending.Phase == ScavPhase.Committed && pending.OuterEndReplayJson != null)
            return new() { Success = true, State = "abandon", Action = "save", RecoveryId = RecoveryId(pending),
                Message = "Complete the saved raid acknowledgement. The recorded result, transferred items and cooldown will be preserved." };
        if (pending.Phase == ScavPhase.Committed && pending.TransferPhase == ScavTransferPhase.Pending && !pending.InventoryMutationPending)
        {
            AssertTransferScav(sessionId, pending);
            return new() { Success = true, State = "transfer", RecoveryId = RecoveryId(pending),
                ScavProfileId = pending.ScavProfileId, ScavEquipmentId = pending.ScavEquipmentId,
                Message = "Your extracted Scav still has loot to transfer. Items already moved to your stash remain there." };
        }
        if (pending.Phase is ScavPhase.Prepared or ScavPhase.Active)
        {
            _ = CreateAbandonedResult(sessionId, pending); // Prove this action before offering it.
            return new() { Success = true, State = "abandon", Action = "abandon", RecoveryId = RecoveryId(pending),
                Message = "End the interrupted raid: your PMC remains dead, your Scav equipment is forfeited, and the normal Scav cooldown applies." };
        }
        if (CanRollForward(sessionId, pending))
            return new() { Success = true, State = "abandon", Action = "save", RecoveryId = RecoveryId(pending),
                Message = "Complete the interrupted profile save using its recorded result. Already transferred items and the recorded cooldown are preserved." };
        throw new InvalidDataException("The interrupted native result cannot be proven complete. Recovery is blocked to protect inventory and insurance.");
    }

    public async Task<ScavRecoveryResponse> RecoverAbandon(MongoId sessionId, ScavRecoveryRequest request,
        Func<EndLocalRaidRequestData, Task> nativeEnd,
        Func<PmcData, PmcData, EndLocalRaidRequestData, Task> nativeScavOutcome,
        Func<Task<long>> nativeSave, CancellationToken ct,
        Func<EndLocalRaidRequestData, Task>? outerEnd = null)
    {
        string id = sessionId.ToString();
        await Gate(id).WaitAsync(ct);
        bool gateHeld = true;
        bool outerAttempted = false;
        try
        {
            if (request.RecoveryId?.Length != 64) throw new InvalidDataException("Invalid recovery identifier.");
            var entry = journal.ForSession(id).SingleOrDefault(x => RecoveryId(x) == request.RecoveryId)
                ?? throw new InvalidDataException("This recovery does not belong to the signed-in account.");
            RequireCurrentVersion(entry);
            AssertRecoveryAccount(sessionId, entry);
            await SaveGate(id).WaitAsync(ct);
            try { ReconcileRecovery(sessionId, entry); }
            finally { SaveGate(id).Release(); }
            // Committed tombstones make a lost recovery response safe to retry,
            // including after another admitted raid retired the transfer window.
            if (!ScavStatePolicy.BlocksNewRaid(entry)) return RecoveryStatusCore(sessionId);
            if (journal.ForSession(id).Count(ScavStatePolicy.BlocksNewRaid) != 1)
                throw new InvalidDataException("Multiple interrupted continuations require manual inspection.");
            if (entry.Phase is ScavPhase.Prepared or ScavPhase.Active)
            {
                var abandoned = CreateAbandonedResult(sessionId, entry);
                entry.RecoveryAbandoned = true;
                if (outerEnd != null) entry.OuterEndReplayJson = Serialize(abandoned);
                journal.Write(entry);
                if (outerEnd == null)
                    await EndCore(sessionId, abandoned, nativeEnd, nativeScavOutcome, ct, true);
                else
                {
                    // Preserve the normal outer-mod -> Redeploy lock order. An
                    // already-running outer end may be waiting for our gate; it
                    // must be allowed to finish instead of awaiting itself.
                    outerAttempted = true;
                    Gate(id).Release(); gateHeld = false;
                    await CompleteOuterRecovery(sessionId, entry, outerEnd);
                    await Gate(id).WaitAsync(ct); gateHeld = true;
                    entry = journal.Read(id, entry.InitialServerId)
                        ?? throw new InvalidDataException("Recovery journal disappeared.");
                    if (entry.Phase != ScavPhase.Committed)
                        throw new InvalidDataException("The outer raid callback did not complete the combined result.");
                }
            }
            else if (CanRollForward(sessionId, entry))
            {
                await SaveGate(id).WaitAsync(ct);
                try
                {
                    await PersistRecordedOutcome(sessionId, entry, nativeSave);
                }
                finally { SaveGate(id).Release(); }
            }
            else if (entry.Phase == ScavPhase.Committed && entry.OuterEndReplayJson != null) { /* Exact outer completion below. */ }
            else if (entry.Phase == ScavPhase.Committed && entry.TransferPhase == ScavTransferPhase.Pending && !entry.InventoryMutationPending)
                return RecoveryStatusCore(sessionId); // A transferred/extracted life cannot be abandoned as a second death.
            else throw new InvalidDataException("The interrupted native result cannot be safely replayed.");
            if (entry.Phase == ScavPhase.Committed && entry.OuterEndReplayJson != null)
            {
                outerAttempted = true;
                Gate(id).Release(); gateHeld = false;
                await CompleteOuterRecovery(sessionId, entry, outerEnd);
                await Gate(id).WaitAsync(ct); gateHeld = true;
            }
            ForgetIssuedRaid(id, entry.InitialServerId);
            return RecoveryStatusCore(sessionId);
        }
        catch (Exception ex)
        {
            if (outerAttempted)
            {
                // A late real result can win the outer lifecycle race. Report
                // its proven committed state even if our conflicting end failed.
                try
                {
                    if (!gateHeld) { await Gate(id).WaitAsync(); gateHeld = true; }
                    await SaveGate(id).WaitAsync();
                    try
                    {
                        var completed = journal.ForSession(id).SingleOrDefault(x => RecoveryId(x) == request.RecoveryId);
                        if (completed != null)
                        {
                            ReconcileRecovery(sessionId, completed);
                            if (completed.Phase == ScavPhase.Committed) return RecoveryStatusCore(sessionId);
                        }
                    }
                    finally { SaveGate(id).Release(); }
                }
                catch (Exception) { /* Preserve the whitelisted failure below. */ }
            }
            return RecoveryBlocked(ex);
        }
        finally { if (gateHeld) Gate(id).Release(); }
    }

    private static ScavRecoveryResponse RecoveryBlocked(Exception ex) => new()
    {
        State = "blocked", Error = "recovery-blocked",
        // Whitelist explanations: never forward serializer/native errors that may
        // contain private journal paths, tokens, or complete account data.
        Message = ex is RecoveryInsuranceException
            ? "This older interrupted raid lacks its PMC insurance snapshot. Automatic recovery is unavailable; retain the private journal for support."
            : "Recovery could not verify this account's interrupted result. No uncertain outcome will be repeated. Retain the private journal for support."
    };

    private void AssertRecoveryAccount(MongoId sessionId, ScavJournalEntry entry)
    {
        var full = saves.GetProfile(sessionId);
        if (full.ProfileInfo?.ProfileId != sessionId || full.CharacterData?.PmcData?.Id.ToString() != entry.PmcProfileId
            || full.CharacterData.PmcData.Savage.ToString() != entry.ScavProfileId
            || full.CharacterData.ScavData?.Id.ToString() != entry.ScavProfileId)
            throw new InvalidDataException("Recovery account identity changed.");
    }

    private void AssertTransferScav(MongoId sessionId, ScavJournalEntry entry)
    {
        var scav = saves.GetProfile(sessionId).CharacterData!.ScavData!;
        if (!entry.Extracted || scav.Inventory?.Equipment.ToString() != entry.ScavEquipmentId)
            throw new InvalidDataException("The extracted Scav inventory was replaced.");
        ScavInventoryPolicy.Carried(scav, templates.Items.ContainsKey);
    }

    private sealed class RecoveryInsuranceException : Exception;

    private void ValidateInsuranceCapture(IEnumerable<Item>? captured, IEnumerable<InsuredItem> insured)
    {
        if (captured == null) throw new InvalidDataException("PMC insurance capture is missing.");
        var registered = insured.Select(x => x.ItemId?.ToString() ?? "").ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in captured)
            if (item.Id.IsEmpty || !seen.Add(item.Id.ToString()) || !registered.Contains(item.Id.ToString())
                || !templates.Items.ContainsKey(item.Template))
                throw new InvalidDataException("Invalid original PMC insurance capture.");
    }

    private EndLocalRaidRequestData CreateAbandonedResult(MongoId sessionId, ScavJournalEntry entry)
    {
        AssertRecoveryAccount(sessionId, entry);
        AssertReservedScav(sessionId, entry);
        var frozen = Parse<EndLocalRaidRequestData>(entry.FrozenPmcJson);
        var insured = Parse<List<InsuredItem>>(entry.OriginalInsuredJson);
        if (!entry.PmcInsuranceCaptured && insured.Count != 0) throw new RecoveryInsuranceException();
        if (entry.PmcInsuranceCaptured) ValidateInsuranceCapture(frozen.LostInsuredItems, insured);
        if (frozen.ServerId != entry.InitialServerId || frozen.Results?.Profile?.Id.ToString() != entry.PmcProfileId
            || frozen.Results.Result != ExitStatus.KILLED || !ValidDuration(frozen.Results.PlayTime)
            || frozen.LocationTransit != null || !ScavTransferPolicy.ContainsNoTransferredItems(frozen.TransferItems))
            throw new InvalidDataException("Invalid frozen PMC result.");
        // Native EndCore filters this complete list against death-retained slots.
        // Items in mail or a pending insurance return have already been delivered;
        // they must never be scheduled again after an interrupted older request.
        var full = saves.GetProfile(sessionId);
        var delivered = (full.InsuranceList ?? []).SelectMany(x => x.Items ?? [])
            .Select(x => x.Id.ToString()).ToHashSet(StringComparer.Ordinal);
        foreach (var item in (full.BtrDeliveryList ?? []).SelectMany(x => x.Items ?? [])) delivered.Add(item.Id.ToString());
        foreach (var dialogue in full.DialogueRecords?.Values.AsEnumerable() ?? [])
            foreach (var message in dialogue.Messages ?? [])
                foreach (var item in message.Items?.Data ?? []) delivered.Add(item.Id.ToString());
        var lost = (frozen.LostInsuredItems ?? []).Where(x => !delivered.Contains(x.Id.ToString())).ToList();
        return new()
        {
            ServerId = entry.InitialServerId, LostInsuredItems = lost,
            Results = new() { Profile = Parse<PmcData>(entry.ScavJson), Result = ExitStatus.KILLED,
                PlayTime = 0, InSession = false }
        };
    }

    private bool CanRollForward(MongoId sessionId, ScavJournalEntry entry)
    {
        bool transfer = entry.TransferPhase == ScavTransferPhase.Completing;
        if (transfer && entry.InventoryMutationPending) return false;
        if (!transfer && !entry.InventoryMutationPending && !ScavStatePolicy.NeedsRecovery(entry.Phase)) return false;
        var (before, outcome, expected) = RecoverySnapshot(entry);
        if (before == null || outcome == null || expected == null || ScavJournal.Hash(outcome) != expected) return false;
        var recorded = Parse<SptProfile>(outcome);
        if (recorded.ProfileInfo?.ProfileId != sessionId || recorded.CharacterData?.PmcData?.Id.ToString() != entry.PmcProfileId
            || recorded.CharacterData.ScavData?.Id.ToString() != entry.ScavProfileId
            || recorded.CharacterData.PmcData.Savage.ToString() != entry.ScavProfileId) return false;
        if (transfer && (entry.TransferResponse == null || recorded.CharacterData.ScavData.Inventory?.Equipment.ToString() == entry.ScavEquipmentId)) return false;
        if (entry.InventoryMutationPending && (!entry.Extracted || entry.TransferPhase != ScavTransferPhase.Pending
            || recorded.CharacterData.ScavData.Inventory?.Equipment.ToString() != entry.ScavEquipmentId)) return false;
        string old = ScavJournal.Hash(before), current = ScavJournal.Hash(Serialize(saves.GetProfile(sessionId)));
        return (current == old || current == expected) && (DiskMatches(entry, old) || DiskMatches(entry, expected));
    }

    private void ReconcileRecovery(MongoId sessionId, ScavJournalEntry entry)
    {
        RequireCurrentVersion(entry);
        AssertRecoveryAccount(sessionId, entry);
        bool transfer = entry.TransferPhase == ScavTransferPhase.Completing;
        if (transfer && entry.InventoryMutationPending) throw new InvalidDataException("Conflicting recovery stages.");
        if (!transfer && !entry.InventoryMutationPending && !ScavStatePolicy.NeedsRecovery(entry.Phase)) return;
        var snapshot = RecoverySnapshot(entry);
        if (!DiskMatches(entry, snapshot.Expected)) return;
        if (ScavJournal.Hash(Serialize(saves.GetProfile(sessionId))) != snapshot.Expected)
        {
            if (!CanRollForward(sessionId, entry)) throw new InvalidDataException("Memory differs from the verified saved result.");
            ReplaceProfileContents(saves.GetProfile(sessionId), Parse<SptProfile>(snapshot.Outcome!));
        }
        PreserveOuterEndForRecovery(entry);
        MarkRecoveryComplete(entry);
    }

    private static (string? Before, string? Outcome, string? Expected) RecoverySnapshot(ScavJournalEntry entry)
        => entry.InventoryMutationPending
            ? (entry.InventoryBeforeProfileJson, entry.InventoryExpectedProfileJson, entry.InventoryExpectedProfileHash)
            : entry.TransferPhase == ScavTransferPhase.Completing
                ? (entry.TransferBeforeProfileJson, entry.TransferExpectedProfileJson, entry.TransferExpectedProfileHash)
                : (entry.BeforeProfileJson, entry.ExpectedProfileJson, entry.ExpectedProfileHash);

    private void MarkRecoveryComplete(ScavJournalEntry entry)
    {
        if (entry.InventoryMutationPending) MarkInventoryComplete(entry);
        else if (entry.TransferPhase == ScavTransferPhase.Completing) MarkTransferComplete(entry);
        else MarkCommitted(entry);
    }

    private async Task PersistRecordedOutcome(MongoId sessionId, ScavJournalEntry entry, Func<Task<long>> nativeSave)
    {
        // Caller holds both account locks. Never replay native random generation,
        // mail, insurance or item actions: only the complete recorded profile.
        if (!CanRollForward(sessionId, entry)) throw new InvalidDataException("Profile changed before recovery could save.");
        PreserveOuterEndForRecovery(entry);
        journal.Write(entry);
        var snapshot = RecoverySnapshot(entry);
        ReplaceProfileContents(saves.GetProfile(sessionId), Parse<SptProfile>(snapshot.Outcome!));
        InvalidateFailedNativeSave(sessionId);
        await SaveAndVerify(entry, nativeSave, snapshot.Expected!);
        MarkRecoveryComplete(entry);
    }

    private static void PreserveOuterEndForRecovery(ScavJournalEntry entry)
    {
        if (!entry.InventoryMutationPending && entry.TransferPhase != ScavTransferPhase.Completing
            && ScavStatePolicy.NeedsRecovery(entry.Phase) && entry.FinalJson != null)
            entry.OuterEndReplayJson = entry.FinalJson;
    }

    private async Task CompleteOuterRecovery(MongoId sessionId, ScavJournalEntry entry,
        Func<EndLocalRaidRequestData, Task>? outerEnd)
    {
        string id = sessionId.ToString(), identity = RecoveryId(entry);
        Task transaction;
        await Gate(id).WaitAsync();
        try
        {
            if (outerRecoveries.TryGetValue(id, out var existing))
            {
                if (existing.RecoveryId != identity) throw new InvalidDataException("Another recovered outer raid owns this account.");
                transaction = existing.Task;
            }
            else
            {
                // Never dispatch a stale object read before another recovery's
                // ACK/new raid. Re-read and claim the exact generation atomically.
                var current = journal.Read(id, entry.InitialServerId)
                    ?? throw new InvalidDataException("The outer recovery journal disappeared.");
                RequireCurrentVersion(current); AssertRecoveryAccount(sessionId, current);
                if (RecoveryId(current) != identity) throw new InvalidDataException("The outer recovery generation changed.");
                if (current.Phase == ScavPhase.Committed
                    && (current.TransferPhase == ScavTransferPhase.Retired || current.OuterEndReplayJson == null)) return;
                if (current.OuterEndReplayJson == null) throw new InvalidDataException("The exact outer request is missing.");
                var original = Parse<EndLocalRaidRequestData>(current.OuterEndReplayJson);
                if (original.ServerId != current.InitialServerId) throw new InvalidDataException("Outer raid identity mismatch.");
                if (current.Phase == ScavPhase.Committed)
                {
                    if (ScavJournal.Hash(Serialize(original)) != current.FinalHash)
                        throw new InvalidDataException("The exact committed outer request could not be proven.");
                }
                else if (current.Phase is not (ScavPhase.Prepared or ScavPhase.Active) || !current.RecoveryAbandoned
                    || original.Results?.Profile?.Id.ToString() != current.ScavProfileId)
                    throw new InvalidDataException("An unfinished native outcome cannot be replayed.");
                var execution = new OuterRecoveryExecution(identity);
                outerRecoveries[id] = execution;
                transaction = execution.Task = ExecuteOuterRecovery(sessionId, original, execution, outerEnd);
            }
        }
        finally { Gate(id).Release(); }
        await transaction;
    }

    private async Task ExecuteOuterRecovery(MongoId sessionId, EndLocalRaidRequestData original,
        OuterRecoveryExecution execution, Func<EndLocalRaidRequestData, Task>? outerEnd)
    {
        await Task.Yield(); // Publish the claim before any callback can enter our inner gate.
        string id = sessionId.ToString();
        try
        {
            if (outerEnd != null) await outerEnd(original);
            else if (matchCallbacks != null)
                _ = await matchCallbacks.EndLocalRaidAsync("/client/match/local/end", original, sessionId, CancellationToken.None);
            else throw new InvalidOperationException("The outer raid callback is unavailable.");
            await AcknowledgeOuterEnd(sessionId, original, CancellationToken.None);
        }
        finally
        {
            await Gate(id).WaitAsync();
            try { ((ICollection<KeyValuePair<string, OuterRecoveryExecution>>)outerRecoveries).Remove(new(id, execution)); }
            finally { Gate(id).Release(); }
        }
    }

    public async Task AcknowledgeOuterEnd(MongoId sessionId, EndLocalRaidRequestData request, CancellationToken ct)
    {
        string id = sessionId.ToString();
        await Gate(id).WaitAsync(ct);
        try
        {
            var entry = journal.Read(id, request.ServerId ?? "");
            if (entry?.Version != 2 || entry.Phase != ScavPhase.Committed || entry.OuterEndReplayJson == null
                || entry.TransferPhase == ScavTransferPhase.Retired) return;
            AssertRecoveryAccount(sessionId, entry);
            if (entry.FinalHash != ScavJournal.Hash(Serialize(request))) return;
            entry.OuterEndReplayJson = null; journal.Write(entry);
        }
        finally { Gate(id).Release(); }
    }

    private void MarkInventoryComplete(ScavJournalEntry entry)
    {
        entry.InventoryMutationPending = false;
        entry.InventoryBeforeProfileJson = entry.InventoryExpectedProfileJson = entry.InventoryExpectedProfileHash = null;
        entry.Phase = ScavPhase.Committed; entry.Error = null; journal.Write(entry);
        quarantined.TryRemove(entry.SessionId, out _);
    }

    private static void ReplaceProfileContents(SptProfile target, SptProfile source)
    {
        // Keep SaveServer's root reference stable for native callers. Every public
        // serialized field, including insurance and dialogs, comes from one outcome.
        foreach (var property in typeof(SptProfile).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                property.SetValue(target, property.GetValue(source));
    }

    private void InvalidateFailedNativeSave(MongoId sessionId)
    {
        // SPT 4.1 caches the attempted MD5 before its atomic file write. After a
        // failed write the identical profile would otherwise be skipped forever
        // in this server process. This is reached only under both account locks,
        // after exact before/outcome proof; no native outcome is run again.
        var field = typeof(SaveServer).GetField("saveMd5", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field?.GetValue(saves) is not ConcurrentDictionary<MongoId, string> cache)
            throw new InvalidOperationException("The native profile save cache contract changed.");
        cache.TryRemove(sessionId, out _);
    }
}

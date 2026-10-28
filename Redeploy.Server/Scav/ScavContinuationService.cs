using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.InRaid;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Match;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using Path = System.IO.Path;

namespace Redeploy.Server.Scav;

/// <summary>Reserves the account Player-Scav. Both native outcomes persist together;
/// the surviving Scav then owns the native transfer. No independent profile or loot mail.</summary>
[Injectable(InjectionType.Singleton)]
public sealed class ScavContinuationService
{
    public static ScavContinuationService? Current { get; private set; }
    public static readonly AsyncLocal<bool> RawSave = new();
    public static readonly AsyncLocal<bool> RawRegenerate = new();
    public static readonly AsyncLocal<bool> RawItemEvent = new();
    private static readonly AsyncLocal<string?> CurrentInventoryMutation = new();
    private static readonly AsyncLocal<bool> RawEnd = new();
    private static readonly AsyncLocal<Settlement?> CurrentSettlement = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> saveGates = new();
    private readonly ConcurrentDictionary<string, string> issuedRaids = new();
    private readonly ConcurrentDictionary<string, bool> quarantined = new();
    private readonly SaveServer saves;
    private readonly JsonUtil json;
    private readonly ICloner cloner;
    private readonly InRaidHelper inRaid;
    private readonly TemplateTable templates;
    private readonly ISptLogger<ScavContinuationService> logger;
    private readonly ScavJournal journal;

    private sealed class Settlement(ScavJournalEntry entry)
    {
        public readonly ScavJournalEntry Entry = entry;
        public bool PmcHandled;
        public bool ScavStage;
        public bool ScavSaveRequested;
        public bool Active = true;
        public Func<Task<long>>? NativeSave;
    }

    public ScavContinuationService(SaveServer saves, JsonUtil json, ICloner cloner,
        InRaidHelper inRaid, TemplateTable templates, ISptLogger<ScavContinuationService> logger)
    {
        this.saves = saves; this.json = json; this.cloner = cloner;
        this.inRaid = inRaid; this.templates = templates; this.logger = logger;
        journal = new ScavJournal(Path.Combine(Path.GetDirectoryName(typeof(ScavContinuationService).Assembly.Location)!, "data", "pmc-scav"));
        Current = this;
    }

    private SemaphoreSlim Gate(string id) => gates.GetOrAdd(id, _ => new(1, 1));
    private SemaphoreSlim SaveGate(string id) => saveGates.GetOrAdd(id, _ => new(1, 1));
    private string Serialize<T>(T value) => json.Serialize(value) ?? throw new InvalidDataException("Serialization failed.");
    private T Parse<T>(string value) => json.Deserialize<T>(value) ?? throw new InvalidDataException("Invalid continuation data.");
    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private static bool IsPmcServer(string? id) => id?.Split('.').ElementAtOrDefault(1)?.StartsWith("pmc ", StringComparison.OrdinalIgnoreCase) == true;
    public bool IsExecuting(MongoId sessionId) => CurrentSettlement.Value is { Active: true } context && context.Entry.SessionId == sessionId.ToString();

    public void BeforeStart(MongoId sessionId)
    {
        foreach (var entry in journal.ForSession(sessionId.ToString()))
        {
            if (!ScavStatePolicy.BlocksNewRaid(entry)) continue;
            RequireCurrentVersion(entry);
            if (entry.Phase is ScavPhase.Settling or ScavPhase.RecoveryRequired && DiskMatches(entry, entry.ExpectedProfileHash))
                MarkCommitted(entry);
            if (entry.TransferPhase == ScavTransferPhase.Completing && DiskMatches(entry, entry.TransferExpectedProfileHash))
                MarkTransferComplete(entry);
            if (ScavStatePolicy.BlocksNewRaid(entry))
                throw new InvalidOperationException("Redeploy: finish the Scav loot transfer or recover the pending PMC-to-Scav raid before starting another raid.");
        }
    }

    public void RegisterStart(MongoId sessionId, StartLocalRaidRequestData request, StartLocalRaidResponseData response)
    {
        // A real admitted raid retires the previous completed transfer. Old root
        // tombstones remain, so late scavsave cannot resurrect transferred inventory.
        foreach (var previous in journal.ForSession(sessionId.ToString()))
            if (previous.Version == 2 && previous.Phase == ScavPhase.Committed
                && previous.TransferPhase == ScavTransferPhase.Complete)
            {
                previous.TransferPhase = ScavTransferPhase.Retired;
                previous.TransferResponse = null; journal.Write(previous);
            }
        string? rejected = StartRejection(request, response);
        if (rejected == null)
            issuedRaids[sessionId.ToString()] = response.ServerId!;
        else issuedRaids.TryRemove(sessionId.ToString(), out _);
        logger.Debug($"Redeploy Scav raid registration: {rejected ?? "issued"}; "
            + $"transitFlags={(int?)request.TransitionType}, incomingTransition={request.Transition != null}, "
            + $"transitionCount={response.Transition?.TransitionCount}, visitedCount={response.Transition?.VisitedLocations?.Count}.");
    }

    private static string? StartRejection(StartLocalRaidRequestData request, StartLocalRaidResponseData response)
    {
        if (request.ServerId?.StartsWith("arenamode-start:", StringComparison.OrdinalIgnoreCase) == true
            || response.ServerId?.Contains("|arenamode:", StringComparison.OrdinalIgnoreCase) == true)
            return "arena-raid";
        if (!string.Equals(request.PlayerSide, "pmc", StringComparison.OrdinalIgnoreCase)
            || !IsPmcServer(response.ServerId))
            return "not-issued-pmc";
        // COMMON/EVENT describe available transit exits, including on a fresh raid.
        // An incoming transfer instead supplies a transition object or receives
        // nonzero count/history from the server's saved location-transit state.
        if (request.TransitionType is { } flags
            && (flags & ~(TransitionType.COMMON | TransitionType.EVENT)) != 0)
            return "unknown-transit-flags";
        if (request.Transition != null)
            return "incoming-transition";
        if (response.Transition is { } transition
            && (transition.TransitionCount != 0 || transition.VisitedLocations is not { Count: 0 }))
            return "non-fresh-transition";
        return null;
    }

    public async Task<ScavResponse> Prepare(MongoId sessionId, ScavPrepareRequest request, CancellationToken ct)
    {
        string id = sessionId.ToString();
        await Gate(id).WaitAsync(ct);
        try
        {
            if (request.ProtocolVersion != 2 || !Guid.TryParse(request.ClientRequestId, out _)
                || request.InitialServerId.Length > 256 || !IsPmcServer(request.InitialServerId))
                return ScavResponse.Rejected("invalid-prepare-request");
            string frozenJson = Serialize(request.FrozenPmcDeath);
            if (frozenJson.Length > 16_000_000) return ScavResponse.Rejected("profile-too-large");
            string hash = ScavJournal.Hash(frozenJson);
            var existing = journal.Read(id, request.InitialServerId);
            if (existing != null)
            {
                RequireCurrentVersion(existing);
                if (!ScavStatePolicy.SamePreparedRequest(existing, request.ClientRequestId, hash))
                    return ScavResponse.Rejected("continuation-already-prepared");
                if (existing.Phase is not (ScavPhase.Prepared or ScavPhase.Active))
                    return ScavResponse.Rejected("continuation-closed");
                if (existing.Phase == ScavPhase.Prepared && existing.ExpiresAt < Now)
                    return ScavResponse.Rejected("prepare-expired");
                return PreparedResponse(existing);
            }
            if (!issuedRaids.TryGetValue(id, out var issued) || issued != request.InitialServerId)
            {
                logger.Debug($"Redeploy Scav preparation rejected: no-issued-pmc-raid; registration={(issued == null ? "missing" : "identity-mismatch")}.");
                return ScavResponse.Rejected("no-issued-pmc-raid");
            }
            if (journal.ForSession(id).Any(ScavStatePolicy.BlocksNewRaid))
                return ScavResponse.Rejected("previous-continuation-pending");
            var full = saves.GetProfile(sessionId);
            var pmc = full.CharacterData!.PmcData!;
            var scav = full.CharacterData.ScavData;
            if (scav?.Info?.Side != "Savage" || scav.Id == null || scav.Id == pmc.Id || scav.Id != pmc.Savage
                || scav.Inventory?.Equipment == null || scav.Info.SavageLockTime == null
                || !double.IsFinite(scav.Info.SavageLockTime.Value))
                return ScavResponse.Rejected("regular-scav-unavailable");
            if (scav.Info.SavageLockTime >= Now)
                return new ScavResponse { Error = "scav-cooldown", AvailableAt = scav.Info.SavageLockTime };
            var frozen = Parse<EndLocalRaidRequestData>(frozenJson);
            if (frozen.ServerId != issued || frozen.Results?.Result != ExitStatus.KILLED
                || frozen.Results.Profile?.Id != pmc.Id || frozen.Results.Profile?.Info?.Side == "Savage"
                || !ValidDuration(frozen.Results.PlayTime)
                || frozen.LocationTransit != null || !ScavTransferPolicy.ContainsNoTransferredItems(frozen.TransferItems))
                return ScavResponse.Rejected("invalid-frozen-pmc-death");
            var frozenPmc = frozen.Results!.Profile!;
            ScavInventoryPolicy.Carried(frozenPmc, templates.Items.ContainsKey);
            var scavItems = ScavInventoryPolicy.Carried(scav, templates.Items.ContainsKey);
            var pmcIds = pmc.Inventory!.Items!.Select(x => x.Id.ToString()).ToHashSet(StringComparer.Ordinal);
            if (scavItems.Any(x => pmcIds.Contains(x.Id.ToString())) || pmcIds.Contains(scav.Inventory.Equipment.ToString()!))
                return ScavResponse.Rejected("regular-scav-shares-pmc-items");
            frozenPmc.InsuredItems = cloner.Clone(pmc.InsuredItems ?? []);
            frozen.Results.ExitName = null; frozen.Results.InSession = false;
            frozen.LocationTransit = null; frozen.TransferItems = null;
            // A serialization roundtrip separates mutable state, preserving real account
            // identity/loadout/progression. Preparation never invokes a Scav generator.
            var scavJson = Serialize(scav);
            var entry = new ScavJournalEntry
            {
                SessionId = id, InitialServerId = issued, PmcProfileId = pmc.Id!.Value.ToString(),
                ScavProfileId = scav.Id!.Value.ToString(), ScavEquipmentId = scav.Inventory.Equipment.Value.ToString(),
                RequestId = request.ClientRequestId, PrepareHash = hash,
                Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                ScavJson = scavJson, GenerationHash = ScavJournal.Hash(scavJson),
                FrozenPmcJson = Serialize(frozen), OriginalInsuredJson = Serialize(pmc.InsuredItems ?? []),
                ExpiresAt = Now + 600, Phase = ScavPhase.Prepared
            };
            journal.Write(entry);
            return PreparedResponse(entry);
        }
        catch (Exception ex)
        {
            logger.Warning($"Redeploy Scav preparation refused ({ex.GetType().Name}); ordinary PMC settlement remains available.");
            return ScavResponse.Rejected("prepare-unavailable");
        }
        finally { Gate(id).Release(); }
    }

    private ScavResponse PreparedResponse(ScavJournalEntry entry) => new()
    {
        Success = true, Token = entry.Token, Phase = entry.Phase.ToString(),
        ScavProfile = Parse<PmcData>(entry.ScavJson), GenerationHash = entry.GenerationHash, ExpiresAt = entry.ExpiresAt
    };

    public async Task<ScavResponse> Control(MongoId sessionId, ScavControlRequest request, bool cancel, CancellationToken ct)
    {
        string id = sessionId.ToString();
        await Gate(id).WaitAsync(ct);
        try
        {
            var entry = journal.Read(id, request.InitialServerId);
            if (entry == null || entry.Version != 2 || !ScavStatePolicy.MatchesToken(entry, request.Token))
                return ScavResponse.Rejected("unknown-continuation");
            if (cancel)
            {
                if (!ScavStatePolicy.CanCancel(entry.Phase)) return ScavResponse.Rejected("body-already-activated");
                entry.Phase = ScavPhase.Cancelled;
            }
            else
            {
                if (request.ScavProfileId != entry.ScavProfileId || request.GenerationHash != entry.GenerationHash)
                    return ScavResponse.Rejected("generation-mismatch");
                if (entry.Phase == ScavPhase.Active) return new() { Success = true, Phase = "Active" };
                if (entry.Phase != ScavPhase.Prepared) return ScavResponse.Rejected("continuation-closed");
                AssertReservedScav(sessionId, entry);
                entry.Phase = ScavPhase.Active;
            }
            journal.Write(entry);
            return new() { Success = true, Phase = entry.Phase.ToString() };
        }
        catch (Exception) { return ScavResponse.Rejected("control-unavailable"); }
        finally { Gate(id).Release(); }
    }

    public bool ShouldInterceptEnd(MongoId sessionId, EndLocalRaidRequestData request)
    {
        if (RawEnd.Value || IsExecuting(sessionId) || request.ServerId == null) return false;
        if (journal.Read(sessionId.ToString(), request.ServerId) != null) return true;
        if (issuedRaids.TryGetValue(sessionId.ToString(), out var issued) && issued == request.ServerId) return true;
        return IsPmcServer(request.ServerId) && request.Results?.Profile?.Id != saves.GetProfile(sessionId).CharacterData?.PmcData?.Id;
    }

    public async Task End(MongoId sessionId, EndLocalRaidRequestData request,
        Func<EndLocalRaidRequestData, Task> nativeEnd,
        Func<PmcData, PmcData, EndLocalRaidRequestData, Task> nativeScavOutcome, CancellationToken ct)
    {
        string id = sessionId.ToString();
        await Gate(id).WaitAsync(ct);
        bool acquiredSave = false;
        ScavJournalEntry? entry = null;
        try
        {
            string incomingId = request.Results?.Profile?.Id.ToString() ?? "";
            entry = journal.Read(id, request.ServerId ?? "");
            if (entry == null)
            {
                if (!issuedRaids.TryGetValue(id, out var issued) || issued != request.ServerId
                    || incomingId != saves.GetProfile(sessionId).CharacterData?.PmcData?.Id.ToString())
                    throw new InvalidOperationException("Unregistered Scav continuation; profile save refused.");
                ForgetIssuedRaid(id, issued);
                await InvokeNative(nativeEnd, request);
                return;
            }
            RequireCurrentVersion(entry);
            if (incomingId == entry.PmcProfileId && (entry.Phase is ScavPhase.Prepared or ScavPhase.Cancelled || entry.PmcOnly))
            {
                await EndPmcOnly(sessionId, entry, request, nativeEnd, ct);
                ForgetIssuedRaid(id, entry.InitialServerId);
                return;
            }
            if (incomingId != entry.ScavProfileId) throw new InvalidOperationException("Continuation profile identity mismatch.");
            if (!ValidDuration(request.Results?.PlayTime)) throw new InvalidDataException("Invalid Scav body duration.");
            string finalJson = Serialize(request), finalHash = ScavJournal.Hash(finalJson);
            if (entry.FinalHash != null && entry.FinalHash != finalHash)
                throw new InvalidOperationException("Conflicting continuation end retry.");
            if (entry.Phase == ScavPhase.Committed) return;
            if (DiskMatches(entry, entry.ExpectedProfileHash)) { MarkCommitted(entry); return; }
            if (entry.Phase == ScavPhase.Cancelled || ScavStatePolicy.NeedsRecovery(entry.Phase))
                throw new InvalidOperationException("Continuation needs recovery; native settlement will not be replayed.");
            AssertReservedScav(sessionId, entry);
            var frozen = Parse<EndLocalRaidRequestData>(entry.FrozenPmcJson);
            var finalScav = request.Results!.Profile!;
            if (finalScav.Info?.Side != "Savage" || finalScav.Inventory?.Equipment.ToString() != entry.ScavEquipmentId
                || request.Results.Result == null || request.Results.Result == ExitStatus.TRANSIT
                || request.LocationTransit != null || !ScavTransferPolicy.ContainsNoTransferredItems(request.TransferItems))
                throw new InvalidDataException("Unsupported Scav continuation result.");
            var carried = ScavInventoryPolicy.Carried(finalScav, templates.Items.ContainsKey);
            var frozenPmc = frozen.Results!.Profile!;
            var lostRoots = (IEnumerable<Item>)typeof(InRaidHelper).GetMethod("GetInventoryItemsLostOnDeath", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(inRaid, [frozenPmc])!;
            var protectedIds = ScavInventoryPolicy.RetainedIds(frozenPmc.Inventory!.Items!, lostRoots);
            if (carried.Any(x => protectedIds.Contains(x.Id.ToString())))
                throw new InvalidDataException("Protected PMC items appeared in Scav inventory.");
            bool extracted = ScavInventoryPolicy.Extracted(request.Results.Result);
            var recovered = extracted ? carried.Select(x => x.Id.ToString()).ToHashSet(StringComparer.Ordinal) : [];
            var originalInsurance = Parse<List<InsuredItem>>(entry.OriginalInsuredJson);
            if (originalInsurance.Count > 0 && request.LostInsuredItems == null)
                throw new InvalidDataException("Original PMC insurance calculation is missing.");
            frozenPmc.InsuredItems = originalInsurance;
            frozen.LostInsuredItems = ScavInventoryPolicy.LostInsurance(request.LostInsuredItems,
                originalInsurance.Select(x => x.ItemId!.Value.ToString()).ToHashSet(StringComparer.Ordinal), recovered, protectedIds);
            var scavEnd = Parse<EndLocalRaidRequestData>(finalJson);
            // Keep the real issued raid identity. A second outer end with a fabricated
            // Scav ID would re-enter other mods' exact-raid lifecycle locks. Common end
            // effects run once for the PMC; the native Scav handler settles only its life.
            scavEnd.LostInsuredItems = null; scavEnd.TransferItems = null; scavEnd.LocationTransit = null;
            scavEnd.Results!.InSession = false;
            // Protocol 2 reports duration measured from the replacement body's actual
            // OnGameSessionBegin. PMC time, prompt time and preload time are excluded by
            // the client before native Scav lifetime/exit-condition calculations.
            entry.FinalJson = finalJson; entry.FinalHash = finalHash; entry.Extracted = extracted;
            entry.TransferPhase = extracted ? ScavTransferPhase.Pending : ScavTransferPhase.Complete;
            await SaveGate(id).WaitAsync(ct); acquiredSave = true;
            var full = saves.GetProfile(sessionId);
            entry.BeforeProfileJson = Serialize(full); entry.Phase = ScavPhase.Settling;
            journal.Write(entry);
            var context = new Settlement(entry); CurrentSettlement.Value = context;
            await InvokeNative(nativeEnd, frozen);
            if (!context.PmcHandled) throw new InvalidOperationException("Native PMC outcome did not complete.");
            full = saves.GetProfile(sessionId);
            RebaseScavStanding(scavEnd.Results.Profile!, Parse<PmcData>(entry.ScavJson), full.CharacterData!.PmcData!);
            if (extracted)
            {
                // Native survived handling otherwise waits for the late scavsave upload.
                // Persist the transfer inventory once here before any item can be moved.
                full.CharacterData.ScavData!.Inventory = cloner.Clone(scavEnd.Results.Profile!.Inventory);
            }
            context.ScavStage = true;
            await nativeScavOutcome(full.CharacterData!.PmcData!, full.CharacterData.ScavData!, scavEnd);
            if (!context.ScavSaveRequested || context.NativeSave == null) throw new InvalidOperationException("Native Scav outcome did not request persistence.");
            entry.ExpectedProfileHash = ScavJournal.Hash(Serialize(full));
            journal.Write(entry);
            await SaveAndVerify(entry, context.NativeSave, entry.ExpectedProfileHash);
            MarkCommitted(entry); ForgetIssuedRaid(id, entry.InitialServerId);
            logger.Info(extracted ? "Redeploy: your Scav extracted; recovered equipment is ready for the loot transfer."
                : "Redeploy: the Scav continuation ended; PMC death and Scav cooldown were saved.");
        }
        catch (Exception ex)
        {
            if (entry?.Phase == ScavPhase.Settling)
            {
                if (DiskMatches(entry, entry.ExpectedProfileHash)) { MarkCommitted(entry); return; }
                Quarantine(entry, ex);
            }
            throw;
        }
        finally
        {
            if (CurrentSettlement.Value is { } context) context.Active = false;
            CurrentSettlement.Value = null;
            if (acquiredSave) SaveGate(id).Release();
            Gate(id).Release();
        }
    }

    private static void RebaseScavStanding(PmcData final, PmcData initial, PmcData settledPmc)
    {
        // Apply the Scav phase's delta on top of the settled PMC standing; native
        // handling still clamps Fence and grants the usual extraction karma.
        foreach (var trader in final.TradersInfo ?? [])
            if (trader.Value.Standing is double after && initial.TradersInfo?.TryGetValue(trader.Key, out var before) == true
                && before.Standing is double old && settledPmc.TradersInfo?.TryGetValue(trader.Key, out var current) == true
                && current.Standing is double now)
                trader.Value.Standing = now + (after - old);
    }

    private static bool ValidDuration(double? seconds)
        => seconds is >= 0 and <= 86400 && double.IsFinite(seconds.Value);

    private async Task EndPmcOnly(MongoId sessionId, ScavJournalEntry entry, EndLocalRaidRequestData request,
        Func<EndLocalRaidRequestData, Task> nativeEnd, CancellationToken ct)
    {
        var hash = ScavJournal.Hash(Serialize(request));
        if (entry.FinalHash != null && entry.FinalHash != hash) throw new InvalidOperationException("Conflicting cancelled continuation end retry.");
        if (entry.Phase == ScavPhase.Committed) return;
        if (DiskMatches(entry, entry.ExpectedProfileHash)) { MarkCommitted(entry); return; }
        if (ScavStatePolicy.NeedsRecovery(entry.Phase)) throw new InvalidOperationException("Cancelled continuation needs recovery.");
        await SaveGate(entry.SessionId).WaitAsync(ct);
        try
        {
            entry.PmcOnly = true; entry.FinalHash = hash; entry.TransferPhase = ScavTransferPhase.Retired;
            entry.BeforeProfileJson = Serialize(saves.GetProfile(sessionId)); entry.Phase = ScavPhase.Settling;
            journal.Write(entry);
            var context = new Settlement(entry); CurrentSettlement.Value = context;
            await InvokeNative(nativeEnd, request);
            if (!context.PmcHandled || context.NativeSave == null) throw new InvalidOperationException("Native PMC cancellation outcome did not complete.");
            entry.ExpectedProfileHash = ScavJournal.Hash(Serialize(saves.GetProfile(sessionId)));
            journal.Write(entry);
            await SaveAndVerify(entry, context.NativeSave, entry.ExpectedProfileHash);
            MarkCommitted(entry);
        }
        finally { SaveGate(entry.SessionId).Release(); }
    }

    private void AssertReservedScav(MongoId sessionId, ScavJournalEntry entry)
    {
        var current = saves.GetProfile(sessionId).CharacterData!.ScavData!;
        if (current.Id.ToString() != entry.ScavProfileId || current.Inventory?.Equipment.ToString() != entry.ScavEquipmentId
            || ScavJournal.Hash(Serialize(current)) != entry.GenerationHash)
            throw new InvalidOperationException("The reserved Player-Scav changed before settlement.");
    }

    private static void RequireCurrentVersion(ScavJournalEntry entry)
    {
        if (entry.Version != 2) throw new InvalidOperationException("A legacy independent-Scav journal requires recovery with its original build.");
    }
    private void ForgetIssuedRaid(string session, string raid)
        => ((ICollection<KeyValuePair<string, string>>)issuedRaids).Remove(new(session, raid));
    private void MarkCommitted(ScavJournalEntry entry)
    {
        entry.Phase = ScavPhase.Committed; entry.Error = null;
        ScavStatePolicy.CompactCommitted(entry); journal.Write(entry);
    }
    private void MarkTransferComplete(ScavJournalEntry entry)
    {
        entry.TransferPhase = ScavTransferPhase.Complete;
        entry.TransferBeforeProfileJson = null; entry.Error = null; journal.Write(entry);
    }
    private void Quarantine(ScavJournalEntry entry, Exception ex)
    {
        quarantined[entry.SessionId] = true;
        entry.Phase = ScavPhase.RecoveryRequired; entry.Error = ex.GetType().Name; journal.Write(entry);
        logger.Error("Redeploy: Scav settlement could not be verified. Profile saving is paused to prevent duplicate inventory or insurance; retain the private continuation journal for recovery.");
    }
    private static async Task InvokeNative(Func<EndLocalRaidRequestData, Task> native, EndLocalRaidRequestData request)
    {
        RawEnd.Value = true;
        try { await native(request); }
        finally { RawEnd.Value = false; }
    }
    public void NativePmcHandled(MongoId sessionId, SptProfile profile)
    {
        if (CurrentSettlement.Value is { Active: true } context && context.Entry.SessionId == sessionId.ToString())
            context.PmcHandled = true;
    }

    public async Task<long> Save(MongoId sessionId, Func<Task<long>> nativeSave, CancellationToken ct)
    {
        string id = sessionId.ToString();
        if (CurrentSettlement.Value is { Active: true } context && context.Entry.SessionId == id)
        {
            context.NativeSave = nativeSave;
            context.ScavSaveRequested |= context.ScavStage;
            return 0; // Neither half of the combined outcome may persist alone.
        }
        if (CurrentInventoryMutation.Value == id)
        {
            RawSave.Value = true;
            try { return await nativeSave(); }
            finally { RawSave.Value = false; }
        }
        await SaveGate(id).WaitAsync(ct);
        try
        {
            if (quarantined.ContainsKey(id)) throw new InvalidOperationException("Redeploy continuation profile requires recovery before saving.");
            RawSave.Value = true; return await nativeSave();
        }
        finally { RawSave.Value = false; SaveGate(id).Release(); }
    }
    private async Task SaveAndVerify(ScavJournalEntry entry, Func<Task<long>> nativeSave, string expected)
    {
        RawSave.Value = true;
        try
        {
            try { await nativeSave(); }
            catch (Exception) { if (!DiskMatches(entry, expected)) throw; }
            if (!DiskMatches(entry, expected)) throw new IOException("The complete Scav continuation profile was not persisted.");
        }
        finally { RawSave.Value = false; }
    }
    private bool DiskMatches(ScavJournalEntry entry, string? expected)
    {
        if (string.IsNullOrEmpty(expected)) return false;
        string path = Path.GetFullPath(Path.Combine("user", "profiles", entry.SessionId + ".json"));
        if (!File.Exists(path)) return false;
        try
        {
            var profile = json.DeserializeFromFile<SptProfile>(path);
            return profile != null && ScavJournal.Hash(Serialize(profile)) == expected;
        }
        catch (IOException) { return false; }
        catch (System.Text.Json.JsonException) { return false; }
    }

    public bool BlocksScavSave(MongoId sessionId, string? equipment)
        => equipment != null && journal.ForSession(sessionId.ToString()).Any(entry => entry.Version == 2
            && entry.ScavEquipmentId == equipment && !entry.PmcOnly && entry.Phase != ScavPhase.Cancelled);
    public bool OwnsRegeneration(MongoId sessionId) => !RawRegenerate.Value && FindTransfer(sessionId) != null;
    public bool OwnsInventoryWindow(MongoId sessionId) => !RawItemEvent.Value && FindTransfer(sessionId) != null;
    public Task<long> SaveNativeProfile(MongoId sessionId) => saves.SaveProfileAsync(sessionId);
    private ScavJournalEntry? FindTransfer(MongoId sessionId) => journal.ForSession(sessionId.ToString())
        .Where(x => x.Version == 2 && x.Phase != ScavPhase.Cancelled && x.TransferPhase != ScavTransferPhase.Retired)
        .OrderByDescending(x => x.ExpiresAt).FirstOrDefault();

    public async ValueTask<T> WithTransferInventory<T>(MongoId sessionId, Func<ValueTask<T>> native, CancellationToken ct)
    {
        string id = sessionId.ToString();
        await Gate(id).WaitAsync(ct);
        bool acquiredSave = false;
        try
        {
            var entry = FindTransfer(sessionId);
            if (entry != null && (entry.Phase != ScavPhase.Committed
                || entry.TransferPhase is ScavTransferPhase.Completing))
                throw new InvalidOperationException("The reserved Scav inventory is not available for menu operations before settlement.");
            await SaveGate(id).WaitAsync(ct); acquiredSave = true;
            if (quarantined.ContainsKey(id)) throw new InvalidOperationException("The Scav inventory requires recovery.");
            CurrentInventoryMutation.Value = id; RawItemEvent.Value = true;
            return await native();
        }
        finally
        {
            CurrentInventoryMutation.Value = null; RawItemEvent.Value = false;
            if (acquiredSave) SaveGate(id).Release();
            Gate(id).Release();
        }
    }

    public async ValueTask<string> FinishTransfer(MongoId sessionId, Func<ValueTask<string>> nativeRegenerate,
        Func<Task<long>> nativeSave, CancellationToken ct)
    {
        string id = sessionId.ToString();
        await Gate(id).WaitAsync(ct);
        bool acquiredSave = false;
        ScavJournalEntry? entry = null;
        try
        {
            entry = FindTransfer(sessionId) ?? throw new InvalidOperationException("No owned Scav transfer.");
            if (entry.Phase != ScavPhase.Committed)
                throw new InvalidOperationException("The reserved Player-Scav cannot regenerate before settlement.");
            if (entry.TransferPhase == ScavTransferPhase.Completing)
            {
                if (!DiskMatches(entry, entry.TransferExpectedProfileHash))
                    throw new InvalidOperationException("Scav transfer completion needs recovery; regeneration will not be repeated.");
                MarkTransferComplete(entry);
            }
            if (entry.TransferPhase == ScavTransferPhase.Complete)
            {
                if (entry.TransferResponse == null) throw new InvalidOperationException("Scav death has already completed; no loot transfer remains.");
                return entry.TransferResponse;
            }
            if (!entry.Extracted || entry.TransferPhase != ScavTransferPhase.Pending
                || saves.GetProfile(sessionId).CharacterData!.ScavData!.Inventory?.Equipment.ToString() != entry.ScavEquipmentId)
                throw new InvalidOperationException("Scav transfer ownership changed.");
            await SaveGate(id).WaitAsync(ct); acquiredSave = true;
            entry.TransferBeforeProfileJson = Serialize(saves.GetProfile(sessionId));
            entry.TransferPhase = ScavTransferPhase.Completing; journal.Write(entry);
            RawRegenerate.Value = true;
            try { entry.TransferResponse = await nativeRegenerate(); }
            finally { RawRegenerate.Value = false; }
            var successor = saves.GetProfile(sessionId).CharacterData!.ScavData!;
            if (successor.Id.ToString() != entry.ScavProfileId || successor.Inventory?.Equipment.ToString() == entry.ScavEquipmentId)
                throw new InvalidOperationException("Native Scav regeneration did not produce a distinct next loadout.");
            entry.TransferExpectedProfileHash = ScavJournal.Hash(Serialize(saves.GetProfile(sessionId)));
            journal.Write(entry);
            await SaveAndVerify(entry, nativeSave, entry.TransferExpectedProfileHash);
            MarkTransferComplete(entry);
            return entry.TransferResponse;
        }
        catch (Exception ex)
        {
            if (entry?.TransferPhase == ScavTransferPhase.Completing)
            {
                if (DiskMatches(entry, entry.TransferExpectedProfileHash)) { MarkTransferComplete(entry); return entry.TransferResponse!; }
                Quarantine(entry, ex);
            }
            throw;
        }
        finally
        {
            if (acquiredSave) SaveGate(id).Release();
            Gate(id).Release();
        }
    }
}

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Generators.Bot;
using SPTarkov.Server.Core.Helpers.Bot;
using SPTarkov.Server.Core.Helpers.Profile;
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

/// <summary>
/// Optional, issued-raid-bound continuation. The normal Scav is never generated or
/// used as transfer storage. Only the native PMC end pipeline owns final persistence.
/// </summary>
[Injectable(InjectionType.Singleton)]
public sealed class ScavContinuationService
{
    public static ScavContinuationService? Current { get; private set; }
    public static readonly AsyncLocal<bool> RawSave = new();
    private static readonly AsyncLocal<bool> RawEnd = new();
    private static readonly AsyncLocal<Settlement?> CurrentSettlement = new();
    private const string SystemSender = "59e7125688a45068a6249071";
    private readonly ConcurrentDictionary<string, SemaphoreSlim> gates = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> saveGates = new();
    private readonly ConcurrentDictionary<string, string> issuedRaids = new();
    private readonly ConcurrentDictionary<string, bool> quarantined = new();
    private readonly SaveServer saves;
    private readonly JsonUtil json;
    private readonly ICloner cloner;
    private readonly BotGenerator generator;
    private readonly BotHelper bots;
    private readonly ProfileHelper profiles;
    private readonly InRaidHelper inRaid;
    private readonly TemplateTable templates;
    private readonly ISptLogger<ScavContinuationService> logger;
    private readonly ScavJournal journal;

    private sealed class Settlement(ScavJournalEntry entry, string normalScavJson)
    {
        public readonly ScavJournalEntry Entry = entry;
        public readonly string NormalScavJson = normalScavJson;
        public bool NativeBodyComplete;
        public bool ReceiptAttached;
        public bool Active = true;
    }

    public ScavContinuationService(SaveServer saves, JsonUtil json, ICloner cloner,
        BotGenerator generator, BotHelper bots, ProfileHelper profiles, InRaidHelper inRaid, TemplateTable templates,
        ISptLogger<ScavContinuationService> logger)
    {
        this.saves = saves; this.json = json; this.cloner = cloner; this.generator = generator;
        this.bots = bots; this.profiles = profiles; this.inRaid = inRaid; this.templates = templates; this.logger = logger;
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
        string id = sessionId.ToString();
        foreach (var entry in journal.ForSession(id))
        {
            if (!ScavStatePolicy.BlocksNewRaid(entry.Phase)) continue;
            if (DiskHasReceipt(entry)) { MarkCommitted(entry); continue; }
            throw new InvalidOperationException("Redeploy: finish or recover the pending PMC-to-Scav raid before starting another raid.");
        }
    }

    public void RegisterStart(MongoId sessionId, StartLocalRaidRequestData request, StartLocalRaidResponseData response)
    {
        string marker = request.ServerId ?? "";
        bool arena = marker.StartsWith("arenamode-start:", StringComparison.OrdinalIgnoreCase)
            || response.ServerId?.Contains("|arenamode:", StringComparison.OrdinalIgnoreCase) == true;
        if (!arena && string.Equals(request.PlayerSide, "pmc", StringComparison.OrdinalIgnoreCase)
            && (request.TransitionType == null || request.TransitionType == TransitionType.NONE)
            && IsPmcServer(response.ServerId))
            issuedRaids[sessionId.ToString()] = response.ServerId!;
        else issuedRaids.TryRemove(sessionId.ToString(), out _);
    }

    public async Task<ScavResponse> Prepare(MongoId sessionId, ScavPrepareRequest request, CancellationToken ct)
    {
        string id = sessionId.ToString();
        await Gate(id).WaitAsync(ct);
        try
        {
            if (request.ProtocolVersion != 1 || !Guid.TryParse(request.ClientRequestId, out _)
                || request.InitialServerId.Length > 256 || !IsPmcServer(request.InitialServerId))
                return ScavResponse.Rejected("invalid-prepare-request");
            string frozenJson = Serialize(request.FrozenPmcDeath);
            if (frozenJson.Length > 16_000_000) return ScavResponse.Rejected("profile-too-large");
            string hash = ScavJournal.Hash(frozenJson);
            var existing = journal.Read(id, request.InitialServerId);
            if (existing != null)
            {
                if (!ScavStatePolicy.SamePreparedRequest(existing, request.ClientRequestId, hash))
                    return ScavResponse.Rejected("continuation-already-prepared");
                if (existing.Phase is not (ScavPhase.Prepared or ScavPhase.Active))
                    return ScavResponse.Rejected("continuation-closed");
                if (existing.Phase == ScavPhase.Prepared && existing.ExpiresAt < Now)
                    return ScavResponse.Rejected("prepare-expired");
                return PreparedResponse(existing);
            }
            if (!issuedRaids.TryGetValue(id, out var issued) || issued != request.InitialServerId)
                return ScavResponse.Rejected("no-issued-pmc-raid");
            if (journal.ForSession(id).Any(x => ScavStatePolicy.BlocksNewRaid(x.Phase)))
                return ScavResponse.Rejected("previous-continuation-pending");
            var full = saves.GetProfile(sessionId);
            var pmc = full.CharacterData!.PmcData!;
            var frozen = Parse<EndLocalRaidRequestData>(frozenJson);
            if (frozen.ServerId != issued || frozen.Results?.Result != ExitStatus.KILLED
                || frozen.Results.Profile?.Id != pmc.Id || frozen.Results.Profile?.Info?.Side == "Savage"
                || frozen.Results.PlayTime is null or < 0 or > 86400
                || frozen.LocationTransit != null || !ScavTransferPolicy.ContainsNoTransferredItems(frozen.TransferItems))
                return ScavResponse.Rejected("invalid-frozen-pmc-death");
            var frozenPmc = frozen.Results!.Profile!;
            ScavInventoryPolicy.Carried(frozenPmc, templates.Items.ContainsKey);
            // Insurance authority remains the server's original PMC data.
            frozenPmc.InsuredItems = cloner.Clone(pmc.InsuredItems ?? []);
            frozen.Results.ExitName = null;
            frozen.Results.InSession = false;
            frozen.LocationTransit = null;
            // Empty native BTR/transit roots carry no delivery authority.
            frozen.TransferItems = null;
            var generated = Generate(sessionId, pmc, full.CharacterData.ScavData!);
            var generatedJson = Serialize(generated);
            var entry = new ScavJournalEntry
            {
                SessionId = id, InitialServerId = issued, PmcProfileId = pmc.Id!.Value.ToString(),
                ScavProfileId = generated.Id!.Value.ToString(), RequestId = request.ClientRequestId,
                PrepareHash = hash, Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
                ScavJson = generatedJson, GenerationHash = ScavJournal.Hash(generatedJson),
                FrozenPmcJson = Serialize(frozen), OriginalInsuredJson = Serialize(pmc.InsuredItems ?? []),
                ExpiresAt = Now + 600, Phase = ScavPhase.Prepared, ReceiptId = new MongoId().ToString()
            };
            journal.Write(entry); // Only now can a client receive/activate this identity.
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

    private PmcData Generate(MongoId sessionId, PmcData pmc, PmcData normalScav)
    {
        var source = cloner.Clone(pmc) ?? throw new InvalidOperationException("PMC clone failed.");
        var template = cloner.Clone(bots.GetBotTemplate("assault") ?? throw new InvalidOperationException("No assault template."))!;
        var scav = generator.GeneratePlayerScav(sessionId, "assault", "easy", template, source);
        scav.Id = new MongoId(); scav.Savage = null; scav.Aid = source.Aid;
        scav.SessionId = normalScav.SessionId ?? source.SessionId;
        scav.Info!.Side = "Savage";
        scav.Info.Settings = new(); scav.Info.Bans = [];
        scav.Info.RegistrationDate = source.Info!.RegistrationDate;
        scav.Info.GameVersion = source.Info.GameVersion;
        scav.Info.MemberCategory = MemberCategory.UniqueId;
        scav.Info.LockedMoveCommands = true;
        scav.Info.MainProfileNickname = source.Info.Nickname;
        scav.TradersInfo = source.TradersInfo;
        scav.RagfairInfo = source.RagfairInfo; scav.UnlockedInfo = source.UnlockedInfo;
        scav.Encyclopedia = source.Encyclopedia ?? [];
        scav.Notes ??= new Notes { DataNotes = [] };
        scav.WishList ??= new(); scav.Variables ??= [];
        scav.Quests = []; scav.TaskConditionCounters = []; scav.InsuredItems = [];
        scav = profiles.RemoveSecureContainer(scav);
        var generatedItems = ScavInventoryPolicy.Carried(scav, templates.Items.ContainsKey);
        var originalIds = pmc.Inventory!.Items!.Select(x => x.Id.ToString()).ToHashSet(StringComparer.Ordinal);
        if (generatedItems.Any(x => originalIds.Contains(x.Id.ToString())))
            throw new InvalidDataException("Generated Scav reused PMC gear IDs.");
        return scav;
    }

    public async Task<ScavResponse> Control(MongoId sessionId, ScavControlRequest request, bool cancel, CancellationToken ct)
    {
        string id = sessionId.ToString();
        await Gate(id).WaitAsync(ct);
        try
        {
            var entry = journal.Read(id, request.InitialServerId);
            if (entry == null || !ScavStatePolicy.MatchesToken(entry, request.Token))
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
                // Body may already be authoritative even if an activation HTTP response was lost.
                // A registered identity remains recognizable after expiry; only Prepare expires.
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
        if (RawEnd.Value || IsExecuting(sessionId)) return false;
        if (request.ServerId == null) return false;
        if (journal.Read(sessionId.ToString(), request.ServerId) != null) return true;
        // Retire the exact issued admission even if no conversion was requested. Otherwise
        // a delayed Prepare after ordinary native settlement could arm a stale PMC snapshot.
        if (issuedRaids.TryGetValue(sessionId.ToString(), out var issued) && issued == request.ServerId) return true;
        // Missing journal must never save a temporary Scav as the PMC (also after server restart).
        return IsPmcServer(request.ServerId) && request.Results?.Profile?.Id != saves.GetProfile(sessionId).CharacterData?.PmcData?.Id;
    }

    public async Task End(MongoId sessionId, EndLocalRaidRequestData request,
        Func<EndLocalRaidRequestData, Task> nativeEnd, CancellationToken ct)
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
                // No journal or profile mutation for ordinary raids. Hold the same gate as
                // Prepare/Control across the native end, and never reopen admission on failure.
                await InvokeNative(nativeEnd, request);
                return;
            }
            if (incomingId == entry.PmcProfileId && entry.Phase is ScavPhase.Prepared or ScavPhase.Cancelled)
            {
                entry.Phase = ScavPhase.Cancelled; journal.Write(entry);
                ForgetIssuedRaid(id, entry.InitialServerId);
                // Ordinary death after failed preparation/body creation must remain ordinary.
                await InvokeNative(nativeEnd, request);
                return;
            }
            if (incomingId != entry.ScavProfileId) throw new InvalidOperationException("Continuation profile identity mismatch.");
            string finalJson = Serialize(request);
            string finalHash = ScavJournal.Hash(finalJson);
            if (entry.FinalHash != null && entry.FinalHash != finalHash)
                throw new InvalidOperationException("Conflicting continuation end retry.");
            if (entry.Phase == ScavPhase.Committed || DiskHasReceipt(entry))
            {
                MarkCommitted(entry); return;
            }
            if (entry.Phase == ScavPhase.Cancelled || ScavStatePolicy.NeedsRecovery(entry.Phase))
                throw new InvalidOperationException("Continuation needs recovery; native settlement will not be replayed.");
            var frozen = Parse<EndLocalRaidRequestData>(entry.FrozenPmcJson);
            var finalScav = request.Results!.Profile!;
            if (!string.Equals(finalScav.Info?.Side, "Savage", StringComparison.OrdinalIgnoreCase)
                || request.Results.Result == null || request.Results.Result == ExitStatus.TRANSIT
                || request.LocationTransit != null || !ScavTransferPolicy.ContainsNoTransferredItems(request.TransferItems))
                throw new InvalidDataException("Unsupported Scav continuation result.");
            var carried = ScavInventoryPolicy.Carried(finalScav, templates.Items.ContainsKey);
            var frozenPmc = frozen.Results!.Profile!;
            // Match native LostOnDeathConfig, including retained pocket/special slots and custom rules.
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
            frozen.Results.Profile!.InsuredItems = originalInsurance;
            frozen.LostInsuredItems = ScavInventoryPolicy.LostInsurance(request.LostInsuredItems,
                originalInsurance.Select(x => x.ItemId!.Value.ToString()).ToHashSet(StringComparer.Ordinal), recovered, protectedIds);
            var loot = ScavInventoryPolicy.LootItems(carried, finalScav.Inventory!.Equipment!.Value.ToString());
            var mailed = extracted ? cloner.Clone(loot)! : [];
            ScavInventoryPolicy.ApplyRunThrough(mailed, request.Results.Result);
            var receipt = MakeReceipt(entry, mailed, extracted);
            entry.FinalJson = finalJson; entry.FinalHash = finalHash;
            entry.ReceiptJson = Serialize(receipt);
            await SaveGate(id).WaitAsync(ct); acquiredSave = true;
            var full = saves.GetProfile(sessionId);
            entry.BeforeProfileJson = Serialize(full);
            entry.Phase = ScavPhase.Settling;
            journal.Write(entry); // Durable latch BEFORE any native post-raid side effect.
            CurrentSettlement.Value = new(entry, Serialize(full.CharacterData!.ScavData));
            try { await InvokeNative(nativeEnd, frozen); }
            catch (Exception)
            {
                // Backup/notification failures can occur after the successful native save.
                if (!DiskHasReceipt(entry)) throw;
            }
            if (!DiskHasReceipt(entry)) throw new IOException("Continuation receipt was not persisted.");
            MarkCommitted(entry);
            ForgetIssuedRaid(id, entry.InitialServerId);
            logger.Info(extracted ? "Redeploy: Scav extraction completed; recovered equipment is available in messages."
                : "Redeploy: Scav continuation ended; PMC death and insurance were saved.");
        }
        catch (Exception ex)
        {
            if (entry?.Phase == ScavPhase.Settling)
            {
                if (DiskHasReceipt(entry)) { MarkCommitted(entry); return; }
                quarantined[id] = true;
                entry.Phase = ScavPhase.RecoveryRequired;
                entry.Error = ex.GetType().Name;
                journal.Write(entry);
                logger.Error("Redeploy: Scav settlement could not be verified. Profile saving is paused to prevent duplicate insurance/loot; retain the private continuation journal for recovery.");
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

    private void ForgetIssuedRaid(string session, string raid)
        => ((ICollection<KeyValuePair<string, string>>)issuedRaids).Remove(new(session, raid));

    private void MarkCommitted(ScavJournalEntry entry)
    {
        entry.Phase = ScavPhase.Committed;
        entry.Error = null;
        ScavStatePolicy.CompactCommitted(entry);
        journal.Write(entry);
    }

    private static async Task InvokeNative(Func<EndLocalRaidRequestData, Task> native, EndLocalRaidRequestData request)
    {
        RawEnd.Value = true;
        try { await native(request); }
        finally { RawEnd.Value = false; }
    }

    private Message MakeReceipt(ScavJournalEntry entry, List<Item> items, bool extracted)
    {
        var stash = new MongoId();
        return new Message
        {
            Id = new MongoId(entry.ReceiptId), UserId = new MongoId(SystemSender), MessageType = MessageType.SystemMessage,
            DateTime = Now, Text = extracted ? "Redeploy: your Scav extracted. Recovered equipment is attached."
                : "Redeploy: the Scav continuation ended. Your PMC death and insurance have been recorded.",
            HasRewards = items.Count > 0, RewardCollected = false, MaxStorageTime = 7 * 86400,
            Items = items.Count > 0 ? new MessageItems { Stash = stash, Data = ScavInventoryPolicy.MailItems(items, stash) } : null
        };
    }

    public void NativePmcHandled(MongoId sessionId, SptProfile profile)
    {
        var context = CurrentSettlement.Value;
        if (context is not { Active: true } || context.Entry.SessionId != sessionId.ToString()) return;
        profile.CharacterData!.ScavData = Parse<PmcData>(context.NormalScavJson);
        context.NativeBodyComplete = true;
    }

    public async Task<long> Save(MongoId sessionId, Func<Task<long>> nativeSave, CancellationToken ct)
    {
        string id = sessionId.ToString();
        var context = CurrentSettlement.Value;
        bool ownsSettlement = context is { Active: true } && context.Entry.SessionId == id;
        if (!ownsSettlement) await SaveGate(id).WaitAsync(ct);
        try
        {
            if (quarantined.ContainsKey(id)) throw new InvalidOperationException("Redeploy continuation profile requires recovery before saving.");
            if (ownsSettlement)
            {
                if (!context!.NativeBodyComplete) throw new InvalidOperationException("Premature save during PMC-to-Scav settlement.");
                if (!context.ReceiptAttached)
                {
                    AddReceipt(saves.GetProfile(sessionId), Parse<Message>(context.Entry.ReceiptJson!));
                    context.ReceiptAttached = true;
                }
            }
            RawSave.Value = true;
            return await nativeSave();
        }
        finally
        {
            RawSave.Value = false;
            if (!ownsSettlement) SaveGate(id).Release();
        }
    }

    public static void AddReceipt(SptProfile full, Message message)
    {
        full.DialogueRecords ??= [];
        MongoId sender = new(SystemSender);
        if (!full.DialogueRecords.TryGetValue(sender, out var dialog))
            full.DialogueRecords[sender] = dialog = new Dialogue
                { Id = sender, Type = MessageType.SystemMessage, Messages = [], New = 0, AttachmentsNew = 0, Pinned = false };
        dialog.Messages ??= [];
        if (dialog.Messages.Any(x => x.Id == message.Id)) return;
        dialog.Messages.Add(message); dialog.New = (dialog.New ?? 0) + 1;
        if (message.HasRewards == true) dialog.AttachmentsNew = (dialog.AttachmentsNew ?? 0) + 1;
    }

    private bool DiskHasReceipt(ScavJournalEntry entry)
    {
        // Same relative root as installed SaveServer; never inspect a different deployment.
        string path = Path.GetFullPath(Path.Combine("user", "profiles", entry.SessionId + ".json"));
        if (!File.Exists(path)) return false;
        try
        {
            var profile = json.DeserializeFromFile<SptProfile>(path);
            return profile?.DialogueRecords?.Values.Any(x => x.Messages?.Any(m => m.Id.ToString() == entry.ReceiptId) == true) == true;
        }
        catch (IOException) { return false; }
        catch (System.Text.Json.JsonException) { return false; }
    }
}

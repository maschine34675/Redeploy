using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Redeploy.Server.Scav;

public enum ScavPhase { Prepared, Active, Settling, Committed, Cancelled, RecoveryRequired }

/// <summary>Private write-ahead record. Never ship this directory or log its payloads.</summary>
public sealed record ScavJournalEntry
{
    public int Version { get; set; } = 1;
    public string SessionId { get; set; } = "";
    public string InitialServerId { get; set; } = "";
    public string PmcProfileId { get; set; } = "";
    public string ScavProfileId { get; set; } = "";
    public string RequestId { get; set; } = "";
    public string PrepareHash { get; set; } = "";
    public string Token { get; set; } = "";
    public string GenerationHash { get; set; } = "";
    public string ScavJson { get; set; } = "";
    public string FrozenPmcJson { get; set; } = "";
    public string OriginalInsuredJson { get; set; } = "";
    public long ExpiresAt { get; set; }
    public ScavPhase Phase { get; set; }
    public string? FinalHash { get; set; }
    public string? FinalJson { get; set; }
    public string? BeforeProfileJson { get; set; }
    public string? ReceiptJson { get; set; }
    public string ReceiptId { get; set; } = "";
    public string? Error { get; set; }
}

public sealed class ScavJournal(string directory)
{
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private string SessionDirectory(string session) => Path.Combine(directory, Hash(session));
    private string PathFor(string session, string raid) => Path.Combine(SessionDirectory(session), Hash(raid) + ".json");
    public ScavJournalEntry? Read(string session, string raid)
    {
        string path = PathFor(session, raid);
        if (!File.Exists(path)) return null;
        var entry = JsonSerializer.Deserialize<ScavJournalEntry>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Invalid Scav continuation journal.");
        if (entry.Version != 1 || entry.SessionId != session || entry.InitialServerId != raid)
            throw new InvalidDataException("Scav continuation journal identity mismatch.");
        return entry;
    }
    public IEnumerable<ScavJournalEntry> ForSession(string session)
    {
        string sessionDirectory = SessionDirectory(session);
        if (!Directory.Exists(sessionDirectory)) yield break;
        foreach (string path in Directory.EnumerateFiles(sessionDirectory, "*.json"))
        {
            var entry = JsonSerializer.Deserialize<ScavJournalEntry>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Invalid Scav continuation journal.");
            if (entry.Version != 1 || entry.SessionId != session || Path.GetFullPath(path) != Path.GetFullPath(PathFor(session, entry.InitialServerId)))
                throw new InvalidDataException("Scav continuation journal identity mismatch.");
            yield return entry;
        }
    }
    public void Write(ScavJournalEntry entry)
    {
        string path = PathFor(entry.SessionId, entry.InitialServerId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, entry);
            stream.Flush(true);
        }
        File.Move(temp, path, true);
    }
}

public static class ScavStatePolicy
{
    public static void CompactCommitted(ScavJournalEntry entry)
    {
        if (entry.Phase != ScavPhase.Committed) throw new InvalidOperationException("Only a verified commit can be compacted.");
        entry.Token = entry.ScavJson = entry.FrozenPmcJson = entry.OriginalInsuredJson = "";
        entry.FinalJson = entry.BeforeProfileJson = entry.ReceiptJson = null;
    }
    public static bool CanCancel(ScavPhase phase) => phase is ScavPhase.Prepared or ScavPhase.Cancelled;
    public static bool NeedsRecovery(ScavPhase phase) => phase is ScavPhase.Settling or ScavPhase.RecoveryRequired;
    // Prepared can already own an authoritative body when Activate/its response was lost.
    // Expiry stops another generation response; it cannot prove that no body was committed.
    public static bool BlocksNewRaid(ScavPhase phase) => phase is ScavPhase.Prepared or ScavPhase.Active or ScavPhase.Settling or ScavPhase.RecoveryRequired;
    public static bool SamePreparedRequest(ScavJournalEntry entry, string requestId, string hash)
        => entry.RequestId == requestId && entry.PrepareHash == hash;
    public static bool MatchesToken(ScavJournalEntry entry, string token)
    {
        if (token.Length != 64 || entry.Token.Length != 64) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(entry.Token), Encoding.ASCII.GetBytes(token));
    }
}

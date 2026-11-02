// Minimal schema fixture for the released Raid Review 1.10.0 contract. Every production-used
// field/property/method is compared with the SHA-pinned released assembly by PublishedMetadata.
using EFT;
namespace RAID_REVIEW;
public class TrackingRaid
{
    public string sessionId { get; set; } public string profileId { get; set; }
    public ExitStatus exitStatus { get; set; } public long timeInRaid { get; set; }
    public string type { get; set; } = "PMC";
}
public class TrackingPlayer
{
    public string sessionId { get; set; } public string profileId { get; set; } public string name { get; set; }
    public string type { get; set; } = "HUMAN"; public string mod_SAIN_brain { get; set; } = "PLAYER";
    public int level { get; set; } public EPlayerSide team { get; set; } public int group { get; set; } public long spawnTime { get; set; }
    public string mod_SAIN_difficulty { get; set; } public string mod_SAIN_name { get; set; }
}
public class TrackingPlayerData
{
    public string sessionId { get; set; } public string profileId { get; set; } public long time { get; set; }
}
public class TrackingPlayerDeadOrUnspawned
{
    public string sessionId { get; set; } public string profileId { get; set; } public long time { get; set; }
    public PlayerStatus status { get; set; }
}
public enum PlayerStatus { Alive, Dead, Unspawned, Unknown, Extracted, Downed }
public class TrackingRaidKill
{
    public string sessionId { get; set; } public string profileId { get; set; } public string killedId { get; set; } public long time { get; set; }
}
public class TrackingBallistic
{
    public string sessionId { get; set; } public string profileId { get; set; } public string hitPlayerId { get; set; } public long time { get; set; }
}
public class TrackingLootItem
{
    public string sessionId { get; set; } public string profileId { get; set; } public string itemId { get; set; }
    public bool added { get; set; } public long time { get; set; }
}
public class TrackingPlayerInventory
{
    public string sessionId { get; set; } public string profileId { get; set; } public long time { get; set; }
}

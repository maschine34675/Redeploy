using CorpseRun.Config;
using EFT;
using EFT.Game.Spawning;
using UnityEngine;

namespace CorpseRun.Services;

public static class RespawnSessionState
{
    public static bool IsInRaid { get; private set; }
    public static bool PendingRespawnChoice { get; set; }
    public static bool IsRespawnInProgress { get; set; }
    public static bool HasGivenUp { get; set; }
    public static bool UsedRespawnThisRaid { get; set; }
    public static int RespawnsUsed { get; private set; }
    public static int DeathsThisRaid { get; private set; }
    public static LocalGame CurrentLocalGame { get; set; }
    public static Vector3 LastCorpsePosition { get; set; }
    public static ISpawnSystem CachedSpawnSystem { get; set; }
    public static RespawnLocationMode SelectedSpawnMode { get; set; } = RespawnLocationMode.NearCorpse;
    public static bool RespawnItemAvailable { get; set; } = true;

    public static void OnRaidStarted()
    {
        Reset();
        IsInRaid = true;
    }

    public static void OnRaidEnded()
    {
        Reset();
    }

    public static void RegisterDeath(Vector3 corpsePosition)
    {
        DeathsThisRaid++;
        LastCorpsePosition = corpsePosition;
        PendingRespawnChoice = true;
    }

    public static void RegisterRespawnUsed()
    {
        RespawnsUsed++;
        UsedRespawnThisRaid = true;
        PendingRespawnChoice = false;
    }

    private static void Reset()
    {
        IsInRaid = false;
        PendingRespawnChoice = false;
        IsRespawnInProgress = false;
        HasGivenUp = false;
        UsedRespawnThisRaid = false;
        RespawnsUsed = 0;
        DeathsThisRaid = 0;
        CurrentLocalGame = null;
        LastCorpsePosition = default;
        CachedSpawnSystem = null;
        SelectedSpawnMode = RespawnLocationMode.NearCorpse;
        RespawnItemAvailable = true;
    }
}

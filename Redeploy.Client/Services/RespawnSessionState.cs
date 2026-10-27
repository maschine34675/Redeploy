using Redeploy.Config;
using EFT;
using UnityEngine;

namespace Redeploy.Services;

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

    /// <summary>Time.time of the last death this raid; negative when there was none.</summary>
    public static float LastDeathTime { get; private set; } = -1f;
    public static RespawnLocationMode SelectedSpawnMode { get; set; } = RespawnLocationMode.NearCorpse;
    public static bool RespawnItemAvailable { get; set; } = true;

    public static void OnRaidStarted()
    {
        // The choke point both raid patches funnel through, which is why the
        // cursor is released here rather than in each of them: a raid that
        // ends with the prompt still open used to leave it drawing nothing and
        // the game took its cursor back by itself. It no longer does, so the
        // prompt has to be told, on every path.
        UI.RespawnPromptPanel.Instance?.Hide();
        Reset();
        PmcScavSession.ResetForRaidStart();
        IsInRaid = true;
    }

    public static void OnRaidEnded()
    {
        UI.RespawnPromptPanel.Instance?.Hide();
        Reset();
    }

    public static void RegisterDeath(Vector3 corpsePosition)
    {
        DeathsThisRaid++;
        LastCorpsePosition = corpsePosition;
        LastDeathTime = Time.time;
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
        LastDeathTime = -1f;
        Core.Spawning.SpawnSystemCache.Clear();
        SelectedSpawnMode = RespawnLocationMode.NearCorpse;
        RespawnItemAvailable = true;
    }
}

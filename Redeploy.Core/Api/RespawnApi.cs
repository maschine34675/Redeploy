using EFT;
using Redeploy.Core.Engine;
using System;

namespace Redeploy.Core.Api;

/// <summary>
/// Frozen respawn contract for other mods (design: docs/RESPAWN-API.md, section 3).
/// Assembly simple name (frozen): maschine-Redeploy.Core. Reflection name (frozen, no
/// Version/Culture/PublicKeyToken): "Redeploy.Core.Api.RespawnApi, maschine-Redeploy.Core".
///
/// BCL/EFT types only; bindable by reflection or by reference. Unity main thread only.
/// Each subscriber runs in its own try/catch; handlers whose target is a destroyed
/// UnityEngine.Object are skipped. Evolution is additive only: a new member means
/// ApiVersion 2 and a new baseline (Redeploy.Core/Api/api-v1.baseline.txt guards v1).
/// </summary>
public static class RespawnApi
{
    /// <summary>
    /// Contract level of the INSTALLED Core. A property, never a const: a const would be
    /// baked into consumers. Fix-level Core releases do not change it; RedeployCore.Version
    /// reports those.
    /// </summary>
    public static int ApiVersion => 1;

    // ---- frozen literals (values never change; later versions may add new ones: treat unknown values generically) ----

    /// <summary>Replaced reason: the old body was dead at takeover and stays in the world as the corpse.</summary>
    public const string ReasonDeath = "death";

    /// <summary>Replaced reason: the old body was alive at takeover (kit swap, round break); its requester disposes it later.</summary>
    public const string ReasonSwap = "swap";

    /// <summary>ReplacementFailed stage: failed before a replacement body existed.</summary>
    public const string StagePrepare = "prepare";

    /// <summary>ReplacementFailed stage: creation faulted or timed out.</summary>
    public const string StageCreate = "create";

    /// <summary>ReplacementFailed stage: the raid began stopping while creation was pending.</summary>
    public const string StageRaidEnding = "raid-ending";

    /// <summary>ReplacementFailed stage: failed while wiring the created body.</summary>
    public const string StageRewire = "rewire";

    // ---- queries ----

    /// <summary>
    /// Core can replace the local body in this session: keep-alive patches installed, at
    /// least one driver registered, Fika not loaded (evaluated in this order; the Fika probe
    /// runs last). When false, Core is idle: it claims no death, blocks nothing and rejects
    /// RunRespawn. Says nothing about whether a given death will be respawned. A false read
    /// before the chainloader has finished is not final.
    /// </summary>
    public static bool IsAvailable => DeathInterception.IsAvailable;

    /// <summary>
    /// A replacement pipeline is running: from RunRespawn entry (after the preflight) to
    /// its synchronous tail, plus one frame on failure.
    /// </summary>
    public static bool IsReplacing => RespawnEngineState.IsPipelineRunning;

    /// <summary>
    /// Is a Core driver holding the raid open for the death of <paramref name="body"/>
    /// (null = game.LocalPlayer)?
    ///
    /// true: a driver claimed this death and has not released it; inside Player.OnDead
    /// postfixes and OnPlayerDeadStatic handlers (before CG_Spawn): a driver is predicted to
    /// claim it. A replacement is possible, not promised: the player may give up, a required
    /// item may be missing, the mode may end the match, and a Killed Stop can still follow.
    /// The pre-claim value is a heuristic: it knows a driver will claim, not whether that
    /// driver will replace (known case: the match-deciding death in ArenaMode Team DM
    /// predicts true and turns false right after the claim). Always true while DeathClaimed
    /// is being raised.
    ///
    /// false: Core will not replace this body (not the current local body, alive, bot,
    /// unclaimed, released, resolved, raid stopping, Core unavailable). false is NOT "final
    /// death": a respawn mod outside Core may act.
    /// </summary>
    public static bool IsRespawnPending(Player body) => RespawnClaims.IsPending(body);

    // ---- events ----

    /// <summary>
    /// (deadBody, driverName) A driver claimed the local player's death. Raised in the
    /// CG_Spawn prefix, after the vanilla death events and before the driver acts. A claim,
    /// not a promise: a Killed Stop may follow.
    /// </summary>
    public static event Action<Player, string> DeathClaimed;

    /// <summary>
    /// (oldBody, newBody, reason) The local body was replaced and is fully wired. Raised
    /// synchronously in the replacing frame, before spawn protection and before the
    /// requester's own follow-up work.
    /// </summary>
    public static event Action<Player, Player, string> Replaced;

    /// <summary>
    /// (oldBody, stage) A started replacement ended without Replaced. Core does not roll
    /// back: drop cached bodies and read GameWorld.MainPlayer again in the next frame. Never
    /// raised after RaidEnded of the same game. oldBody is null only if the request named
    /// no body and the game had no local player at entry (no current requester does that).
    /// </summary>
    public static event Action<Player, string> ReplacementFailed;

    /// <summary>
    /// A LocalGame raid ended (GameEnd postfix, after the drivers and Core have reset).
    /// Raised once per raid. Never raised for Fika's CoopGame, the hideout or trader
    /// narrate scenes.
    /// </summary>
    public static event Action RaidEnded;

    // ---- engine-internal (not part of the contract) ----

    internal static void PublishDeathClaimed(Player body, string driver)
        => CoreSignals.Respawn.Raise(DeathClaimed, h => h(body, driver), nameof(DeathClaimed));

    internal static void PublishReplaced(Player oldBody, Player newBody, string reason)
        => CoreSignals.Respawn.Raise(Replaced, h => h(oldBody, newBody, reason), nameof(Replaced));

    internal static void PublishReplacementFailed(Player oldBody, string stage)
        => CoreSignals.Respawn.Raise(ReplacementFailed, h => h(oldBody, stage), nameof(ReplacementFailed));

    internal static void PublishRaidEnded()
        => CoreSignals.Lifecycle.Raise(RaidEnded, h => h(), nameof(RaidEnded));
}

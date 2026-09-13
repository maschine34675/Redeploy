using System;
using System.Collections;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using Redeploy.Core;
using Redeploy.Core.Api;
using Redeploy.Core.Engine;
using Redeploy.Core.Spawning;
using UnityEngine;

namespace RespawnApiProbe;

/// <summary>
/// The compiled half of the probe. One greppable line per observation, all starting with
/// "[probe] evt=". Every line carries the frame and the facade state (8.5): IsReplacing,
/// IsAvailable, the current MainPlayer; event lines add IsRespawnPending for their body.
/// Bodies print as local#instanceId / other#instanceId / destroyed#instanceId / null.
/// </summary>
internal sealed class ProbeRuntime : MonoBehaviour
{
    private const int TrailFrames = 3;

    private GameWorld _world;
    private Player _lastMain;
    private bool _replacingSeen;

    // One-shot latches, cleared at every raid boundary.
    private bool _fromReplacedTried;
    private bool _stoppingTried;
    private bool _swapKillTried;

    // Question 2: a short per-frame trail after each Replaced.
    private Player _trailBody;
    private int _trailFrom;
    private int _trailLeft;

    private void Awake()
    {
        RespawnApi.DeathClaimed += OnDeathClaimed;
        RespawnApi.Replaced += OnReplaced;
        RespawnApi.Replaced += AlwaysThrow;
        RespawnApi.ReplacementFailed += OnReplacementFailed;
        RespawnApi.RaidEnded += OnRaidEnded;

        var tracker = Plugin.Tracker;
        tracker.BodyChanged += OnInteropBodyChanged;
        tracker.DeathClaimed += OnInteropDeathClaimed;
        tracker.ReplacementFailed += OnInteropReplacementFailed;
        tracker.RaidEnded += OnInteropRaidEnded;

        Line("Subscribed", $"apiVersion={RespawnApi.ApiVersion} core={RedeployCore.Version} "
                           + "note=a second Replaced subscriber throws on purpose; Core must log it and keep dispatching");

        EnsurePipelinePatch();
        Plugin.Settings.RunRespawnDuringPipeline.SettingChanged += (_, _) => EnsurePipelinePatch();
    }

    private void OnDestroy()
    {
        RespawnApi.DeathClaimed -= OnDeathClaimed;
        RespawnApi.Replaced -= OnReplaced;
        RespawnApi.Replaced -= AlwaysThrow;
        RespawnApi.ReplacementFailed -= OnReplacementFailed;
        RespawnApi.RaidEnded -= OnRaidEnded;
    }

    private void Update()
    {
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        if (!ReferenceEquals(world, _world))
        {
            _world = world;
            _lastMain = null;
            ResetRaidLatches();
            Line("World", $"world={(world == null ? "null" : world.GetInstanceID().ToString())}");
        }

        if (world == null)
            return;

        var main = world.MainPlayer;
        if (!ReferenceEquals(main, _lastMain))
        {
            Line("MainPlayerChanged", $"from={Id(_lastMain)} to={Id(main)} camera={CameraId()}");
            _lastMain = main;
        }

        if (_trailLeft > 0 && Time.frameCount > _trailFrom)
        {
            Line("Trail", $"sinceReplaced={Time.frameCount - _trailFrom} mainIsNew={ReferenceEquals(main, _trailBody)} "
                          + $"pipelineRunning={RespawnEngineState.IsPipelineRunning} camera={CameraId()}");
            _trailLeft--;
        }

        // A successful LocalGame replacement completes inside one call: the game creates the
        // player with async:false, so IsReplacing never spans a frame on that path. These two
        // lines appear only when a pipeline really waits (a failing or timing-out creation).
        bool replacing = RespawnApi.IsReplacing;
        if (replacing && !_replacingSeen)
        {
            _replacingSeen = true;
            Line("ReplacingStarted", "");
        }
        else if (!replacing && _replacingSeen)
        {
            _replacingSeen = false;
            Line("ReplacingEnded", "");
        }

        var game = CurrentGame();
        if (game != null
            && !_stoppingTried
            && Plugin.Settings.RunRespawnDuringStopping.Value
            && (game.Status == GameStatus.Stopping || game.Status == GameStatus.SoftStopping))
        {
            _stoppingTried = true;
            TryRunRespawn("during-stopping");
        }
    }

    // ---------------------------------------------------------------- direct (compiled) subscribers

    private static void OnDeathClaimed(Player body, string driver)
        => Line("DeathClaimed", $"body={Id(body)} driver={driver} pending={Pending(body)}");

    private void OnReplaced(Player oldBody, Player newBody, string reason)
    {
        Line("Replaced", $"old={Id(oldBody)} new={Id(newBody)} reason={reason} pendingOld={Pending(oldBody)} "
                         + $"mainIsNew={ReferenceEquals(MainPlayer(), newBody)} camera={CameraId()}");

        _trailBody = newBody;
        _trailFrom = Time.frameCount;
        _trailLeft = TrailFrames;

        if (Plugin.Settings.RunRespawnFromReplaced.Value && !_fromReplacedTried)
        {
            _fromReplacedTried = true;
            TryRunRespawn("from-replaced-handler");
        }

        if (Plugin.Settings.KillAfterRaidStartSwap.Value && reason == RespawnApi.ReasonSwap && !_swapKillTried)
        {
            _swapKillTried = true;
            // Never kill synchronously inside a Core handler (3.6); the coroutine yields first.
            StartCoroutine(KillAfterDelay(newBody, Plugin.Settings.KillAfterRaidStartSwapDelaySeconds.Value));
        }
    }

    private static void OnReplacementFailed(Player oldBody, string stage)
        => Line("ReplacementFailed", $"old={Id(oldBody)} stage={stage} pendingOld={Pending(oldBody)}");

    private void OnRaidEnded()
    {
        Line("RaidEnded", "");
        ResetRaidLatches();
    }

    private static void AlwaysThrow(Player oldBody, Player newBody, string reason)
        => throw new InvalidOperationException(
            "RespawnApiProbe: this Replaced subscriber always throws (8.5). Core must log it and still reach every other subscriber.");

    // ---------------------------------------------------------------- interop tracker (reflection-bound)

    private static void OnInteropBodyChanged(Player previous, Player current, string reason)
        => Line("BodyChanged(interop)", $"previous={Id(previous)} current={Id(current)} reason={reason} "
                                       + $"bound={Plugin.Tracker.IsBound} pendingPrevious={InteropPending(previous)}");

    private static void OnInteropDeathClaimed(Player body, string driver)
        => Line("DeathClaimed(interop)", $"body={Id(body)} driver={driver}");

    private static void OnInteropReplacementFailed(Player oldBody, string stage)
        => Line("ReplacementFailed(interop)", $"old={Id(oldBody)} stage={stage}");

    private static void OnInteropRaidEnded()
        => Line("RaidEnded(interop)", "");

    // ---------------------------------------------------------------- scenarios

    private static bool _pipelinePatchApplied;

    /// <summary>
    /// RunRespawnDuringPipeline has to ask from inside a running pipeline, because a
    /// successful one never outlives the call that started it. The pipeline creates the
    /// replacement through BaseLocalGame.vmethod_3 while IsPipelineRunning is true, so a
    /// prefix there sees exactly that window. Patched only once the switch is on.
    /// </summary>
    private static void EnsurePipelinePatch()
    {
        if (_pipelinePatchApplied || !Plugin.Settings.RunRespawnDuringPipeline.Value)
            return;

        _pipelinePatchApplied = true;
        try
        {
            var target = AccessTools.Method(typeof(BaseLocalGame<EftGamePlayerOwner>), "vmethod_3");
            new Harmony(Plugin.PluginGuid).Patch(target,
                prefix: new HarmonyMethod(typeof(ProbeRuntime), nameof(BeforePlayerCreation)));
            Line("Scenario", "name=during-pipeline action=patched target=BaseLocalGame.vmethod_3");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[probe] evt=Scenario name=during-pipeline result=patch-failed message=\"{ex.Message}\"");
        }
    }

    private static void BeforePlayerCreation()
    {
        try
        {
            // The game's own raid-start creation also runs through here, with no pipeline.
            if (Plugin.Settings.RunRespawnDuringPipeline.Value && RespawnEngineState.IsPipelineRunning)
                TryRunRespawn("during-pipeline");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[probe] evt=Scenario name=during-pipeline result=threw message=\"{ex.Message}\"");
        }
    }

    private static void TryRunRespawn(string scenario)
    {
        var game = CurrentGame();
        var body = game?.LocalPlayer;
        if (game == null || body == null)
        {
            Line("Scenario", $"name={scenario} result=skipped reason=no-local-game-or-body");
            return;
        }

        var request = new RespawnRequest
        {
            Game = game,
            DeadPlayer = body,
            SpawnSelector = new SameSpotSelector(body.Position, body.Transform.rotation),
            HealthPercent = 100f,
        };

        Line("Scenario", $"name={scenario} action=RunRespawn body={Id(body)} status={game.Status}");

        // A refusal is reported synchronously through onFailure, before StartCoroutine returns.
        Exception refusal = null;
        game.StartCoroutine(RespawnEngine.RunRespawn(request, failure => refusal = failure));

        if (refusal != null)
            Line("Scenario", $"name={scenario} result=refused error={refusal.GetType().Name} message=\"{refusal.Message}\"");
        else
            Plugin.Log.LogWarning($"[probe] evt=Scenario frame={Time.frameCount} name={scenario} result=ACCEPTED "
                                  + "message=\"the engine started a pipeline it should have refused\"");
    }

    private IEnumerator KillAfterDelay(Player body, float delaySeconds)
    {
        Line("Scenario", $"name=kill-after-raid-start-swap action=scheduled body={Id(body)} delay={delaySeconds:F2}s");
        yield return new WaitForSeconds(Mathf.Max(0f, delaySeconds));

        var health = body != null ? body.ActiveHealthController : null;
        if (body == null || !ReferenceEquals(MainPlayer(), body) || health == null || !health.IsAlive)
        {
            Line("Scenario", "name=kill-after-raid-start-swap result=skipped reason=not-the-living-local-body");
            yield break;
        }

        Line("Scenario", $"name=kill-after-raid-start-swap action=kill body={Id(body)}");
        health.Kill(EDamageType.Undefined);
        Line("Scenario", $"name=kill-after-raid-start-swap result=killed alive={health.IsAlive}");
    }

    private void ResetRaidLatches()
    {
        _fromReplacedTried = false;
        _stoppingTried = false;
        _swapKillTried = false;
        _trailLeft = 0;
        _trailBody = null;
    }

    /// <summary>Resolves where the body stands at request time; the probe never moves anyone.</summary>
    private sealed class SameSpotSelector : ISpawnSelector
    {
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;

        public SameSpotSelector(Vector3 position, Quaternion rotation)
        {
            _position = position;
            _rotation = rotation;
        }

        public (Vector3 position, Quaternion rotation) Resolve(LocalGame game) => (_position, _rotation);
    }

    // ---------------------------------------------------------------- formatting

    private static void Line(string evt, string details)
        => Plugin.Log.LogInfo($"[probe] evt={evt} frame={Time.frameCount} replacing={RespawnApi.IsReplacing} "
                              + $"available={RespawnApi.IsAvailable} main={Id(MainPlayer())}"
                              + (details.Length > 0 ? " " + details : ""));

    private static string Pending(Player body)
    {
        try { return RespawnApi.IsRespawnPending(body).ToString(); }
        catch (Exception ex) { return "threw:" + ex.GetType().Name; }
    }

    private static string InteropPending(Player body)
    {
        bool? pending = Plugin.Tracker.IsRespawnPending(body);
        return pending.HasValue ? pending.Value.ToString() : "null";
    }

    private static LocalGame CurrentGame()
        => Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;

    private static Player MainPlayer()
        => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;

    private static string Id(Player player)
    {
        if (ReferenceEquals(player, null)) return "null";
        if (player == null) return "destroyed#" + player.GetInstanceID();
        return (player.IsYourPlayer ? "local#" : "other#") + player.GetInstanceID();
    }

    private static string CameraId()
    {
        try
        {
            var camera = CameraManager.Instance != null ? CameraManager.Instance.Camera : null;
            return camera == null ? "null" : camera.GetInstanceID().ToString();
        }
        catch (Exception ex)
        {
            return "threw:" + ex.GetType().Name;
        }
    }
}

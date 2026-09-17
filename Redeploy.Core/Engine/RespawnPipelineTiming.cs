using Comfort.Common;
using EFT;
using EFT.EnvironmentEffect;
using EFT.GlobalEvents;
using HarmonyLib;
using Redeploy.Core.GameAccess;
using System;
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Redeploy.Core.Engine;

/// <summary>
/// Measures one RunRespawn, because the pipeline gets slower with every respawn of a raid
/// (ArenaMode LONG-RAID-REVIEW-2026-09-16.md: 0.50 s to 1.02 s over 26 respawns) and the cause is
/// to be measured, not guessed. Writes one Info line per pipeline:
///
///   Respawn pipeline #7 (death, replaced) on factory4_day: 812.4 ms in 0 frame(s) | teardown 3.1 (owner 2.8) | ... || restore 4.1 ms (212 victims) | before (read in 0.3 ms): frame 16.9 ms, heap 1843 MB, ...
///
/// and, for a replacement, a second line with the rest of that frame and the frame after, where
/// work the pipeline only scheduled (Start of new components, the first render of the new camera,
/// per-frame cost that grew) shows up. Stages are named group.step; steps under 0.05 ms are left
/// out of the line. The "before" counters are read before the clock starts, so they cost the timed
/// stages nothing: the state each candidate from the research depends on (handlers piling up on
/// shared objects, the kill list cloned twice, the dead-body registry scan, culling jobs).
///
/// What it cannot see: work a driver does before calling RunRespawn (ArenaMode's area modes prepare
/// the spawn point in ArenaAreaRespawn.TryPrepare first, so "spawn" there only unwraps that result),
/// and the inside of "create", which is one call into the game (pooled body, inventory, quest and
/// achievement controllers, Player.Init) that cannot be split without patching those methods.
/// A driver that yields on RunRespawn resumes a frame later, so its own timer also contains the
/// "after" line's first number.
///
/// Measurement only: every member swallows its own failures, and RunRespawn calls it only through
/// inert statements, which the golden-order guard proves (Redeploy.Tests/SourceGuards.cs).
/// </summary>
internal sealed class RespawnPipelineTiming
{
    /// <summary>A scene-wide object count is O(scene): only every n-th respawn, and two frames late.</summary>
    private const int SceneCountEvery = 5;

    private const float MinStepMilliseconds = 0.05f;

    private static readonly RespawnPipelineTiming Disabled = new RespawnPipelineTiming();

    private static int _respawnsThisRaid;
    private static long _restoreTicks;
    private static int _restoredVictims = -1;

    private readonly PipelineStages _stages;
    private readonly int _index;
    private readonly int _startFrame;
    private readonly string _map;
    private readonly string _before;
    private readonly long _beforeTicks;
    private bool _finished;

    private RespawnPipelineTiming()
    {
        _finished = true;
    }

    private RespawnPipelineTiming(LocalGame game)
    {
        _index = ++_respawnsThisRaid;
        _startFrame = Time.frameCount;
        _map = Read(() => game.Location.Id);
        // Outside Core's stages but inside a driver's own respawn timer (ArenaMode's "pipeline"),
        // and some of it scales with the handler counts it reads: timed, so it can be subtracted.
        var reading = Stopwatch.GetTimestamp();
        _before = DescribeState(game);
        _beforeTicks = Stopwatch.GetTimestamp() - reading;
        _restoreTicks = 0;
        _restoredVictims = -1;
        // Last, so reading the counters above is not part of any stage.
        _stages = new PipelineStages(Stopwatch.GetTimestamp(), GC.CollectionCount(0));
    }

    /// <summary>Raid start and raid end: respawn numbers count per raid.</summary>
    internal static void ResetForRaidBoundary()
    {
        _respawnsThisRaid = 0;
    }

    /// <summary>Starts the clock for one pipeline. Never throws; a failure only disables this measurement.</summary>
    public static RespawnPipelineTiming Begin(LocalGame game)
    {
        try
        {
            return new RespawnPipelineTiming(game);
        }
        catch (Exception ex)
        {
            CoreLog.LogWarning($"Respawn pipeline timing is off for this respawn: {ex.Message}");
            return Disabled;
        }
    }

    /// <summary>Closes the step that just ran.</summary>
    public void Mark(string stage)
    {
        if (_finished)
            return;

        try
        {
            _stages.Mark(stage, Stopwatch.GetTimestamp(), GC.CollectionCount(0));
        }
        catch (Exception)
        {
            // Measurement must never cost the respawn anything.
        }
    }

    /// <summary>
    /// The kill-list restore runs inside player creation, in Core's BeginStatisticsSession postfix;
    /// it is timed there and reported next to the create stage it is part of. Known blind spot: a
    /// creation abandoned after the 30 s timeout keeps running and may still restore later, inside
    /// whichever pipeline runs then; the abandoned one has already logged "failed-create-timeout".
    /// </summary>
    internal static void RecordVictimsRestore(long ticks, int victims)
    {
        _restoreTicks += ticks;
        _restoredVictims = victims;
    }

    /// <summary>
    /// Writes the line. <paramref name="game"/> is given only for a replacement: it hosts the short
    /// coroutine that measures the frames after.
    /// </summary>
    public void Finish(string outcome, string reason, LocalGame game)
    {
        if (_finished)
            return;
        _finished = true;

        try
        {
            var frequency = Stopwatch.Frequency;
            var frames = Time.frameCount - _startFrame;
            var line = new StringBuilder(1024)
                .Append("Respawn pipeline #").Append(_index)
                .Append(" (").Append(reason ?? "?").Append(", ").Append(outcome).Append(") on ").Append(_map)
                .Append(": ").Append(PipelineStages.Milliseconds(_stages.TotalTicks, frequency)).Append(" ms in ")
                .Append(frames).Append(" frame(s) | ")
                .Append(_stages.Format(frequency, MinStepMilliseconds))
                .Append(" || restore ").Append(PipelineStages.Milliseconds(_restoreTicks, frequency)).Append(" ms (")
                .Append(_restoredVictims < 0 ? "-" : _restoredVictims.ToString(CultureInfo.InvariantCulture)).Append(" victims)")
                .Append(" | before (read in ").Append(PipelineStages.Milliseconds(_beforeTicks, frequency)).Append(" ms): ").Append(_before);
            CoreLog.LogInfo(line.ToString());

            if (game != null)
                game.StartCoroutine(MeasureFollowingFrames(_index, Stopwatch.GetTimestamp()));
        }
        catch (Exception ex)
        {
            CoreLog.LogWarning($"Respawn pipeline timing could not be written: {ex.Message}");
        }
    }

    private static IEnumerator MeasureFollowingFrames(int index, long finishedAt)
    {
        yield return null;
        var resumed = Stopwatch.GetTimestamp();
        yield return null;
        var next = Stopwatch.GetTimestamp();

        var frequency = Stopwatch.Frequency;
        var line = $"Respawn pipeline #{index} after: {PipelineStages.Milliseconds(resumed - finishedAt, frequency)} ms to the next frame's coroutines, "
                   + $"next frame {PipelineStages.Milliseconds(next - resumed, frequency)} ms";

        if (index % SceneCountEvery == 0)
        {
            // Two frames after the respawn, outside every timed window (Arena's included).
            var started = Stopwatch.GetTimestamp();
            var bodies = Read(() => UnityEngine.Object.FindObjectsOfType<LocalPlayer>().Length);
            line += $", active LocalPlayer objects {bodies} (counted in {PipelineStages.Milliseconds(Stopwatch.GetTimestamp() - started, frequency)} ms)";
        }

        CoreLog.LogInfo(line + ".");
    }

    private static string DescribeState(LocalGame game)
    {
        var profile = game?.Profile;
        var skills = profile?.Skills;
        var world = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;
        BotsController bots = null;
        try
        {
            bots = LocalGameAccessor.GetBotsController(game);
        }
        catch (Exception)
        {
            // Read as "?" below.
        }

        var text = new StringBuilder(512);
        Append(text, "frame", Read(() => (Time.unscaledDeltaTime * 1000f).ToString("0.0", CultureInfo.InvariantCulture) + " ms"));
        Append(text, "heap", Read(() => (GC.GetTotalMemory(false) / (1024 * 1024)).ToString(CultureInfo.InvariantCulture) + " MB"));
        Append(text, "collections", Read(() => GC.CollectionCount(0)));
        Append(text, "alive", Read(() => world.AllAlivePlayersList.Count));
        Append(text, "victims", Read(() => profile.EftStats.Victims.Count));
        Append(text, "deadBodies", Read(() => bots.BotSpawner._deadBodiesController.BodiesCount));
        Append(text, "traderHandlers", Read(() => HandlerCount(Fields.TraderStanding, profile)));
        Append(text, "skillHandlers", Read(() => Fields.SkillEvents.Sum(field => HandlerCount(field, skills))
                                                 + HandlerCount(Fields.ProbabilityResult, skills.ImmunityAvoidPoisonChance)
                                                 + HandlerCount(Fields.ProbabilityResult, skills.ImmunityAvoidMiscEffectsChance)));
        Append(text, "environmentHandlers", Read(() => HandlerCount(Fields.EnvironmentChanged, EnvironmentManager.Instance)));
        Append(text, "flareSubscribers", Read(() => GlobalEventsController.Instance._eventsSubscribes.TryGetValue(typeof(FlareShootZoneEvent), out var list) ? list.Count : 0));
        Append(text, "cullingJobs", Read(() => CullingManager.CullingJob.JobParameters.Count));
        Append(text, "cullingCameras", Read(() => ((IDictionary)Fields.CullingCameras.GetValue(CullingManager.Instance)).Count));
        return text.ToString();
    }

    /// <summary>Subscribers on an event's delegate field; throws (and reads as "?") when the field or target is missing.</summary>
    private static int HandlerCount(FieldInfo field, object target)
    {
        if (field == null || target == null)
            throw new InvalidOperationException("not found");
        return field.GetValue(target) is Delegate handlers ? handlers.GetInvocationList().Length : 0;
    }

    private static void Append(StringBuilder text, string name, string value)
    {
        if (text.Length > 0)
            text.Append(", ");
        text.Append(name).Append(' ').Append(value);
    }

    /// <summary>
    /// Resolved once, in their own type: a reflection or type-load failure here must stay inside
    /// Begin's try, never reach ResetForRaidBoundary or the restore postfix through this class's
    /// static initializer.
    /// </summary>
    private static class Fields
    {
        public static readonly FieldInfo TraderStanding = AccessTools.Field(typeof(Profile), "OnTraderStandingChanged");
        public static readonly FieldInfo EnvironmentChanged = AccessTools.Field(typeof(EnvironmentManager), "_onPlayerEnvironmentChanged");
        public static readonly FieldInfo[] SkillEvents =
        {
            AccessTools.Field(typeof(SkillManager), "OnSkillLevelChanged"),
            AccessTools.Field(typeof(SkillManager), "OnSkillExperienceChanged"),
            AccessTools.Field(typeof(SkillManager), "WeaponMastered"),
            AccessTools.Field(typeof(SkillManager), "OnMasteringExperienceChanged"),
        };
        public static readonly FieldInfo ProbabilityResult = AccessTools.Field(typeof(SkillManager.ProbabilityBuff), "_onResult");
        public static readonly FieldInfo CullingCameras = AccessTools.Field(typeof(CullingManager), "_cameras");
    }

    private static string Read<T>(Func<T> read)
    {
        try
        {
            var value = read();
            return value == null ? "?" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }
        catch (Exception)
        {
            return "?";
        }
    }
}

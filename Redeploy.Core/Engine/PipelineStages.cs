using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Redeploy.Core.Engine;

/// <summary>
/// The stage bookkeeping behind one "Respawn pipeline" log line. Stages are named "group.step";
/// each mark closes the step that just ran, so a step's time is the distance to the previous mark.
/// A step during which the collection count moved is flagged, because a collection inside it can
/// dwarf the step's own work. Pure (no Unity): linked into Redeploy.Tests.
/// </summary>
internal sealed class PipelineStages
{
    private readonly List<string> _groups = new List<string>(8);
    private readonly Dictionary<string, List<Step>> _steps = new Dictionary<string, List<Step>>(8, StringComparer.Ordinal);
    private readonly long _start;
    private long _last;
    private int _lastCollections;

    public PipelineStages(long startTicks, int collections)
    {
        _start = startTicks;
        _last = startTicks;
        _lastCollections = collections;
    }

    public long TotalTicks => _last - _start;

    /// <summary>Closes the step "group.step" (a name without a dot is its own group) at <paramref name="nowTicks"/>.</summary>
    public void Mark(string stage, long nowTicks, int collections)
    {
        var dot = stage.IndexOf('.');
        var group = dot < 0 ? stage : stage.Substring(0, dot);
        var step = dot < 0 ? null : stage.Substring(dot + 1);

        if (!_steps.TryGetValue(group, out var steps))
        {
            _steps[group] = steps = new List<Step>(8);
            _groups.Add(group);
        }

        // A clock that runs backwards (it should not) must not produce negative stages.
        steps.Add(new Step(step, Math.Max(0L, nowTicks - _last), collections != _lastCollections));
        _last = Math.Max(_last, nowTicks);
        _lastCollections = collections;
    }

    /// <summary>
    /// "create 690.2* | rewire 55.1 (dictionary 0.1, camera 40.2, ...) | ..." in milliseconds with one
    /// decimal, groups in the order they first ran; * marks a collection during that step. Named steps
    /// shorter than <paramref name="minStepMilliseconds"/> without a collection are left out of the
    /// parentheses; their group total still contains them.
    /// </summary>
    public string Format(long ticksPerSecond, double minStepMilliseconds = 0)
    {
        var text = new StringBuilder(512);
        foreach (var group in _groups)
        {
            var steps = _steps[group];
            long groupTicks = 0;
            var groupCollected = false;
            foreach (var step in steps)
            {
                groupTicks += step.Ticks;
                groupCollected |= step.Collected;
            }

            if (text.Length > 0)
                text.Append(" | ");
            text.Append(group).Append(' ').Append(Milliseconds(groupTicks, ticksPerSecond));
            if (groupCollected)
                text.Append('*');

            var named = steps.FindAll(step => step.Name != null
                                             && (step.Collected || ticksPerSecond <= 0 || step.Ticks * 1000.0 / ticksPerSecond >= minStepMilliseconds));
            if (named.Count == 0)
                continue;

            text.Append(" (");
            for (var i = 0; i < named.Count; i++)
            {
                if (i > 0)
                    text.Append(", ");
                text.Append(named[i].Name).Append(' ').Append(Milliseconds(named[i].Ticks, ticksPerSecond));
                if (named[i].Collected)
                    text.Append('*');
            }
            text.Append(')');
        }
        return text.ToString();
    }

    public static string Milliseconds(long ticks, long ticksPerSecond)
        => ticksPerSecond <= 0
            ? "?"
            : (ticks * 1000.0 / ticksPerSecond).ToString("0.0", CultureInfo.InvariantCulture);

    private readonly struct Step
    {
        public readonly string Name;
        public readonly long Ticks;
        public readonly bool Collected;

        public Step(string name, long ticks, bool collected)
        {
            Name = name;
            Ticks = ticks;
            Collected = collected;
        }
    }
}

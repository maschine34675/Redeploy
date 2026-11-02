using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using EFT;

namespace Redeploy.Core.Compat;

/// <summary>Recording identities only. Never writes a game profile or a player identity.</summary>
internal sealed class RaidReviewLifeLedger
{
    internal sealed class Life
    {
        internal Player Body;
        internal string Real, Alias;
        internal int Number;
        internal bool Retired, Dead;
    }
    private readonly ConditionalWeakTable<Player, Life> _bodies = new();
    private readonly Dictionary<string, Life> _current = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Life> _aliases = new(StringComparer.Ordinal);
    internal Life Current { get; private set; }
    internal Life Find(Player body) => !ReferenceEquals(body, null) && _bodies.TryGetValue(body, out var life) ? life : null;
    internal Life Find(string real) => real != null && _current.TryGetValue(real, out var life) ? life : null;
    internal Life Add(Player body)
    {
        if (Find(body) != null) throw new InvalidOperationException("Raid Review cannot commit the same physical life twice");
        var id = body.ProfileId;
        if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("Raid Review replacement identity is empty");
        var number = _aliases.Count + 1;
        var life = new Life { Body = body, Real = id, Alias = id + ":redeploy:" + number, Number = number };
        _bodies.Add(body, life); _aliases.Add(life.Alias, life); _current[id] = life;
        if (Current != null) { Current.Retired = true; Current.Body = null; }
        return Current = life;
    }
    internal string Name(string alias) => _aliases.TryGetValue(alias, out var life) ? " [life " + life.Number + "]" : "";
    internal string Map(string real, Player physical, bool requirePhysical)
    {
        if (string.IsNullOrEmpty(real)) return real;
        if (_aliases.ContainsKey(real)) return real;
        var bodyLife = Find(physical);
        if (bodyLife != null)
        {
            if (bodyLife.Real != real) throw new InvalidOperationException("Raid Review event/body identity disagrees");
            return bodyLife.Alias;
        }
        var current = Find(real);
        if (current == null) return real; // Bots and unrelated humans are untouched.
        if (requirePhysical) throw new InvalidOperationException("Raid Review local event has no confirmed physical source");
        return current.Alias;
    }
}

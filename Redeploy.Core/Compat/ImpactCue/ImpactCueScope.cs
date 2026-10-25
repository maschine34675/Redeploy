using System;
using System.Runtime.CompilerServices;

namespace Redeploy.Core.Compat;

/// <summary>A prepared operation is read-only; only its exact native unregister grants authority.</summary>
internal sealed class ImpactCueScope
{
    private sealed class Lease { internal object World; internal bool Retired; }
    private sealed class Preparation { internal object Game, World, Body; }
    private readonly ConditionalWeakTable<object, Lease> _bodies = new();
    private Preparation _pending;
    internal object Game { get; private set; }
    internal object World { get; private set; }
    internal object Body { get; private set; }
    internal bool Frozen { get; private set; }
    internal bool Enabled => Body != null;
    internal object PreparedBody => _pending?.Body;
    internal bool OwnsBody(object body) => body != null && _bodies.TryGetValue(body, out _);

    internal void Prepare(object game, object world, object body)
    {
        if (_pending != null || game == null || world == null || body == null
            || (Enabled && (!ReferenceEquals(Game, game) || !ReferenceEquals(World, world) || !ReferenceEquals(Body, body))))
            throw new InvalidOperationException("ImpactCue has conflicting physical ownership");
        Validate(body, world);
        _pending = new Preparation { Game = game, World = world, Body = body };
    }
    internal void AbortPreparation() => _pending = null;
    internal bool Start(object game, object world, object body)
    {
        var p = _pending;
        if (p == null || !ReferenceEquals(p.Game, game) || !ReferenceEquals(p.World, world) || !ReferenceEquals(p.Body, body))
            return false;
        Validate(body, world);
        Game = game; World = world; Body = body; Frozen = true; _pending = null;
        Track(body, world);
        return true;
    }
    internal void Bind(object game, object world, object body, bool authoritative)
    {
        if (!Enabled || !authoritative || body == null || !ReferenceEquals(Game, game) || !ReferenceEquals(World, world))
            throw new InvalidOperationException("ImpactCue cannot bind an unconfirmed body");
        Validate(body, world);
        if (!ReferenceEquals(Body, body)) _bodies.GetValue(Body, _ => new Lease()).Retired = true;
        Track(body, world); Body = body; Frozen = false;
    }
    internal bool Allows(object body, object game, object world, object main, object local, bool authoritative)
    {
        if (!OwnsBody(body)) return true;
        return Enabled && !Frozen && authoritative && !_bodies.GetValue(body, _ => new Lease()).Retired
            && ReferenceEquals(Game, game) && ReferenceEquals(World, world) && ReferenceEquals(Body, body)
            && ReferenceEquals(body, main) && ReferenceEquals(body, local);
    }
    internal void RetireWorld(object world)
    {
        if (_pending != null && ReferenceEquals(_pending.World, world)) _pending = null;
        if (!Enabled || !ReferenceEquals(World, world)) return;
        _bodies.GetValue(Body, _ => new Lease()).Retired = true;
        Game = null; World = null; Body = null; Frozen = false;
    }
    private void Validate(object body, object world)
    {
        if (_bodies.TryGetValue(body, out var lease) && !ReferenceEquals(lease.World, world))
            throw new InvalidOperationException("ImpactCue body was reused across worlds");
    }
    private void Track(object body, object world)
    {
        var lease = _bodies.GetValue(body, _ => new Lease());
        lease.World = world; lease.Retired = false;
    }
}

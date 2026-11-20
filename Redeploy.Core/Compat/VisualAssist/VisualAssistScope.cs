using System;
using System.Runtime.CompilerServices;

namespace Redeploy.Core.Compat;

/// <summary>Preparation is read-only; the exact native unregister commits physical ownership.</summary>
internal sealed class VisualAssistScope
{
    private sealed class Lease { internal object World; }
    private readonly ConditionalWeakTable<object, Lease> _bodies = new();
    private object _preparedGame, _preparedWorld;
    internal object PreparedBody { get; private set; }
    internal object Game { get; private set; }
    internal object World { get; private set; }
    internal object Body { get; private set; }
    internal bool Frozen { get; private set; }
    internal bool Enabled => Body != null;
    internal bool Owns(object body) => body != null && _bodies.TryGetValue(body, out _);
    internal void Prepare(object game, object world, object body)
    {
        if (PreparedBody != null || game == null || world == null || body == null
            || (Enabled && (!ReferenceEquals(Game, game) || !ReferenceEquals(World, world) || !ReferenceEquals(Body, body))))
            throw new InvalidOperationException("VisualAssist has conflicting physical ownership");
        Validate(body, world);
        _preparedGame = game; _preparedWorld = world; PreparedBody = body;
    }
    internal void Abort() { _preparedGame = null; _preparedWorld = null; PreparedBody = null; }
    internal bool Start(object game, object world, object body)
    {
        if (PreparedBody == null || !ReferenceEquals(PreparedBody, body)
            || !ReferenceEquals(_preparedGame, game) || !ReferenceEquals(_preparedWorld, world)) return false;
        Track(body, world); Game = game; World = world; Body = body; Frozen = true; Abort(); return true;
    }
    internal void Bind(object game, object world, object body, bool authoritative)
    {
        if (!Enabled || !authoritative || body == null || !ReferenceEquals(Game, game) || !ReferenceEquals(World, world))
            throw new InvalidOperationException("VisualAssist cannot bind an unconfirmed body");
        Track(body, world); Body = body; Frozen = false;
    }
    internal void Retire(object world)
    {
        if (ReferenceEquals(_preparedWorld, world)) Abort();
        if (!Enabled || !ReferenceEquals(World, world)) return;
        Game = null; World = null; Body = null; Frozen = false;
    }
    private void Validate(object body, object world)
    {
        if (_bodies.TryGetValue(body, out var lease) && !ReferenceEquals(lease.World, world))
            throw new InvalidOperationException("VisualAssist body was reused across worlds");
    }
    private void Track(object body, object world)
    {
        Validate(body, world);
        _bodies.GetValue(body, _ => new Lease()).World = world;
    }
}

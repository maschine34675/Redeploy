using System;
using System.Runtime.CompilerServices;

namespace Redeploy.Core.Compat;

/// <summary>Reference-only authority. Seeing a HUD or preparing a retry is not ownership.</summary>
internal sealed class SimpleCrosshairScope
{
    private sealed class Lease { internal object World; internal bool Retired; }
    private sealed class Preparation { internal object Game, World, Body, Panel; }
    private readonly ConditionalWeakTable<object, Lease> _bodies = new();
    private readonly ConditionalWeakTable<object, Lease> _panels = new();
    private Preparation _pending;
    internal object Game { get; private set; }
    internal object World { get; private set; }
    internal object Body { get; private set; }
    internal object Panel { get; private set; }
    internal bool Frozen { get; private set; }
    internal bool Enabled => Body != null;
    internal object PreparedBody => _pending?.Body;
    internal bool OwnsBody(object body) => body != null && _bodies.TryGetValue(body, out _);
    internal bool OwnsPanel(object panel) => panel != null && _panels.TryGetValue(panel, out _);

    internal void Prepare(object game, object world, object body, object panel)
    {
        if (_pending != null || game == null || world == null || body == null
            || (Enabled && (!ReferenceEquals(Game, game) || !ReferenceEquals(World, world) || !ReferenceEquals(Body, body))))
            throw new InvalidOperationException("Crosshair handover has conflicting physical ownership");
        _pending = new Preparation { Game = game, World = world, Body = body, Panel = panel };
    }
    internal void AbortPreparation() => _pending = null;
    // CompletePreparation validates only. Authority starts at the actual unregister or
    // replacement construction, after the engine has passed its cancellation boundary.
    internal void CompletePreparation(object panel)
    {
        if (_pending == null || (_pending.Panel != null && !ReferenceEquals(_pending.Panel, panel)))
            throw new InvalidOperationException("Crosshair changed during preparation");
        Validate(_bodies, _pending.Body, _pending.World);
        Validate(_panels, panel, _pending.World);
    }
    internal bool Start(object body)
    {
        var pending = _pending;
        if (pending == null || !ReferenceEquals(pending.Body, body)) return false;
        Validate(_bodies, pending.Body, pending.World); Validate(_panels, pending.Panel, pending.World);
        Game = pending.Game; World = pending.World; Body = pending.Body; Panel = pending.Panel;
        Track(_bodies, Body, World); Track(_panels, Panel, World);
        Frozen = true; _pending = null;
        return true;
    }
    internal bool Freeze(object body)
    {
        if (!Enabled || !ReferenceEquals(Body, body)) return false;
        Frozen = true;
        return true;
    }
    internal void AdoptPanel(object panel)
    {
        if (!Enabled || panel == null) throw new InvalidOperationException("Crosshair panel has no owned body");
        if (OwnsPanel(panel) && !ReferenceEquals(_panels.GetValue(panel, _ => new Lease()).World, World))
            throw new InvalidOperationException("Crosshair panel belongs to another world");
        if (Panel != null && !ReferenceEquals(Panel, panel)) _panels.GetValue(Panel, _ => new Lease()).Retired = true;
        Panel = panel; Track(_panels, panel, World);
    }
    internal void Bind(object game, object world, object body, object panel, bool authoritative)
    {
        if (!Enabled || !authoritative || body == null || panel == null
            || !ReferenceEquals(Game, game) || !ReferenceEquals(World, world))
            throw new InvalidOperationException("Crosshair cannot bind an unconfirmed replacement");
        Validate(_bodies, body, world); Validate(_panels, panel, world);
        if (Body != null && !ReferenceEquals(Body, body)) _bodies.GetValue(Body, _ => new Lease()).Retired = true;
        Track(_bodies, body, world); Body = body; AdoptPanel(panel); Frozen = false;
    }
    internal bool AllowsBody(object body, object game, object world, object main, object local, bool authoritative)
    {
        if (!OwnsBody(body)) return true;
        return Enabled && !Frozen && authoritative && !_bodies.GetValue(body, _ => new Lease()).Retired
            && ReferenceEquals(Game, game) && ReferenceEquals(World, world) && ReferenceEquals(Body, body)
            && ReferenceEquals(body, main) && ReferenceEquals(body, local);
    }
    internal bool AllowsPanel(object panel, object game, object world, object main, object local, bool authoritative)
        => !OwnsPanel(panel) || (ReferenceEquals(Panel, panel) && !_panels.GetValue(panel, _ => new Lease()).Retired
            && AllowsBody(Body, game, world, main, local, authoritative));
    internal bool RetireWorld(object world)
    {
        if (_pending != null && ReferenceEquals(_pending.World, world)) _pending = null;
        if (!Enabled || !ReferenceEquals(World, world)) return false;
        _bodies.GetValue(Body, _ => new Lease()).Retired = true;
        if (Panel != null) _panels.GetValue(Panel, _ => new Lease()).Retired = true;
        Game = null; World = null; Body = null; Panel = null; Frozen = false;
        return true;
    }
    private static void Track(ConditionalWeakTable<object, Lease> entries, object instance, object world)
    {
        if (instance == null) return;
        var lease = entries.GetValue(instance, _ => new Lease());
        if (lease.World != null && !ReferenceEquals(lease.World, world))
            throw new InvalidOperationException("Crosshair object reused across worlds");
        lease.World = world; lease.Retired = false;
    }
    private static void Validate(ConditionalWeakTable<object, Lease> entries, object instance, object world)
    {
        if (instance != null && entries.TryGetValue(instance, out var lease) && !ReferenceEquals(lease.World, world))
            throw new InvalidOperationException("Crosshair object belongs to another world");
    }
}

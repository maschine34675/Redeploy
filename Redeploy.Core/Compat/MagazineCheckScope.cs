using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Redeploy.Core.Compat;

/// <summary>Physical panel ownership; observing ordinary UI is not authority to change it.</summary>
internal sealed class MagazineCheckScope
{
    internal sealed class Panel
    {
        internal object Instance, Body, Game, World;
        internal bool Owned;
        internal volatile bool Retired;
        internal long Generation;
    }
    internal sealed class Ticket
    {
        internal WeakReference<Panel> Owner;
        internal long Generation;
        internal Panel Current()
        {
            return Owner != null && Owner.TryGetTarget(out var panel) && panel.Owned && !panel.Retired
                && Interlocked.Read(ref panel.Generation) == Generation ? panel : null;
        }
    }
    private sealed class Seen { }
    private sealed class Preparation { internal object Game, World, Body, Panel; }
    private readonly ConditionalWeakTable<object, Panel> _panels = new();
    private readonly ConditionalWeakTable<object, Seen> _bodies = new();
    private readonly List<Panel> _activePanels = new();
    private Preparation _prepared;
    internal object Game { get; private set; }
    internal object World { get; private set; }
    internal object Body { get; private set; }
    internal bool Frozen { get; private set; }
    internal object PreparedBody => _prepared?.Body;
    internal bool Enabled => Body != null;
    internal Panel[] ActivePanels => _activePanels.ToArray();
    internal Panel Find(object panel) => panel != null && _panels.TryGetValue(panel, out var entry) ? entry : null;
    internal bool OwnsBody(object body) => body != null && _bodies.TryGetValue(body, out _);

    internal void Prepare(object game, object world, object body, object panel)
    {
        if (_prepared != null || game == null || world == null || body == null
            || (Enabled && (!ReferenceEquals(Game, game) || !ReferenceEquals(World, world) || !ReferenceEquals(Body, body))))
            throw new InvalidOperationException("Magazine check handover has conflicting physical ownership");
        _prepared = new Preparation { Game = game, World = world, Body = body, Panel = panel };
    }
    internal void AbortPreparation() => _prepared = null;
    internal void CompletePreparation(object panel)
    {
        var pending = _prepared;
        if (pending == null) return;
        if (pending.Panel != null && !ReferenceEquals(pending.Panel, panel))
            throw new InvalidOperationException("Magazine check panel changed during preparation");
        // Resolve immutable physical ownership before granting body authority.
        // A rejected adoption must remain as cancellable as Prepare itself.
        var entry = panel == null ? null : Observe(panel, pending.Body, pending.Game, pending.World);
        if (entry?.Retired == true)
            throw new InvalidOperationException("A retired magazine check panel cannot be reused");
        Game = pending.Game; World = pending.World; Body = pending.Body;
        RememberBody(Body);
        if (entry != null)
        {
            entry.Owned = true;
            if (!_activePanels.Contains(entry)) _activePanels.Add(entry);
        }
        Frozen = true;
        _prepared = null;
    }
    internal Panel Observe(object panel, object body, object game, object world)
    {
        if (panel == null || body == null) return null;
        var found = Find(panel);
        if (found != null)
        {
            if (!ReferenceEquals(found.Body, body) || !ReferenceEquals(found.Game, game) || !ReferenceEquals(found.World, world))
                throw new InvalidOperationException("Magazine check panel changed its physical owner");
            return found;
        }
        var entry = new Panel { Instance = panel, Body = body, Game = game, World = world };
        _panels.Add(panel, entry);
        return entry;
    }
    internal Panel Adopt(object panel, object body, object game, object world)
    {
        if (!Enabled || !ReferenceEquals(body, Body) || !ReferenceEquals(game, Game) || !ReferenceEquals(world, World))
            throw new InvalidOperationException("Magazine check panel adoption needs the exact active body");
        var entry = Observe(panel, body, game, world);
        if (entry.Retired) throw new InvalidOperationException("A retired magazine check panel cannot be reused");
        entry.Owned = true;
        if (!_activePanels.Contains(entry)) _activePanels.Add(entry);
        return entry;
    }
    internal bool Allows(Panel panel, object game, object world, object main, object local, bool exactOwnedAlive)
        => panel?.Owned != true || (!panel.Retired && !Frozen && exactOwnedAlive
            && ReferenceEquals(Game, game) && ReferenceEquals(World, world)
            && ReferenceEquals(Body, panel.Body) && ReferenceEquals(panel.Game, game)
            && ReferenceEquals(panel.World, world) && ReferenceEquals(panel.Body, main) && ReferenceEquals(panel.Body, local));
    internal bool AllowsBody(object body, object game, object world, object main, object local, bool exactOwnedAlive)
        => !OwnsBody(body) || (!Frozen && exactOwnedAlive && ReferenceEquals(Body, body)
            && ReferenceEquals(Game, game) && ReferenceEquals(World, world)
            && ReferenceEquals(body, main) && ReferenceEquals(body, local));
    internal void Shown(Panel panel)
    {
        if (panel?.Owned == true && !panel.Retired) Interlocked.Increment(ref panel.Generation);
    }
    internal Ticket BeginHide(Panel panel)
    {
        if (panel?.Owned != true || panel.Retired) return null;
        return new Ticket { Owner = new WeakReference<Panel>(panel), Generation = Interlocked.Increment(ref panel.Generation) };
    }
    internal bool Retire(Panel panel)
    {
        if (panel?.Owned != true || panel.Retired) return false;
        panel.Retired = true;
        Interlocked.Increment(ref panel.Generation);
        _activePanels.Remove(panel);
        return true;
    }
    internal Panel[] Bind(object body, object game, object world, bool exactOwnedAlive)
    {
        if (!Enabled || body == null || !exactOwnedAlive || !ReferenceEquals(game, Game) || !ReferenceEquals(world, World))
            throw new InvalidOperationException("Magazine check body commit is not authorized");
        if (!Frozen && ReferenceEquals(Body, body)) return Array.Empty<Panel>();
        var retired = ActivePanels;
        foreach (var panel in retired) Retire(panel);
        Body = body; Frozen = false;
        RememberBody(body);
        return retired;
    }
    internal Panel[] RetireWorld(object world)
    {
        if (_prepared != null && ReferenceEquals(_prepared.World, world)) _prepared = null;
        var retired = _activePanels.Where(p => ReferenceEquals(p.World, world)).ToArray();
        foreach (var panel in retired) Retire(panel);
        if (world != null && ReferenceEquals(World, world)) { Game = World = Body = null; Frozen = false; }
        return retired;
    }
    private void RememberBody(object body)
    {
        if (!_bodies.TryGetValue(body, out _)) _bodies.Add(body, new Seen());
    }
}

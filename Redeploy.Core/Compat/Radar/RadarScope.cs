using System;
using System.Runtime.CompilerServices;

namespace Redeploy.Core.Compat;

/// <summary>A world/HUD lease exists only after a Core-owned physical replacement.</summary>
internal sealed class RadarScope
{
    private sealed class Owner { internal object World; internal bool Retired; }
    private sealed class Snapshot { internal object Game, World, Body, Hud, Manager; internal bool Frozen; }
    private readonly ConditionalWeakTable<object, Owner> _managers = new();
    private readonly ConditionalWeakTable<object, Owner> _huds = new();
    private Snapshot _before;
    internal object Game { get; private set; }
    internal object World { get; private set; }
    internal object Body { get; private set; }
    internal object Hud { get; private set; }
    internal object Manager { get; private set; }
    internal bool Frozen { get; private set; }
    internal bool Enabled => World != null;
    internal bool OwnsManager(object manager) => manager != null && _managers.TryGetValue(manager, out _);
    internal bool ActiveManager(object manager) => manager != null && ReferenceEquals(manager, Manager)
        && _managers.TryGetValue(manager, out var owner) && !owner.Retired && ReferenceEquals(owner.World, World);
    internal bool OwnsHud(object hud) => hud != null && _huds.TryGetValue(hud, out _);
    internal bool ActiveHud(object hud) => hud != null && ReferenceEquals(hud, Hud)
        && _huds.TryGetValue(hud, out var owner) && !owner.Retired && ReferenceEquals(owner.World, World);

    internal void Prepare(object game, object world, object body, bool authorized)
    {
        if (!authorized || game == null || world == null || body == null || _before != null
            || (Enabled && (!ReferenceEquals(Game, game) || !ReferenceEquals(World, world) || !ReferenceEquals(Body, body))))
            throw new InvalidOperationException("Radar preparation lost its physical owner");
        _before = new Snapshot { Game = Game, World = World, Body = Body, Hud = Hud, Manager = Manager, Frozen = Frozen };
        Game = game; World = world; Body = body; Frozen = true;
    }
    internal void Adopt(object hud, object manager, object game, object world, object body, bool authorized)
    {
        if (!authorized || !Enabled || hud == null || manager == null || !ReferenceEquals(Game, game)
            || !ReferenceEquals(World, world) || !ReferenceEquals(Body, body)
            || (Hud != null && !ReferenceEquals(Hud, hud)) || (Manager != null && !ReferenceEquals(Manager, manager)))
            throw new InvalidOperationException("Radar adoption does not belong to this replacement");
        if (_managers.TryGetValue(manager, out var owner))
        {
            if (owner.Retired || !ReferenceEquals(owner.World, world))
                throw new InvalidOperationException("A retired Radar manager cannot be adopted");
        }
        if (_huds.TryGetValue(hud, out var hudOwner))
        {
            if (hudOwner.Retired || !ReferenceEquals(hudOwner.World, world))
                throw new InvalidOperationException("A retired Radar HUD cannot be adopted");
        }
        if (owner == null) _managers.Add(manager, new Owner { World = world });
        if (hudOwner == null) _huds.Add(hud, new Owner { World = world });
        Hud = hud; Manager = manager;
    }
    internal void Freeze(object game, object world, object body, bool authorized)
    {
        if (!authorized || !Enabled || body == null || !ReferenceEquals(Game, game) || !ReferenceEquals(World, world))
            throw new InvalidOperationException("Radar camera change is not owned by Core");
        Frozen = true;
    }
    internal void Bind(object game, object world, object body, bool authorized)
    {
        if (!authorized || !Enabled || body == null || !ReferenceEquals(Game, game) || !ReferenceEquals(World, world))
            throw new InvalidOperationException("Radar body commit is not owned by Core");
        Body = body; Frozen = false; _before = null;
    }
    internal void AbortPreparation()
    {
        if (_before == null) return;
        if (Manager != null && !ReferenceEquals(Manager, _before.Manager)) _managers.Remove(Manager);
        if (Hud != null && !ReferenceEquals(Hud, _before.Hud)) _huds.Remove(Hud);
        Game = _before.Game; World = _before.World; Body = _before.Body;
        Hud = _before.Hud; Manager = _before.Manager; Frozen = _before.Frozen;
        _before = null;
    }
    internal void RetireWorld(object world)
    {
        if (world == null || !ReferenceEquals(world, World)) return;
        if (Manager != null && _managers.TryGetValue(Manager, out var owner)) owner.Retired = true;
        if (Hud != null && _huds.TryGetValue(Hud, out var hudOwner)) hudOwner.Retired = true;
        Game = World = Body = Hud = Manager = null; Frozen = false; _before = null;
    }
    internal static bool ReplaceContact(object trackedBody, object currentBody)
        => currentBody != null && !ReferenceEquals(trackedBody, currentBody);
}

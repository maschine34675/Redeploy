using System;
using System.Runtime.CompilerServices;

namespace Redeploy.Core.Compat;

/// <summary>World-local marker authority begins at Core's physical unregister boundary.</summary>
internal sealed class EnemyMarkersScope
{
    private sealed class Lease { internal bool Retired; }
    private sealed class Preparation { internal object Game, World, Body; }
    private readonly ConditionalWeakTable<object, Lease> _components = new();
    private readonly ConditionalWeakTable<object, Lease> _billboards = new();
    private Lease _lease;
    private Preparation _pending;
    internal object Game { get; private set; }
    internal object World { get; private set; }
    internal bool Enabled => _lease != null;
    internal bool Prepared => _pending != null;
    internal bool OwnsComponent(object component) => component != null && _components.TryGetValue(component, out _);
    internal bool OwnsBillboard(object billboard) => billboard != null && _billboards.TryGetValue(billboard, out _);
    internal bool ActiveComponent(object component) => Active(_components, component);
    internal bool ActiveBillboard(object billboard) => Active(_billboards, billboard);
    private bool Active(ConditionalWeakTable<object, Lease> table, object instance)
        => instance != null && table.TryGetValue(instance, out var lease) && !lease.Retired && ReferenceEquals(lease, _lease);

    internal void Prepare(object game, object world, object body, bool authorized)
    {
        if (!authorized || game == null || world == null || body == null || _pending != null
            || (Enabled && (!ReferenceEquals(Game, game) || !ReferenceEquals(World, world))))
            throw new InvalidOperationException("Enemy Markers preparation lost its Core world/body");
        _pending = new Preparation { Game = game, World = world, Body = body };
    }
    internal bool Start(object game, object world, object body, bool authorized)
    {
        if (!authorized || _pending == null || !ReferenceEquals(_pending.Game, game)
            || !ReferenceEquals(_pending.World, world) || !ReferenceEquals(_pending.Body, body)) return false;
        Game = game; World = world; _lease ??= new Lease(); _pending = null;
        return true;
    }
    internal void AbortPreparation() => _pending = null;
    internal void AdoptComponent(object component, object game, object world, bool authorized)
    {
        if (!authorized || !Enabled || component == null || !ReferenceEquals(game, Game) || !ReferenceEquals(world, World))
            throw new InvalidOperationException("Enemy Markers component is not in the owned world");
        Track(_components, component);
    }
    internal void AdoptBillboard(object component, object billboard)
    {
        if (!ActiveComponent(component) || billboard == null)
            throw new InvalidOperationException("Enemy Markers billboard has no owned component");
        Track(_billboards, billboard);
    }
    private void Track(ConditionalWeakTable<object, Lease> table, object instance)
    {
        if (table.TryGetValue(instance, out var lease))
        {
            if (lease.Retired || !ReferenceEquals(lease, _lease))
                throw new InvalidOperationException("Enemy Markers object belongs to a retired world");
            return;
        }
        table.Add(instance, _lease);
    }
    internal bool AllowsPerson(object component, bool currentWorld, bool coreLocalBody)
        => !OwnsComponent(component) || (ActiveComponent(component) && currentWorld && !coreLocalBody);
    internal bool AllowsBillboard(object billboard, bool currentWorld, bool cameraReady)
        => !OwnsBillboard(billboard) || (ActiveBillboard(billboard) && currentWorld && cameraReady);
    internal void RetireWorld(object world)
    {
        if (_pending != null && ReferenceEquals(_pending.World, world)) _pending = null;
        if (!Enabled || !ReferenceEquals(World, world)) return;
        _lease.Retired = true; _lease = null; Game = World = null;
        // Tombstones carry no strong world/body references; old delayed events stay fenced.
    }
}

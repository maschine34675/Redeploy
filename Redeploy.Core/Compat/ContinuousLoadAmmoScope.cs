using System;
using System.Runtime.CompilerServices;

namespace Redeploy.Core.Compat;

/// <summary>Physical controller ownership and cancellation tickets; never profile/item IDs.</summary>
internal sealed class ContinuousLoadAmmoScope
{
    internal sealed class Lease
    {
        internal readonly object Controller, Body, World, Inventory, Movement;
        internal bool Disposed, HasLimit;
        internal long Revision, Operation;
        internal Lease(object controller, object body, object world, object inventory, object movement)
        { Controller = controller; Body = body; World = world; Inventory = inventory; Movement = movement; }
    }
    internal sealed class Completion
    {
        internal readonly Lease Owner;
        internal readonly long Operation;
        internal Completion(Lease owner) { Owner = owner; Operation = ++owner.Operation; }
    }
    internal sealed class LimitObservation
    {
        internal readonly object Controller, Body, World, Inventory, Movement;
        internal bool Applied, Mixed, Unknown;
        internal long Revision;
        internal Lease Lease;
        internal LimitObservation(object controller, object body, object world, object inventory, object movement)
        { Controller = controller; Body = body; World = world; Inventory = inventory; Movement = movement; }
    }
    internal sealed class LimitCall
    {
        internal readonly LimitObservation Observation;
        internal readonly long Revision;
        internal readonly bool Mixed;
        internal LimitCall(LimitObservation observation, bool present)
        { Observation = observation; Revision = ++observation.Revision; Mixed = present && (!observation.Applied || observation.Mixed); }
    }
    private ConditionalWeakTable<object, LimitObservation> _limitControllers = new(), _limitMovements = new();

    internal LimitCall ObserveLimitStart(object controller, object body, object world, object inventory, object movement, bool alreadyPresent)
    {
        if (controller == null || body == null || world == null || inventory == null || movement == null) return null;
        if (!_limitControllers.TryGetValue(controller, out var observation)
            || !ReferenceEquals(observation.Body, body) || !ReferenceEquals(observation.World, world)
            || !ReferenceEquals(observation.Inventory, inventory) || !ReferenceEquals(observation.Movement, movement))
        {
            observation = new LimitObservation(controller, body, world, inventory, movement);
            _limitControllers.Remove(controller); _limitControllers.Add(controller, observation);
            _limitMovements.Remove(movement); _limitMovements.Add(movement, observation);
        }
        return new LimitCall(observation, alreadyPresent);
    }
    internal void FinishLimitStart(LimitCall call, bool synchronousSuccess, bool stillPresent)
    {
        if (call == null) return;
        var o = call.Observation;
        if (!_limitControllers.TryGetValue(o.Controller, out var current) || !ReferenceEquals(current, o)
            || o.Revision != call.Revision || o.Lease?.Disposed == true) return;
        // Only a completed actual upstream true branch proves a newly applied limit.
        // An asynchronous or faulted future implementation is conservatively unknown.
        o.Applied = synchronousSuccess && stillPresent;
        o.Mixed = o.Applied && call.Mixed;
        o.Unknown = !synchronousSuccess;
    }
    internal void ObserveLimitRemoved(object movement)
    {
        if (movement == null || !_limitMovements.TryGetValue(movement, out var o)) return;
        ++o.Revision; o.Applied = o.Mixed = o.Unknown = false;
        if (o.Lease != null) o.Lease.HasLimit = false;
    }
    internal bool HasUncertainLimit(object controller)
        => controller != null && _limitControllers.TryGetValue(controller, out var o) && (o.Mixed || o.Unknown);

    private ConditionalWeakTable<object, Lease> _controllers = new(), _inventories = new();
    private ConditionalWeakTable<object, Completion> _tasks = new();

    internal Lease BindController(object controller, object body, object bodyWorld, object inventory,
        object currentWorld, object mainPlayer, bool exactRaid, bool local, object movement = null)
    {
        if (controller == null || body == null || inventory == null || bodyWorld == null || !exactRaid || !local
            || !ReferenceEquals(bodyWorld, currentWorld) || !ReferenceEquals(body, mainPlayer)) return null;
        if (_controllers.TryGetValue(controller, out var existing))
            return !existing.Disposed && ReferenceEquals(existing.Body, body) && ReferenceEquals(existing.World, bodyWorld)
                && ReferenceEquals(existing.Inventory, inventory) && ReferenceEquals(existing.Movement, movement) ? existing : null;
        var lease = new Lease(controller, body, bodyWorld, inventory, movement);
        if (_limitControllers.TryGetValue(controller, out var observed)
            && ReferenceEquals(observed.Body, body) && ReferenceEquals(observed.World, bodyWorld)
            && ReferenceEquals(observed.Inventory, inventory) && ReferenceEquals(observed.Movement, movement))
        {
            lease.HasLimit = observed.Applied && !observed.Mixed && !observed.Unknown;
            observed.Lease = lease;
        }
        _controllers.Add(controller, lease);
        _inventories.Remove(inventory); _inventories.Add(inventory, lease);
        return lease;
    }
    internal Lease Find(object controller) => controller != null && _controllers.TryGetValue(controller, out var lease) ? lease : null;
    internal Lease ForInventory(object inventory) => inventory != null && _inventories.TryGetValue(inventory, out var lease) ? lease : null;
    internal Completion CaptureTask(object task, Lease lease)
    {
        if (task == null || lease == null) return null;
        var completion = new Completion(lease);
        _tasks.Remove(task); _tasks.Add(task, completion);
        return completion;
    }
    internal Completion ForTask(object task) => task != null && _tasks.TryGetValue(task, out var completion) ? completion : null;
    internal static bool IsLatest(Completion completion)
        => completion != null && !completion.Owner.Disposed && completion.Operation == completion.Owner.Operation;
    internal static bool IsCurrent(Lease lease, object controller, object world, object mainPlayer, object inventory, bool alive, bool exactRaid, object movement = null)
        => lease != null && !lease.Disposed && alive && exactRaid
            && ReferenceEquals(lease.Controller, controller) && ReferenceEquals(lease.World, world)
            && ReferenceEquals(lease.Body, mainPlayer) && ReferenceEquals(lease.Inventory, inventory)
            && ReferenceEquals(lease.Movement, movement);
    internal static bool Retire(Lease lease)
    {
        if (lease == null || lease.Disposed) return false;
        lease.Disposed = true; ++lease.Revision; ++lease.Operation; return true;
    }
    internal static bool MayResume(Lease lease, long ticket, bool current, bool active, bool multiSelect)
        => lease != null && !lease.Disposed && lease.Revision == ticket && current && !active && !multiSelect;
    internal static bool MayClearStatic(object disposing, object current, bool disposingOwned, bool currentOwned)
        => !(disposingOwned || currentOwned) || disposing != null && ReferenceEquals(disposing, current);
    internal void Clear()
    { _controllers = new(); _inventories = new(); _tasks = new(); _limitControllers = new(); _limitMovements = new(); }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Redeploy.Core.Compat;

/// <summary>Weak physical-body ownership; observing ordinary animations grants no cleanup authority.</summary>
internal sealed class ManimalAmmoScope
{
    internal sealed class Lease
    {
        internal object Body, World, Inventory, Session;
        internal bool Owned, Retired;
        internal long Epoch;
    }
    internal sealed class Ticket
    {
        internal Lease Owner;
        internal object Session;
        internal long Epoch;
    }
    private sealed class Preparation
    {
        internal object Body, World, Inventory, Session;
    }

    private ConditionalWeakTable<object, Lease> _bodies = new();
    private ConditionalWeakTable<object, Lease> _inventories = new();
    private ConditionalWeakTable<object, Ticket> _callbacks = new();
    private readonly List<Lease> _active = new();
    private Preparation _prepared;
    internal IEnumerable<Lease> Active => _active.ToArray();
    internal object PreparedBody => _prepared?.Body;
    internal Lease Find(object body) => body != null && _bodies.TryGetValue(body, out var lease) ? lease : null;
    internal Lease ForInventory(object inventory) => inventory != null && _inventories.TryGetValue(inventory, out var lease) ? lease : null;
    internal Ticket ForCallback(object callback) => callback != null && _callbacks.TryGetValue(callback, out var ticket) ? ticket : null;

    private Lease Observe(object body)
    {
        if (body == null) throw new ArgumentNullException(nameof(body));
        return _bodies.GetValue(body, key => new Lease { Body = key });
    }

    internal void Prepare(object body, object world, object inventory, object session)
    {
        if (body == null || world == null || inventory == null || Find(body)?.Retired == true)
            throw new InvalidOperationException("Manimal old-body identity is incomplete or retired");
        _prepared = new Preparation { Body = body, World = world, Inventory = inventory, Session = session };
    }
    internal void AbortPreparation() => _prepared = null;

    internal Lease CommitCandidate(object candidate, object world, object inventory, object oldSession, bool ownedCandidate)
    {
        var pending = _prepared;
        if (!ownedCandidate || pending == null || candidate == null || inventory == null
            || ReferenceEquals(candidate, pending.Body) || !ReferenceEquals(world, pending.World)
            || (pending.Session != null && !ReferenceEquals(oldSession, pending.Session)))
            throw new InvalidOperationException("Manimal replacement does not match its prepared physical body/session");
        var candidateLease = Find(candidate);
        var candidateInventory = ForInventory(inventory);
        if (candidateLease?.Retired == true || (candidateLease?.Owned == true
            && (!ReferenceEquals(candidateLease.World, world) || !ReferenceEquals(candidateLease.Inventory, inventory)))
            || (candidateInventory != null && !ReferenceEquals(candidateInventory, candidateLease)))
            throw new InvalidOperationException("Manimal candidate has conflicting existing ownership");
        // A live kit replacement can wait for assets while an initial ordinary
        // body starts its first magazine animation. The exact same body/key may
        // gain a session after Prepare; replacing a captured session is different.
        var old = Adopt(pending.Body, pending.World, pending.Inventory, oldSession);
        Adopt(candidate, world, inventory, null);
        _prepared = null;
        Retire(old);
        return old;
    }

    private Lease Adopt(object body, object world, object inventory, object session)
    {
        var lease = Observe(body);
        if (lease.Retired) throw new InvalidOperationException("Manimal retired body needs explicit recovery");
        if (lease.Owned && (!ReferenceEquals(lease.World, world) || !ReferenceEquals(lease.Inventory, inventory)))
            throw new InvalidOperationException("Manimal physical body changed world/inventory ownership");
        lease.Owned = true; lease.World = world; lease.Inventory = inventory;
        if (session != null) CaptureSession(lease, session);
        if (!_active.Contains(lease)) _active.Add(lease);
        var previous = ForInventory(inventory);
        if (previous != null && !ReferenceEquals(previous, lease))
            throw new InvalidOperationException("Manimal inventory belongs to another physical body");
        if (previous == null) _inventories.Add(inventory, lease);
        return lease;
    }

    internal void CaptureSession(Lease lease, object session)
    {
        if (lease == null || !lease.Owned || lease.Retired || session == null) return;
        if (lease.Session != null && !ReferenceEquals(lease.Session, session))
            throw new InvalidOperationException("Manimal session changed on an owned physical body");
        lease.Session = session;
    }

    internal Ticket Stamp(object callback, object body, object session = null)
    {
        if (callback == null || body == null) return null;
        var owner = Observe(body);
        if (_callbacks.TryGetValue(callback, out var existing))
        {
            if (!ReferenceEquals(existing.Owner, owner)) throw new InvalidOperationException("Manimal callback changed physical owner");
            return existing;
        }
        var ticket = new Ticket { Owner = owner, Epoch = owner.Epoch, Session = session };
        _callbacks.Add(callback, ticket);
        return ticket;
    }

    internal bool Retire(Lease lease)
    {
        if (lease == null || !lease.Owned || lease.Retired) return false;
        lease.Retired = true; ++lease.Epoch; _active.Remove(lease);
        return true;
    }
    internal Lease[] RetireWorld(object world)
    {
        var owned = _active.Where(l => ReferenceEquals(l.World, world)).ToArray();
        foreach (var lease in owned) Retire(lease);
        return owned;
    }
    internal void ForgetSession(Lease lease) { if (lease != null && lease.Retired) lease.Session = null; }

    // A failed live swap may restore the previous body. Start a fresh generation;
    // old iterator/drop/task tickets stay invalid even though the physical body is current again.
    internal Lease Recover(object body, object world, object inventory, bool exactOwnedCurrent)
    {
        var lease = Find(body);
        if (!exactOwnedCurrent || lease == null || !lease.Owned || !lease.Retired
            || !ReferenceEquals(lease.World, world) || !ReferenceEquals(lease.Inventory, inventory)) return null;
        lease.Retired = false; ++lease.Epoch; lease.Session = null;
        _active.Add(lease);
        return lease;
    }

    internal static bool MayRun(Lease lease, object world, object main, bool exactRaid, bool alive)
        => lease == null || !lease.Owned || (!lease.Retired && exactRaid && alive
            && ReferenceEquals(lease.World, world) && ReferenceEquals(lease.Body, main));
    internal static bool MayResume(Ticket ticket, object world, object main, bool exactRaid, bool alive)
        => ticket != null && ticket.Epoch == ticket.Owner.Epoch
            && MayRun(ticket.Owner, world, main, exactRaid, alive)
            && (ticket.Session == null || ReferenceEquals(ticket.Session, ticket.Owner.Session));
    internal static bool MayRemoveSession(Lease lease, object dictionaryEntry)
        => lease?.Owned == true && lease.Retired && lease.Session != null
            && ReferenceEquals(lease.Session, dictionaryEntry);

    internal void Clear()
    {
        _active.Clear(); _prepared = null;
        _bodies = new(); _inventories = new(); _callbacks = new();
    }
}

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;


namespace Redeploy.Core.Compat;

/// <summary>Recoil components belong to physical players; a shared profile is not a lease.</summary>
internal sealed class RecoilLegacyScope
{
    internal sealed class Lease
    {
        internal object Body, World, Game, Recoil, Camera, LastHands;
        internal Lease Previous;
        internal bool Committed, Retired, Failed, RecoveryPending, OldRecoilEnabled, OldCameraEnabled;
        internal object[] OldCache;
    }

    private readonly List<Lease> _leases = new();
    private ConditionalWeakTable<object, Lease> _bodies = new();
    private ConditionalWeakTable<object, Lease> _hands = new();
    internal IEnumerable<Lease> Leases => _leases;
    internal Lease Find(object body)
        => body != null && _bodies.TryGetValue(body, out var lease) ? lease : null;
    internal Lease ForHands(object hands)
        => hands != null && _hands.TryGetValue(hands, out var lease) ? lease : null;
    internal void TrackHands(object hands, Lease lease)
    {
        if (hands == null || lease == null) return;
        if (_hands.TryGetValue(hands, out var old))
        {
            if (!ReferenceEquals(old, lease)) throw new InvalidOperationException("Recoil controller changed physical owner");
            return;
        }
        _hands.Add(hands, lease);
    }

    internal static bool MayCreate(bool ownedCandidate, bool creating, bool oldOwned, bool sameBody)
        => ownedCandidate && creating && oldOwned && !sameBody;

    internal Lease Add(object body, object world, object game, Lease previous)
    {
        if (body == null || world == null || game == null || Find(body) != null)
            throw new InvalidOperationException("Recoil body already captured or incomplete");
        var lease = new Lease { Body = body, World = world, Game = game, Previous = previous };
        _bodies.Add(body, lease);
        _leases.Add(lease);
        return lease;
    }

    // Preparation may be rejected by another adapter before teardown. Release
    // only the freshly adopted bookkeeping, never the foreign components/cache.
    internal void AbandonAdoption(Lease lease, object[] hands)
    {
        if (lease == null) return;
        if (ReferenceEquals(Find(lease.Body), lease)) _bodies.Remove(lease.Body);
        foreach (var hand in hands ?? Array.Empty<object>())
            if (hand != null && ReferenceEquals(ForHands(hand), lease)) _hands.Remove(hand);
        _leases.Remove(lease);
    }

    internal static bool Current(Lease lease, object world, object game, object main, object local,
        bool exactRaid, bool alive)
        => lease != null && !lease.Retired && !lease.Failed && exactRaid && alive
            && ReferenceEquals(lease.World, world) && ReferenceEquals(lease.Game, game)
            && ReferenceEquals(lease.Body, main) && ReferenceEquals(lease.Body, local);

    internal static bool OwnsComponent(Lease lease, object component)
        => lease != null && component != null
            && (ReferenceEquals(lease.Recoil, component) || ReferenceEquals(lease.Camera, component));

    internal void Prune(Predicate<Lease> removed) => _leases.RemoveAll(removed);
    internal void Clear()
    {
        _leases.Clear(); _bodies = new ConditionalWeakTable<object, Lease>();
        _hands = new ConditionalWeakTable<object, Lease>();
    }
}

using System;
using System.Runtime.CompilerServices;

namespace Redeploy.Core.Compat;

/// <summary>Authority is an exact Core operation and physical body, never a profile ID.</summary>
internal sealed class FovFixScope
{
    internal sealed class Lease
    {
        internal object Game, World, Controller, Body;
        internal bool Enabled, Frozen;
    }

    private sealed class Seen { }
    private readonly ConditionalWeakTable<object, Seen> _bodies = new();
    private readonly ConditionalWeakTable<object, Seen> _controllers = new();
    private Lease _prepared;
    internal Lease Active { get; private set; }

    internal void Prepare(object game, object world, object controller, object body)
    {
        if (_prepared != null || game == null || world == null || controller == null || body == null)
            throw new InvalidOperationException("Incomplete or overlapping FOV handover");
        if (Active != null && (!ReferenceEquals(Active.Game, game) || !ReferenceEquals(Active.World, world)
            || !ReferenceEquals(Active.Controller, controller) || !ReferenceEquals(Active.Body, body)))
            throw new InvalidOperationException("FOV handover no longer owns the current body/controller");
        // Provisional adoption neither fences callbacks nor mutates the foreign controller.
        _prepared = new Lease { Game = game, World = world, Controller = controller, Body = body };
    }

    internal void AbortPreparation()
    {
        _prepared = null;
    }

    internal void CompletePreparation()
    {
        if (_prepared == null) return;
        Active = _prepared;
        Active.Enabled = Active.Frozen = true;
        Remember(_bodies, Active.Body);
        Remember(_controllers, Active.Controller);
        _prepared = null;
    }

    internal bool OwnsBody(object body) => body != null && _bodies.TryGetValue(body, out _);
    internal bool TouchedController(object controller) => controller != null && _controllers.TryGetValue(controller, out _);
    internal bool Enabled => Active?.Enabled == true;

    internal bool TrackCandidate(object body, object game, object world, bool coreOwned)
    {
        if (!Enabled || body == null || !coreOwned || !ReferenceEquals(Active.Game, game)
            || !ReferenceEquals(Active.World, world)) return false;
        Remember(_bodies, body);
        return true;
    }

    internal bool CanBind(object body, object game, object world, object main, object local,
        bool exactRaid, bool coreOwned, bool alive, bool creating, bool published)
        => Enabled && body != null && exactRaid && coreOwned && alive && (!creating || published)
           && ReferenceEquals(Active.Game, game) && ReferenceEquals(Active.World, world)
           && ReferenceEquals(body, main) && ReferenceEquals(body, local);

    internal void Bind(object body)
    {
        if (!Enabled || !OwnsBody(body)) throw new InvalidOperationException("Unowned FOV body commit");
        Active.Body = body;
        Active.Frozen = false;
    }

    internal bool Allows(object body, object game, object world, object main, object local, bool exactRaid, bool alive)
        => Enabled && !Active.Frozen && body != null && alive && exactRaid
           && ReferenceEquals(Active.Body, body) && ReferenceEquals(Active.Game, game)
           && ReferenceEquals(Active.World, world) && ReferenceEquals(body, main) && ReferenceEquals(body, local);

    internal Lease RetireWorld(object world)
    {
        if (world == null) return null;
        if (_prepared != null && ReferenceEquals(_prepared.World, world)) _prepared = null;
        if (Active == null || !ReferenceEquals(Active.World, world)) return null;
        var retired = Active;
        Active = null;
        return retired;
    }

    private static void Remember(ConditionalWeakTable<object, Seen> table, object key)
    {
        if (!table.TryGetValue(key, out _)) table.Add(key, new Seen());
    }
}

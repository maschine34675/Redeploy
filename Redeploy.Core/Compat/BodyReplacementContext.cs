using System;
using System.Runtime.CompilerServices;
using Comfort.Common;
using EFT;

namespace Redeploy.Core.Compat;

/// <summary>Physical ownership granted only by the shared replacement engine.</summary>
internal static class BodyReplacementContext
{
    private sealed class Lease
    {
        internal readonly GameWorld World;
        internal Lease(GameWorld world) => World = world;
    }

    private static readonly ConditionalWeakTable<Player, Lease> Bodies = new();
    internal static LocalGame CurrentGame { get; private set; }
    internal static GameWorld CurrentWorld { get; private set; }
    internal static LocalPlayer PreviousBody { get; private set; }
    internal static bool IsCreating { get; private set; }
    private static LocalGame _previousGame;
    private static GameWorld _previousWorld;
    private static Lease _previousEntryLease;

    internal static void Begin(LocalGame game, LocalPlayer oldBody)
    {
        var world = game?.GameWorld;
        if (game == null || world == null || oldBody == null || world is HideoutGameWorld
            || !oldBody.IsYourPlayer || !ReferenceEquals(oldBody.GameWorld, world)
            || !ReferenceEquals(oldBody.Profile, game.Profile)
            || !ReferenceEquals(game.LocalPlayer, oldBody) || !ReferenceEquals(world.MainPlayer, oldBody)
            || !Singleton<GameWorld>.Instantiated || !ReferenceEquals(Singleton<GameWorld>.Instance, world)
            || !Singleton<AbstractGame>.Instantiated || !ReferenceEquals(Singleton<AbstractGame>.Instance, game))
            throw new InvalidOperationException("Body compatibility could not verify the current local player before replacement.");

        _previousGame = CurrentGame;
        _previousWorld = CurrentWorld;
        Bodies.TryGetValue(oldBody, out _previousEntryLease);
        CurrentGame = game;
        CurrentWorld = world;
        PreviousBody = oldBody;
        IsCreating = true;
        Track(oldBody, world);
    }

    internal static bool OwnsRaid(LocalGame game, GameWorld world)
        => game != null && world != null && ReferenceEquals(game, CurrentGame)
           && ReferenceEquals(world, CurrentWorld) && ReferenceEquals(game.GameWorld, world)
           && Singleton<GameWorld>.Instantiated && ReferenceEquals(Singleton<GameWorld>.Instance, world)
           && Singleton<AbstractGame>.Instantiated && ReferenceEquals(Singleton<AbstractGame>.Instance, game)
           && (game.Status == GameStatus.Running || game.Status == GameStatus.Starting || game.Status == GameStatus.Started);

    internal static bool OwnsBody(Player body)
        => !ReferenceEquals(body, null) && Bodies.TryGetValue(body, out var lease)
           && ReferenceEquals(body.GameWorld, lease.World);

    internal static bool TryOwnCandidate(Player body)
    {
        if (!IsCreating || body is not LocalPlayer || body == null || !body.IsYourPlayer
            || PreviousBody == null || ReferenceEquals(body, PreviousBody)
            || !OwnsRaid(CurrentGame, CurrentWorld) || !ReferenceEquals(body.GameWorld, CurrentWorld)
            || !ReferenceEquals(body.Profile, PreviousBody.Profile) || !ReferenceEquals(body.Profile, CurrentGame.Profile)
            || !ReferenceEquals(CurrentWorld.MainPlayer, body)
            || !(ReferenceEquals(CurrentGame.LocalPlayer, PreviousBody) || ReferenceEquals(CurrentGame.LocalPlayer, body)))
            return false;
        Track(body, CurrentWorld);
        return true;
    }

    private static void Track(Player body, GameWorld world)
    {
        if (Bodies.TryGetValue(body, out var old))
        {
            if (ReferenceEquals(old.World, world)) return;
            Bodies.Remove(body);
        }
        Bodies.Add(body, new Lease(world));
    }

    internal static void CancelPreparation()
    {
        if (!IsCreating) return;
        if (!ReferenceEquals(PreviousBody, null))
        {
            Bodies.Remove(PreviousBody);
            if (_previousEntryLease != null) Bodies.Add(PreviousBody, _previousEntryLease);
        }
        CurrentGame = _previousGame;
        CurrentWorld = _previousWorld;
        EndAttempt();
    }

    internal static void EndAttempt()
    {
        IsCreating = false;
        PreviousBody = null;
        _previousGame = null;
        _previousWorld = null;
        _previousEntryLease = null;
    }
    internal static void Reset()
    {
        EndAttempt();
        CurrentGame = null;
        CurrentWorld = null;
        // Weak tombstones continue fencing delayed callbacks without retaining dead bodies.
    }
}

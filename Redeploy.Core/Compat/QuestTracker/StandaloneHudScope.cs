using System;
using System.Runtime.CompilerServices;
using Comfort.Common;
using EFT;

namespace Redeploy.Core.Compat;

/// <summary>Read-only preparation, physical leases, and weak tombstones for late foreign callbacks.</summary>
internal sealed class StandaloneHudScope
{
    private readonly ConditionalWeakTable<Player, object> _retired = new();
    internal LocalGame Game { get; private set; }
    internal GameWorld World { get; private set; }
    internal LocalPlayer Body { get; private set; }
    internal LocalPlayer Prepared { get; private set; }
    internal bool Frozen { get; private set; }
    internal bool Active => !ReferenceEquals(Body, null);
    internal bool Owned => Active || !ReferenceEquals(Prepared, null);
    internal static LocalGame CurrentGame => Singleton<AbstractGame>.Instantiated ? Singleton<AbstractGame>.Instance as LocalGame : null;
    internal static GameWorld CurrentWorld => Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance : null;

    internal void Prepare(LocalGame game, LocalPlayer body)
    {
        if (Prepared != null || !BodyReplacementContext.IsCreating || !BodyReplacementContext.OwnsRaid(game, game?.GameWorld)
            || !ReferenceEquals(BodyReplacementContext.PreviousBody, body) || !BodyReplacementContext.OwnsBody(body)
            || !ReferenceEquals(game.LocalPlayer, body) || !ReferenceEquals(game.GameWorld.MainPlayer, body)
            || (Active && (!ReferenceEquals(Game, game) || !ReferenceEquals(World, game.GameWorld) || !ReferenceEquals(Body, body))))
            throw new InvalidOperationException("HUD compatibility requires Core's exact current physical body");
        Game = game; World = game.GameWorld; Prepared = body;
    }
    internal void Abort()
    {
        Prepared = null;
        if (!Active) { Game = null; World = null; }
    }
    internal bool Exact(Player body) => body != null && body.IsYourPlayer && body.HealthController?.IsAlive == true
        && BodyReplacementContext.OwnsRaid(Game, World) && BodyReplacementContext.OwnsBody(body)
        && ReferenceEquals(body.GameWorld, World) && ReferenceEquals(body, World.MainPlayer)
        && ReferenceEquals(body, Game.LocalPlayer) && ReferenceEquals(body, GamePlayerOwner.MyPlayer)
        && World.RegisteredPlayers.Contains(body);
    internal bool Retired(Player body) => !ReferenceEquals(body, null) && _retired.TryGetValue(body, out _);
    internal bool Preparing => Prepared != null && BodyReplacementContext.IsCreating
        && ReferenceEquals(Prepared, BodyReplacementContext.PreviousBody) && BodyReplacementContext.OwnsRaid(Game, World);
    internal bool SuppressUnregister(Player body)
    {
        if (Retired(body)) return true;
        if (!Preparing || !ReferenceEquals(Prepared, body)) return false;
        Body = Prepared; Frozen = true; return true;
    }
    internal bool CanBind(Player body, bool published)
        => Owned && body is LocalPlayer && (!BodyReplacementContext.IsCreating || published)
            && Exact(body) && !ReferenceEquals(body, Prepared ?? Body) && !Retired(body);
    internal void Bind(LocalPlayer body)
    {
        var old = Prepared ?? Body;
        if (!ReferenceEquals(old, null) && !ReferenceEquals(old, body)) _retired.GetValue(old, _ => new object());
        Body = body; Prepared = null; Frozen = false;
    }
    internal void RecoverPrepared()
    {
        // LiveKitRespawn may restore the original living player after creation fails.
        // Recovery grants no new body lease and must never retire that surviving player.
        if (BodyReplacementContext.IsCreating || Prepared == null || !Exact(Prepared))
            throw new InvalidOperationException("HUD compatibility cannot recover an unconfirmed original body");
        Body = Prepared; Prepared = null; Frozen = false;
    }
    internal bool BlockCallbacks => Owned && (Preparing || Frozen || !Exact(Body));
    internal void Retire(GameWorld world)
    {
        if (!ReferenceEquals(World, world)) return;
        // The final current body's unregister must still run the mod's normal raid cleanup.
        // Only bodies actually replaced by Bind remain tombstoned.
        Game = null; World = null; Body = null; Prepared = null; Frozen = false;
    }
}

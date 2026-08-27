using EFT;
using EFT.Game.Spawning;
using Redeploy.Core.GameAccess;
using System;

namespace Redeploy.Core.Spawning;

/// <summary>
/// Builds and caches the game's spawn system for the current raid. Keyed on the
/// LocalGame instance, so a new raid automatically invalidates the cache.
/// </summary>
public static class SpawnSystemCache
{
    private static LocalGame _cachedFor;
    private static ISpawnSystem _cached;

    /// <summary>Pre-build the spawn system (e.g. at death time) so later resolution is instant.</summary>
    public static void Warm(LocalGame game) => GetOrCreate(game);

    public static ISpawnSystem GetOrCreate(LocalGame game)
    {
        if (game == null)
            return null;

        if (_cached != null && ReferenceEquals(_cachedFor, game))
            return _cached;

        _cached = null;
        _cachedFor = null;

        try
        {
            var location = game.Location;
            if (location == null)
                return null;

            var spawnPointManager = SpawnPointsCollection.CreateFromScene(
                new DateTime?(DateTimeExtensions.LocalDateTimeFromUnixTime(location.UnixDateTime)),
                location.SpawnPointParams);

            var spawnSettings = SpawnSystemSettings.CreateDefault();

            _cached = SpawnSystemFactory.CreateSpawnSystem(
                spawnSettings,
                () => 1f,
                game.GameWorld,
                LocalGameAccessor.GetBotsController(game),
                spawnPointManager);
            _cachedFor = game;
        }
        catch (Exception ex)
        {
            CoreLog.LogError($"Failed to cache spawn system: {ex}");
        }

        return _cached;
    }

    public static void Clear()
    {
        _cached = null;
        _cachedFor = null;
    }
}

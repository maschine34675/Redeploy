using CorpseRun.Config;
using EFT;
using EFT.Game.Spawning;
using System;
using UnityEngine;
using UnityEngine.AI;

namespace CorpseRun.Services;

internal static class SpawnPointHelper
{
    public static void CacheSpawnSystem(LocalGame game)
    {
        if (RespawnSessionState.CachedSpawnSystem != null)
            return;

        try
        {
            var location = game.Location_0;
            if (location == null)
                return;

            var spawnPointManager = SpawnPointManagerClass.CreateFromScene(
                new DateTime?(EFTDateTimeClass.LocalDateTimeFromUnixTime(location.UnixDateTime)),
                location.SpawnPointParams);

            var spawnSettings = SpawnSettingsStruct.CreateDefault();

            RespawnSessionState.CachedSpawnSystem = SpawnSystemCreatorClass.CreateSpawnSystem(
                spawnSettings,
                () => 1f,
                game.GameWorld_0,
                LocalGameAccessor.GetBotsController(game),
                spawnPointManager);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogError($"[CorpseRun] Failed to cache spawn system: {ex}");
        }
    }

    public static (Vector3 position, Quaternion rotation) ResolveSpawnTransform(LocalGame game)
    {
        if (RespawnSessionState.SelectedSpawnMode == RespawnLocationMode.InfilSpawn)
            return ResolveInfilSpawn(game);

        return ResolveNearCorpseSpawn();
    }

    private static (Vector3, Quaternion) ResolveInfilSpawn(LocalGame game)
    {
        CacheSpawnSystem(game);
        var spawnSystem = RespawnSessionState.CachedSpawnSystem;
        if (spawnSystem != null)
        {
            var spawnPoint = spawnSystem.SelectSpawnPoint(
                ESpawnCategory.Player,
                game.Profile_0.Info.Side,
                null,
                null,
                null,
                null,
                game.Profile_0.Id);

            if (spawnPoint != null)
                return (spawnPoint.Position, spawnPoint.Rotation);
        }

        return ResolveNearCorpseSpawn();
    }

    private static (Vector3, Quaternion) ResolveNearCorpseSpawn()
    {
        var origin = RespawnSessionState.LastCorpsePosition;
        if (origin == default)
            origin = Vector3.zero;

        var distance = Mathf.Max(5f, Settings.CorpseSpawnDistanceMeters.Value);
        for (var i = 0; i < 12; i++)
        {
            var angle = i * 30f * Mathf.Deg2Rad;
            var candidate = origin + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
            if (NavMesh.SamplePosition(candidate, out var hit, 8f, NavMesh.AllAreas))
                return (hit.position, Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f));
        }

        return (origin + Vector3.up * 2f, Quaternion.identity);
    }
}

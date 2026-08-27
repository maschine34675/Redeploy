using Comfort.Common;
using CommonAssets.Scripts.Game;
using EFT;
using EFT.Communications;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Screens;
using Redeploy.Core.Compat;
using Redeploy.Core.GameAccess;
using Redeploy.Core.Spawning;
using Redeploy.Core.View;
using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace Redeploy.Core.Engine;

/// <summary>Options for one engine-driven respawn.</summary>
public sealed class RespawnRequest
{
    public LocalGame Game;

    /// <summary>The dead body being replaced. Defaults to <c>Game.LocalPlayer</c>.</summary>
    public LocalPlayer DeadPlayer;

    /// <summary>Where the replacement player appears. Required.</summary>
    public ISpawnSelector SpawnSelector;

    /// <summary>
    /// Mutates the profile's equipment before the new body is created (runs at the
    /// exact position of the old naked-respawn swap). Null = keep equipment as-is.
    /// </summary>
    public Action<Profile, LocalPlayer> EquipmentStrategy;

    /// <summary>
    /// Runs right before any profile mutation (health/statistics/equipment) —
    /// e.g. consuming a respawn item from the dead body. Null = nothing.
    /// </summary>
    public Action<LocalPlayer> OnBeforeProfileMutation;

    /// <summary>Health percentage seeded into every body part (clamped to 1..100).</summary>
    public float HealthPercent = 100f;

    /// <summary>Spawn protection after the respawn. Null = none.</summary>
    public SpawnProtectionOptions Protection;
}

public sealed class SpawnProtectionOptions
{
    public float DurationSeconds;

    /// <summary>End protection as soon as the player wields a weapon (prevents invulnerable fighting).</summary>
    public bool EndEarlyOnWeapon = true;

    /// <summary>Stow the weapon the game auto-draws on spawn (naked-respawn behavior).</summary>
    public bool StowAutoDrawnWeapon = true;

    /// <summary>Show toast notifications for protection start/countdown/end.</summary>
    public bool ShowToasts = true;
}

/// <summary>Engine-internal truth about a running respawn pipeline.</summary>
public static class RespawnEngineState
{
    /// <summary>True while the pipeline is replacing the player (owner destroy through rewire).</summary>
    public static bool IsPipelineRunning { get; internal set; }

    public static LocalPlayer LastRespawnedPlayer { get; internal set; }
}

/// <summary>
/// The battle-tested in-place respawn pipeline: tears the dead player out of the
/// running raid, creates a replacement via the game's own player factory, and
/// rewires game/world/UI/camera state to the new body.
/// </summary>
public static class RespawnEngine
{
    /// <summary>Raised after the replacement player is fully wired up (before spawn protection).</summary>
    public static event Action<LocalPlayer> OnPlayerRespawned;

    /// <summary>Raised when spawn protection ends (immediately after respawn if none was requested).</summary>
    public static event Action<LocalPlayer> OnSpawnProtectionEnded;

    /// <summary>
    /// Runs the full respawn. Consume via <c>game.StartCoroutine</c>. Failures are
    /// reported through <paramref name="onFailure"/>; the coroutine never throws.
    /// </summary>
    public static IEnumerator RunRespawn(RespawnRequest request, Action<Exception> onFailure)
    {
        RespawnEngineState.IsPipelineRunning = true;

        var game = request.Game;
        Task<LocalPlayer> createTask = null;
        LocalPlayer deadPlayer = null;
        EftGamePlayerOwner oldOwner;
        Exception pipelineFailure = null;

        try
        {
            deadPlayer = request.DeadPlayer ?? game.LocalPlayer;
            oldOwner = LocalGameAccessor.GetPlayerOwner(game);

            LocalGameAccessor.UnsubscribeDeathHandler(game, deadPlayer);

            if (oldOwner != null)
                OwnerCleanupHelper.DestroyOwnerWithoutClosingBattleUi(oldOwner, deadPlayer);

            UnregisterDeadPlayerFromWorld(deadPlayer);

            request.OnBeforeProfileMutation?.Invoke(deadPlayer);

            ResetProfileHealth(game.Profile, request.HealthPercent);
            RaidVictimsSnapshot.Snapshot(game.Profile);
            request.EquipmentStrategy?.Invoke(game.Profile, deadPlayer);

            var (position, rotation) = request.SpawnSelector.Resolve(game);
            var playerId = LocalGameAccessor.GetNextPlayerId(game);

            var armsMode = AppEnvironment.Config.UseHandsFastAnimator
                ? Player.EUpdateMode.Manual
                : Player.EUpdateMode.Auto;

            var raidSettings = LocalGameAccessor.GetLocalRaidSettings(game);
            var localMode = raidSettings != null ? raidSettings.mode : ELocalMode.TRAINING;

            createTask = game.vmethod_3(
                game.GameWorld,
                playerId,
                position,
                rotation,
                "Player",
                string.Empty,
                EPointOfView.FirstPerson,
                game.Profile,
                false,
                game.UpdateQueue,
                armsMode,
                Player.EUpdateMode.Auto,
                AppEnvironment.Config.CharacterController.ClientPlayerMode,
                () => 1f,
                () => 1f,
                new LocalStatisticManager(),
                LocalGameAccessor.GetSession(game),
                localMode);
        }
        catch (Exception ex)
        {
            pipelineFailure = ex;
        }

        if (pipelineFailure != null)
        {
            // Keep the pipeline flag armed through the end of the failing frame: the old
            // owner's deferred Destroy resolves then, and its CleanupOnDestroy must still
            // be skipped (pre-Core builds cleared their flag one frame later too).
            yield return null;
            RespawnEngineState.IsPipelineRunning = false;
            onFailure(pipelineFailure);
            yield break;
        }

        var creationDeadline = Time.realtimeSinceStartup + 30f;
        while (!createTask.IsCompleted)
        {
            if (Time.realtimeSinceStartup > creationDeadline)
            {
                yield return null;
                RespawnEngineState.IsPipelineRunning = false;
                onFailure(new TimeoutException("Replacement player creation timed out after 30s."));
                yield break;
            }

            yield return null;
        }

        try
        {
            if (createTask.IsFaulted)
                throw createTask.Exception?.InnerException ?? createTask.Exception;

            var newPlayer = createTask.Result;
            newPlayer.Location = game.Location.Id;
            game.LocalPlayer = newPlayer;

            LocalGameAccessor.ReplacePlayerInDictionary(game, newPlayer);
            LocalGameAccessor.SubscribeDeathHandler(game, newPlayer);

            var bots = LocalGameAccessor.GetBotsController(game);
            bots.DestroyInfo(deadPlayer);
            bots.AddActivePLayer(newPlayer);

            if (Singleton<GameWorld>.Instantiated)
                Singleton<GameWorld>.Instance.MainPlayer = newPlayer;

            // CreatePlayerOwner runs the game's func_1 factory, which already subscribes
            // OnLeave -> vmethod_4; wiring it again here would double the leave handling.
            var newOwner = LocalGameAccessor.CreatePlayerOwner(game, newPlayer);
            LocalGameAccessor.SetPlayerOwner(game, newOwner);
            LocalGameAccessor.WirePlayerGameEvents(game, newPlayer);

            GamePlayerOwnerHelper.SetMyPlayer(newPlayer);
            FreecamCompatHelper.RebindOwner(newOwner);
            DynamicMapsCompatHelper.RebindPlayerMarker(newPlayer);
            ExfiltrationController.Instance.InitSecretExfils(newPlayer);

            CameraRebindHelper.RebindToPlayer(newPlayer);
            DeathEffectsHelper.RebindEffectsController(newPlayer);
            newOwner.vmethod_0();
            RaidUiHelper.ClearBlackScreen();
            DeathEffectsHelper.ClearDeathScreenEffects();
            RaidUiHelper.RestoreInputAfterDeath();
            game.StartCoroutine(EnsureBattleUiReady(game, newOwner));
            game.StartCoroutine(FinalizeRespawnView(newPlayer));

            ApplyRespawnHealth(newPlayer);

            RespawnEngineState.LastRespawnedPlayer = newPlayer;
            RaiseRespawned(newPlayer);

            if (request.Protection != null && request.Protection.DurationSeconds > 0f)
                game.StartCoroutine(ApplySpawnProtection(newPlayer, request.Protection));
            else
                RaiseProtectionEnded(newPlayer);
        }
        catch (Exception ex)
        {
            pipelineFailure = ex;
        }

        if (pipelineFailure != null)
        {
            yield return null;
            RespawnEngineState.IsPipelineRunning = false;
            onFailure(pipelineFailure);
            yield break;
        }

        RespawnEngineState.IsPipelineRunning = false;
    }

    private static IEnumerator EnsureBattleUiReady(LocalGame game, EftGamePlayerOwner owner)
    {
        yield return null;
        yield return null;
        yield return null;

        RaidUiHelper.RestoreInputAfterDeath();
        RaidUiHelper.RestoreExtractionTimer(game);
        GamePlayerOwnerHelper.SetMyPlayer(owner?.Player);

        if (owner == null)
            yield break;

        if (!EftScreenManager.Instance.CheckCurrentScreen(EEftScreenType.BattleUI))
            owner.ShowBattleUIScreen();
    }

    private static IEnumerator FinalizeRespawnView(LocalPlayer player)
    {
        yield return null;
        yield return null;

        RaidUiHelper.ClearBlackScreen();
        DeathEffectsHelper.ClearDeathScreenEffects();
        CameraRebindHelper.RebindToPlayer(player);
        DeathEffectsHelper.RebindEffectsController(player);
        GamePlayerOwnerHelper.SetMyPlayer(player);
    }

    private static void UnregisterDeadPlayerFromWorld(LocalPlayer deadPlayer)
    {
        if (deadPlayer == null || !Singleton<GameWorld>.Instantiated)
            return;

        var world = Singleton<GameWorld>.Instance;
        world.UnregisterPlayer(deadPlayer);
    }

    private static void ResetProfileHealth(Profile profile, float healthPercent)
    {
        if (profile?.Health?.BodyParts == null)
            return;

        // Floor of 1%: a 0% seed would spawn the player with 0-HP vitals that die
        // to the first tick of damage.
        var percent = Mathf.Clamp(healthPercent, 1f, 100f) / 100f;

        foreach (var part in profile.Health.BodyParts.Values)
        {
            part.Health.Current = part.Health.Maximum * percent;
            if (part.Effects != null)
                part.Effects.Clear();
        }
    }

    private static void ApplyRespawnHealth(LocalPlayer player)
    {
        // LocalGame.Spawn() calls this at raid start; without it the controller's
        // effect ticking stays disabled and med/food use never completes.
        // No RestoreFullHealth here: the controller is seeded from profile.Health,
        // which ResetProfileHealth already set to the requested percentage — a full
        // restore would override it.
        player.PlayerHealthController?.Start();
    }

    private static IEnumerator ApplySpawnProtection(LocalPlayer player, SpawnProtectionOptions options)
    {
        var duration = options.DurationSeconds;
        if (duration <= 0f || player?.ActiveHealthController == null)
        {
            RaiseProtectionEnded(player);
            yield break;
        }

        var healthController = player.ActiveHealthController;
        var originalCoeff = healthController.DamageCoeff;
        healthController.SetDamageCoeff(0f);
        CoreLog.LogInfo($"Spawn protection active for {duration:0.#}s.");
        if (options.ShowToasts)
            ShowToast($"Spawn protection active: {duration:0}s", ENotificationIconType.Default, Color.green);

        var endedByWeapon = false;
        var elapsed = 0f;
        // The game auto-draws the first available weapon on spawn — for a naked respawn
        // that's the transferred melee weapon, which is not a player action. Stow it and
        // only arm the early-end check once the hands have actually been empty.
        var weaponCheckArmed = false;
        var nextStowAttempt = 0f;
        var lastAnnouncedSecond = int.MaxValue;
        while (elapsed < duration)
        {
            if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
            {
                RaiseProtectionEnded(player);
                yield break;
            }

            if (options.EndEarlyOnWeapon)
            {
                if (!weaponCheckArmed)
                {
                    if (!IsWieldingWeapon(player))
                    {
                        weaponCheckArmed = true;
                    }
                    else if (options.StowAutoDrawnWeapon
                        && elapsed >= nextStowAttempt
                        && !player.InventoryController.IsChangingWeapon)
                    {
                        // Never call SetEmptyHands mid hands-animation — that corrupts the
                        // HandsController (documented hard-won lesson in PlayerLives).
                        CoreLog.LogInfo("Stowing auto-drawn weapon during spawn protection.");
                        player.SetEmptyHands(null);
                        nextStowAttempt = elapsed + 1.5f;
                    }
                }
                else if (IsWieldingWeapon(player))
                {
                    // Grabbing a weapon from the corpse would otherwise let the player fight while invulnerable.
                    CoreLog.LogInfo("Spawn protection ended early: weapon equipped.");
                    endedByWeapon = true;
                    break;
                }
            }

            var remainingSeconds = Mathf.CeilToInt(duration - elapsed);
            if (options.ShowToasts && remainingSeconds <= 5 && remainingSeconds >= 1 && remainingSeconds < lastAnnouncedSecond)
            {
                lastAnnouncedSecond = remainingSeconds;
                ShowToast($"Spawn protection: {remainingSeconds}s", ENotificationIconType.Default, Color.yellow);
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (player != null && player.HealthController != null && player.HealthController.IsAlive)
        {
            player.ActiveHealthController.SetDamageCoeff(originalCoeff);
            CoreLog.LogInfo("Spawn protection ended.");
            if (options.ShowToasts)
            {
                ShowToast(
                    endedByWeapon ? "Spawn protection ended: weapon equipped!" : "Spawn protection expired!",
                    ENotificationIconType.Alert,
                    Color.yellow);
            }
        }

        RaiseProtectionEnded(player);
    }

    /// <summary>
    /// Temporary invulnerability for a living player (round intermissions etc.) —
    /// the same DamageCoeff mechanism the respawn pipeline uses.
    /// </summary>
    public static Coroutine ApplyProtection(LocalGame game, LocalPlayer player, SpawnProtectionOptions options)
    {
        if (game == null || player == null || options == null)
            return null;

        return game.StartCoroutine(ApplySpawnProtection(player, options));
    }

    private static void ShowToast(string message, ENotificationIconType icon, Color color)
    {
        try
        {
            NotificationManager.DisplayMessageNotification(
                message,
                ENotificationDurationType.Default,
                icon,
                color);
        }
        catch (Exception ex)
        {
            CoreLog.LogWarning($"Failed to show notification: {ex.Message}");
        }
    }

    private static bool IsWieldingWeapon(LocalPlayer player)
    {
        var item = player.HandsController?.Item;
        return item is Weapon || item is Knife || item is ThrowWeap;
    }

    private static void RaiseRespawned(LocalPlayer player)
    {
        try
        {
            OnPlayerRespawned?.Invoke(player);
        }
        catch (Exception ex)
        {
            CoreLog.LogWarning($"OnPlayerRespawned subscriber failed: {ex}");
        }
    }

    private static void RaiseProtectionEnded(LocalPlayer player)
    {
        try
        {
            OnSpawnProtectionEnded?.Invoke(player);
        }
        catch (Exception ex)
        {
            CoreLog.LogWarning($"OnSpawnProtectionEnded subscriber failed: {ex}");
        }
    }
}

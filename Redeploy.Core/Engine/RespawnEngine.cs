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

    /// <summary>
    /// Raid start and raid end both clear the per-raid state, so a raid whose GameEnd was
    /// missed cannot leave a hanging flag (which arms the owner-cleanup and statistics
    /// guards) or a previous raid's body behind.
    /// </summary>
    internal static void ResetForRaidBoundary()
    {
        IsPipelineRunning = false;
        LastRespawnedPlayer = null;
        SpawnSystemCache.Clear();
    }
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
        // Refuse before the teardown: everything from the owner destroy on is irreversible,
        // so a request that cannot succeed must fail with nothing changed. A refusal is
        // reported synchronously through onFailure and raises no event.
        var refusal = RespawnPreflight.Check(
            hasRequest: request != null,
            hasGame: request?.Game != null,
            hasSpawnSelector: request?.SpawnSelector != null,
            engineAvailable: DeathInterception.IsAvailable,
            raidTearingDown: RaidIsTearingDown(request?.Game),
            respawnSignalDepth: CoreSignals.Respawn.Depth,
            pipelineRunning: RespawnEngineState.IsPipelineRunning);

        if (refusal != null)
        {
            CoreLog.LogWarning($"Respawn refused before any teardown: {refusal.Message}");
            onFailure?.Invoke(refusal);
            yield break;
        }

        RespawnEngineState.LastRespawnedPlayer = null;
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

        // An external raid end may have raced the creation wait. Stop() has started the
        // raid-end fade whose completion callback is the ONLY invoker of GameEnd — the
        // rewire's ClearBlackScreen would stop that coroutine and soft-lock the raid in
        // Stopping forever. Abort instead; the raid teardown owns the session now.
        if (RaidIsTearingDown(game))
        {
            yield return null;
            RespawnEngineState.IsPipelineRunning = false;
            onFailure(new OperationCanceledException("Raid ended during respawn — pipeline aborted."));
            yield break;
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

            // DestroyInfo's own chain ends in DeadBodiesController.AddBody for a
            // dead player — the record bots would use to loot the SHARED profile,
            // i.e. the freshly equipped live player. The patch blocks new ones;
            // this clears anything that slipped through earlier.
            DeadBodyGuard.PurgeRecords(bots, deadPlayer.ProfileId);

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
            game.StartCoroutine(FinalizeRespawnView(game, newPlayer));

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

    internal static bool RaidIsTearingDown(LocalGame game)
        => game == null
           || game.Status == GameStatus.Stopping
           || game.Status == GameStatus.SoftStopping
           || game.Status == GameStatus.Stopped;

    private static IEnumerator EnsureBattleUiReady(LocalGame game, EftGamePlayerOwner owner)
    {
        yield return null;
        yield return null;
        yield return null;

        if (RaidIsTearingDown(game))
            yield break;

        // A second replacement (or a camera owner such as a spectator) may have moved on in
        // the meantime; finishing the UI of an owner that is no longer the game's would
        // rebind MyPlayer to a retired body.
        if (!ReferenceEquals(LocalGameAccessor.GetPlayerOwner(game), owner))
            yield break;

        RaidUiHelper.RestoreInputAfterDeath();
        RaidUiHelper.RestoreExtractionTimer(game);
        GamePlayerOwnerHelper.SetMyPlayer(owner?.Player);

        if (owner == null)
            yield break;

        if (!EftScreenManager.Instance.CheckCurrentScreen(EEftScreenType.BattleUI))
            owner.ShowBattleUIScreen();
    }

    private static IEnumerator FinalizeRespawnView(LocalGame game, LocalPlayer player)
    {
        yield return null;
        yield return null;

        if (RaidIsTearingDown(game))
            yield break;

        // Same identity guard: RebindToPlayer destroys whatever controller does not belong
        // to its argument, which would drag the camera back to a body already replaced.
        if (!ReferenceEquals(game.LocalPlayer, player))
            yield break;

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

        // The replacement body comes out of the SHARED player pool (LIFO), and
        // ReturnToPool resets animator params/IK but NOT the ProceduralWeaponAnimation
        // effectors: WalkEffector.CurrentWalkPreset can arrive latched to 'lame'
        // (plus arm-sway factor and breath tremor) from whoever used the body last —
        // a bot or the previous own body that died with a blacked leg. The fresh
        // MovementContext starts at None and SetPhysicalCondition only raises its
        // event on a TRANSITION, so the stale preset is never cleared on its own.
        // Mirror BSG's reconnect path: recompute the health conditions, then drive
        // the condition handler directly to reset the effectors.
        try
        {
            player.UpdateSpeedLimitByHealth();
            player.UpdateArmsCondition();

            var weaponAnimation = player.ProceduralWeaponAnimation;
            if (weaponAnimation != null)
            {
                // Same pooled-body family as the walk preset, worse symptom:
                // BotOwner.Create strips the procedural mask to DrawDown-only on
                // every bot body, and no pool/spawn path ever writes a full mask
                // back. A recycled ex-bot body therefore reaches the respawned
                // player with the Shooting bit cleared — PWA.Shoot() drops every
                // recoil impulse (hip and ADS, all weapons) for the rest of the
                // raid. Restore the live-player default — and ONLY that: Walking
                // and HandShake are transient bits the game raises for the moment
                // they apply (Walking while running, HandShake for a draw shake)
                // and lowers again afterwards. Forcing Walking on made the weapon
                // bob as if walking while standing still, until the first
                // move-state change happened to clear it. DrawDown is re-managed
                // by its own toggle, so setting it is harmless.
                weaponAnimation.Mask =
                    EFT.Animations.EProceduralAnimationMask.Breathing
                    | EFT.Animations.EProceduralAnimationMask.MotionReaction
                    | EFT.Animations.EProceduralAnimationMask.ForceReaction
                    | EFT.Animations.EProceduralAnimationMask.Shooting
                    | EFT.Animations.EProceduralAnimationMask.DrawDown
                    | EFT.Animations.EProceduralAnimationMask.Aiming;

                weaponAnimation.PhysicalConditionUpdated(EPhysicalCondition.None, player.MovementContext.PhysicalCondition);
            }
        }
        catch (Exception ex)
        {
            CoreLog.LogWarning($"Post-respawn condition reset failed (stale limp/sway may persist): {ex.Message}");
        }
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

        // NEVER remember a zero as the value to restore. Two overlapping
        // protections would otherwise hand invulnerability over permanently:
        // the second one captures the first one's zero and writes it back when
        // it expires. Falling back to 1 is right in every case here, because a
        // zero can only mean "someone else is protecting right now".
        var currentCoeff = healthController.DamageCoeff;
        var originalCoeff = currentCoeff > 0f ? currentCoeff : 1f;
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
        => CoreSignals.Respawn.Raise(OnPlayerRespawned, h => h(player), nameof(OnPlayerRespawned));

    private static void RaiseProtectionEnded(LocalPlayer player)
        => CoreSignals.Respawn.Raise(OnSpawnProtectionEnded, h => h(player), nameof(OnSpawnProtectionEnded));
}

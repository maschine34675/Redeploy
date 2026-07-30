using Comfort.Common;
using CorpseRun.Config;
using CorpseRun.UI;
using EFT;
using EFT.Communications;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;
using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace CorpseRun.Services;

public static class RespawnService
{
    private static readonly AccessTools.FieldRef<Player, Corpse> CorpseField =
        AccessTools.FieldRefAccess<Player, Corpse>("Corpse");

    public static void OnPlayerDied(LocalGame game)
    {
        if (!Settings.Enabled.Value)
            return;

        // Without the server mod the profile corrupts after a respawn raid — let death proceed normally.
        if (!ServerModCheck.IsServerModAvailable)
            return;

        if (game == null || game.Location_0?.Id == "hideout")
            return;

        var deadPlayer = game.LocalPlayer_0;
        if (deadPlayer == null || !deadPlayer.IsYourPlayer)
            return;

        if (!Settings.HasRespawnsRemaining(RespawnSessionState.RespawnsUsed))
            return;

        var corpsePos = deadPlayer.Position;

        RespawnSessionState.CurrentLocalGame = game;
        RespawnSessionState.RegisterDeath(corpsePos);
        RespawnSessionState.RespawnItemAvailable = HasRequiredRespawnItem(deadPlayer);
        SpawnPointHelper.CacheSpawnSystem(game);

        RestoreRaidUiAfterDeath(game, deadPlayer);
        game.StartCoroutine(FixOwnCorpseRenders(deadPlayer));

        Plugin.ModLogger.LogInfo($"[CorpseRun] Death intercepted. Respawns used: {RespawnSessionState.RespawnsUsed}/{Settings.MaxRespawnsPerRaid.Value}");

        RespawnPromptPanel.Instance?.Show();
    }

    /// <summary>
    /// The corpse reuses the first-person body whose torso renders as shadows-only;
    /// switch it to third-person renders like the game does for world corpses.
    /// </summary>
    private static IEnumerator FixOwnCorpseRenders(LocalPlayer deadPlayer)
    {
        var timeout = Time.time + 10f;
        while (deadPlayer != null && CorpseField(deadPlayer) == null && Time.time < timeout)
            yield return null;

        if (deadPlayer == null || CorpseField(deadPlayer) == null)
            yield break;

        try
        {
            deadPlayer.PlayerBody?.UpdatePlayerRenders(EPointOfView.ThirdPerson, deadPlayer.Side);
            Plugin.ModLogger.LogInfo("[CorpseRun] Corpse renders switched to third person.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"[CorpseRun] Failed to fix corpse renders: {ex.Message}");
        }
    }

    private static void RestoreRaidUiAfterDeath(LocalGame game, LocalPlayer deadPlayer)
    {
        try
        {
            RaidUiHelper.RestoreInputAfterDeath();
            RaidUiHelper.ClearBlackScreen();
            DeathEffectsHelper.ClearDeathScreenEffects();

            if (MonoBehaviourSingleton<PreloaderUI>.Instantiated)
                MonoBehaviourSingleton<PreloaderUI>.Instance.FadeBlackScreen(0f, -3f);

            var owner = LocalGameAccessor.GetPlayerOwner(game);
            if (owner != null && deadPlayer != null && deadPlayer.HealthController.IsAlive == false)
            {
                // Keep battle UI available for the respawn prompt without rebinding a dead owner.
                GamePlayerOwner.SetIgnoreInput(false);
            }
            else
            {
                owner?.vmethod_0();
            }
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"[CorpseRun] UI restore after death failed: {ex.Message}");
        }
    }

    public static void GiveUpRaid()
    {
        var game = RespawnSessionState.CurrentLocalGame;
        if (game == null)
            return;

        RespawnSessionState.HasGivenUp = true;
        RespawnSessionState.PendingRespawnChoice = false;
        RespawnPromptPanel.Instance?.Hide();

        Plugin.ModLogger.LogInfo("[CorpseRun] Player gave up — ending raid as Killed.");
        LocalGameAccessor.InvokeDeathScreen(game);
    }

    public static void BeginRespawn(RespawnLocationMode spawnMode)
    {
        var game = RespawnSessionState.CurrentLocalGame;
        if (game == null || RespawnSessionState.IsRespawnInProgress)
            return;

        RespawnSessionState.SelectedSpawnMode = spawnMode;
        RespawnPromptPanel.Instance?.Hide();
        game.StartCoroutine(RespawnCoroutine(game));
    }

    private static IEnumerator RespawnCoroutine(LocalGame game)
    {
        RespawnSessionState.IsRespawnInProgress = true;

        var cooldown = Mathf.Max(0f, Settings.RespawnCooldownSeconds.Value);
        if (cooldown > 0f)
            yield return new WaitForSeconds(cooldown);

        Exception failure = null;
        yield return game.StartCoroutine(PerformRespawn(game, e => failure = e));

        if (failure != null)
        {
            Plugin.ModLogger.LogError($"[CorpseRun] Respawn failed: {failure}");
            GiveUpRaid();
        }
        else
        {
            RespawnSessionState.RegisterRespawnUsed();
            Plugin.ModLogger.LogInfo("[CorpseRun] Respawn complete.");
        }

        RespawnSessionState.IsRespawnInProgress = false;
    }

    private static IEnumerator PerformRespawn(LocalGame game, Action<Exception> onFailure)
    {
        Task<LocalPlayer> createTask;
        LocalPlayer deadPlayer;
        EftGamePlayerOwner oldOwner;

        try
        {
            deadPlayer = game.LocalPlayer_0;
            oldOwner = LocalGameAccessor.GetPlayerOwner(game);

            LocalGameAccessor.UnsubscribeDeathHandler(game, deadPlayer);

            if (oldOwner != null)
                OwnerCleanupHelper.DestroyOwnerWithoutClosingBattleUi(oldOwner, deadPlayer);

            UnregisterDeadPlayerFromWorld(deadPlayer);

            ConsumeRespawnItem(deadPlayer);

            ResetProfileHealth(game.Profile_0);
            RespawnStatisticsHelper.SnapshotRaidVictims(game.Profile_0);
            RespawnEquipmentHelper.PrepareNakedRespawnProfile(game.Profile_0, deadPlayer);

            var (position, rotation) = SpawnPointHelper.ResolveSpawnTransform(game);
            var playerId = LocalGameAccessor.GetNextPlayerId(game);

            var armsMode = BackendConfigAbstractClass.Config.UseHandsFastAnimator
                ? Player.EUpdateMode.Manual
                : Player.EUpdateMode.Auto;

            var raidSettings = LocalGameAccessor.GetLocalRaidSettings(game);
            var localMode = raidSettings != null ? raidSettings.mode : ELocalMode.TRAINING;

            createTask = game.vmethod_3(
                game.GameWorld_0,
                playerId,
                position,
                rotation,
                "Player",
                string.Empty,
                EPointOfView.FirstPerson,
                game.Profile_0,
                false,
                game.UpdateQueue,
                armsMode,
                Player.EUpdateMode.Auto,
                BackendConfigAbstractClass.Config.CharacterController.ClientPlayerMode,
                () => 1f,
                () => 1f,
                new GClass2268(),
                LocalGameAccessor.GetSession(game),
                localMode);
        }
        catch (Exception ex)
        {
            onFailure(ex);
            yield break;
        }

        while (!createTask.IsCompleted)
            yield return null;

        try
        {
            if (createTask.IsFaulted)
                throw createTask.Exception?.InnerException ?? createTask.Exception;

            var newPlayer = createTask.Result;
            newPlayer.Location = game.Location_0.Id;
            game.LocalPlayer_0 = newPlayer;

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
            ExfiltrationControllerClass.Instance.InitSecretExfils(newPlayer);

            CameraRebindHelper.RebindToPlayer(newPlayer);
            DeathEffectsHelper.RebindEffectsController(newPlayer);
            newOwner.vmethod_0();
            RaidUiHelper.ClearBlackScreen();
            DeathEffectsHelper.ClearDeathScreenEffects();
            RaidUiHelper.RestoreInputAfterDeath();
            game.StartCoroutine(EnsureBattleUiReady(newOwner));
            game.StartCoroutine(FinalizeRespawnView(newPlayer));

            ApplyRespawnHealth(newPlayer);
            game.StartCoroutine(ApplySpawnProtection(newPlayer));
        }
        catch (Exception ex)
        {
            onFailure(ex);
        }
    }

    private static IEnumerator EnsureBattleUiReady(EftGamePlayerOwner owner)
    {
        yield return null;
        yield return null;
        yield return null;

        RaidUiHelper.RestoreInputAfterDeath();
        RaidUiHelper.RestoreExtractionTimer(RespawnSessionState.CurrentLocalGame);
        GamePlayerOwnerHelper.SetMyPlayer(owner?.Player);

        if (owner == null)
            yield break;

        if (!CurrentScreenSingletonClass.Instance.CheckCurrentScreen(EEftScreenType.BattleUI))
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

    private static void ResetProfileHealth(Profile profile)
    {
        if (profile?.Health?.BodyParts == null)
            return;

        var percent = Mathf.Clamp(Settings.RespawnHealthPercent.Value, 0f, 100f) / 100f;

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
        player.PlayerHealthController?.Start();
        player.ActiveHealthController?.RestoreFullHealth();
    }

    private static bool HasRequiredRespawnItem(LocalPlayer player)
    {
        var templateId = Settings.GetRequiredRespawnItemTemplateId();
        if (string.IsNullOrEmpty(templateId))
            return true;

        var items = player?.Profile?.Inventory?.GetPlayerItems(EPlayerItems.Equipment);
        if (items == null)
            return false;

        foreach (var item in items)
        {
            if (item.TemplateId == templateId)
                return true;
        }

        return false;
    }

    private static void ConsumeRespawnItem(LocalPlayer deadPlayer)
    {
        var templateId = Settings.GetRequiredRespawnItemTemplateId();
        if (string.IsNullOrEmpty(templateId))
            return;

        Item requiredItem = null;
        foreach (var item in deadPlayer.Profile.Inventory.GetPlayerItems(EPlayerItems.Equipment))
        {
            if (item.TemplateId == templateId)
            {
                requiredItem = item;
                break;
            }
        }

        if (requiredItem == null)
        {
            Plugin.ModLogger.LogWarning("[CorpseRun] Required respawn item not found at consume time; respawning anyway.");
            return;
        }

        var result = InteractionsHandlerClass.RemoveWithoutRestrictions(requiredItem, deadPlayer.InventoryController);
        if (result.Failed)
            Plugin.ModLogger.LogWarning($"[CorpseRun] Failed to consume respawn item: {result.Error}");
        else
            Plugin.ModLogger.LogInfo($"[CorpseRun] Consumed respawn item {Settings.RequiredRespawnItem.Value}.");
    }

    private static IEnumerator ApplySpawnProtection(LocalPlayer player)
    {
        var duration = Settings.SpawnProtectionSeconds.Value;
        if (duration <= 0f || player?.ActiveHealthController == null)
            yield break;

        var healthController = player.ActiveHealthController;
        var originalCoeff = healthController.DamageCoeff;
        healthController.SetDamageCoeff(0f);
        Plugin.ModLogger.LogInfo($"[CorpseRun] Spawn protection active for {duration:0.#}s.");
        ShowToast($"Spawn protection active: {duration:0}s", ENotificationIconType.Default, Color.green);

        var endedByWeapon = false;
        var elapsed = 0f;
        while (elapsed < duration)
        {
            if (player == null || player.HealthController == null || !player.HealthController.IsAlive)
                yield break;

            // Grabbing a weapon from the corpse would otherwise let the player fight while invulnerable.
            if (IsWieldingWeapon(player))
            {
                Plugin.ModLogger.LogInfo("[CorpseRun] Spawn protection ended early: weapon equipped.");
                endedByWeapon = true;
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        if (player != null && player.HealthController != null && player.HealthController.IsAlive)
        {
            player.ActiveHealthController.SetDamageCoeff(originalCoeff);
            Plugin.ModLogger.LogInfo("[CorpseRun] Spawn protection ended.");
            ShowToast(
                endedByWeapon ? "Spawn protection ended: weapon equipped!" : "Spawn protection expired!",
                ENotificationIconType.Alert,
                Color.yellow);
        }
    }

    private static void ShowToast(string message, ENotificationIconType icon, Color color)
    {
        try
        {
            NotificationManagerClass.DisplayMessageNotification(
                message,
                ENotificationDurationType.Default,
                icon,
                color);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"[CorpseRun] Failed to show notification: {ex.Message}");
        }
    }

    private static bool IsWieldingWeapon(LocalPlayer player)
    {
        var item = player.HandsController?.Item;
        return item is Weapon || item is KnifeItemClass || item is ThrowWeapItemClass;
    }
}

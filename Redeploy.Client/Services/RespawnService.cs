using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using Redeploy.Config;
using Redeploy.Core.Engine;
using Redeploy.Core.GameAccess;
using Redeploy.Core.Inventory;
using Redeploy.Core.Spawning;
using Redeploy.Core.View;
using Redeploy.UI;
using System;
using System.Collections;
using UnityEngine;

namespace Redeploy.Services;

public static class RespawnService
{
    private static readonly AccessTools.FieldRef<Player, EFT.Interactive.Corpse> CorpseField =
        AccessTools.FieldRefAccess<Player, EFT.Interactive.Corpse>("Corpse");

    public static void OnPlayerDied(LocalGame game)
    {
        if (!Settings.Enabled.Value)
            return;

        // A replacement player can never sync across Fika co-op clients — stay hands-off.
        if (Core.Compat.FikaCompatCheck.IsFikaLoaded)
            return;

        // Without the server mod the profile corrupts after a respawn raid — let death proceed normally.
        if (!ServerModCheck.IsServerModAvailable)
            return;

        if (game == null || game.Location?.Id == "hideout")
            return;

        var deadPlayer = game.LocalPlayer;
        if (deadPlayer == null || !deadPlayer.IsYourPlayer)
            return;

        if (!Settings.HasRespawnsRemaining(RespawnSessionState.RespawnsUsed))
            return;

        var corpsePos = deadPlayer.Position;

        RespawnSessionState.CurrentLocalGame = game;
        RespawnSessionState.RegisterDeath(corpsePos);
        RespawnSessionState.RespawnItemAvailable = HasRequiredRespawnItem(deadPlayer);
        SpawnSystemCache.Warm(game);

        RestoreRaidUiAfterDeath(game, deadPlayer);
        game.StartCoroutine(FixOwnCorpseRenders(deadPlayer));

        Plugin.ModLogger.LogInfo($"Death intercepted. Respawns used: {RespawnSessionState.RespawnsUsed}/{Settings.MaxRespawnsPerRaid.Value}");

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
            Plugin.ModLogger.LogInfo("Corpse renders switched to third person.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Failed to fix corpse renders: {ex.Message}");
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
            Plugin.ModLogger.LogWarning($"UI restore after death failed: {ex.Message}");
        }
    }

    public static void GiveUpRaid()
    {
        // Before the early return: the prompt is on screen either way, and it
        // now owns the cursor until it is told to let go.
        RespawnPromptPanel.Instance?.Hide();

        var game = RespawnSessionState.CurrentLocalGame;
        if (game == null)
            return;

        RespawnSessionState.HasGivenUp = true;
        RespawnSessionState.PendingRespawnChoice = false;

        Plugin.ModLogger.LogInfo("Player gave up — ending raid as Killed.");
        LocalGameAccessor.InvokeDeathScreen(game);
    }

    public static void BeginRespawn(RespawnLocationMode spawnMode)
    {
        // Same reasoning as GiveUpRaid: the player pressed a button on the
        // prompt, so it is going away whether or not the respawn can start.
        RespawnPromptPanel.Instance?.Hide();

        var game = RespawnSessionState.CurrentLocalGame;
        if (game == null || RespawnSessionState.IsRespawnInProgress)
            return;

        RespawnSessionState.SelectedSpawnMode = spawnMode;
        game.StartCoroutine(RespawnCoroutine(game));
    }

    private static IEnumerator RespawnCoroutine(LocalGame game)
    {
        RespawnSessionState.IsRespawnInProgress = true;

        var cooldown = Mathf.Max(0f, Settings.RespawnCooldownSeconds.Value);
        if (cooldown > 0f)
            yield return new WaitForSeconds(cooldown);

        var request = new RespawnRequest
        {
            Game = game,
            DeadPlayer = game.LocalPlayer,
            SpawnSelector = BuildSpawnSelector(),
            OnBeforeProfileMutation = ConsumeRespawnItem,
            EquipmentStrategy = (profile, dead) => EquipmentSurgeon.ReplaceEquipment(profile, dead),
            HealthPercent = Settings.RespawnHealthPercent.Value,
            Protection = new SpawnProtectionOptions
            {
                DurationSeconds = Settings.SpawnProtectionSeconds.Value,
                EndEarlyOnWeapon = true,
                StowAutoDrawnWeapon = true,
                ShowToasts = true,
            },
        };

        Exception failure = null;
        yield return game.StartCoroutine(RespawnEngine.RunRespawn(request, e => failure = e));

        if (failure != null)
        {
            Plugin.ModLogger.LogError($"Respawn failed: {failure}");
            GiveUpRaid();
        }
        else
        {
            RespawnSessionState.RegisterRespawnUsed();
            Plugin.ModLogger.LogInfo("Respawn complete.");
        }

        RespawnSessionState.IsRespawnInProgress = false;
    }

    private static ISpawnSelector BuildSpawnSelector()
    {
        var nearCorpse = new NearPositionSpawnSelector(
            () => RespawnSessionState.LastCorpsePosition,
            () => Settings.CorpseSpawnDistanceMeters.Value);

        return RespawnSessionState.SelectedSpawnMode == RespawnLocationMode.InfilSpawn
            ? (ISpawnSelector)new InfilSpawnSelector(nearCorpse)
            : nearCorpse;
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
            Plugin.ModLogger.LogWarning("Required respawn item not found at consume time; respawning anyway.");
            return;
        }

        var result = ItemManipulator.RemoveWithoutRestrictions(requiredItem, deadPlayer.InventoryController);
        if (result.Failed)
            Plugin.ModLogger.LogWarning($"Failed to consume respawn item: {result.Error}");
        else
            Plugin.ModLogger.LogInfo($"Consumed respawn item {Settings.RequiredRespawnItem.Value}.");
    }
}

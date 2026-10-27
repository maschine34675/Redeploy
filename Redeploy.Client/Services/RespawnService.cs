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
        // Core has already announced this death as claimed. Returning silently from here
        // would leave the player dead with no prompt and a raid that never ends; throwing
        // makes Core fail open and run the vanilla death instead. RedeployDriver mirrors
        // every one of these gates, so a throw only means the two drifted apart — and the
        // message says where.
        if (!Settings.Enabled.Value)
            throw Drift("the mod is disabled");

        // A replacement player can never sync across Fika co-op clients.
        if (Core.Compat.FikaCompatCheck.IsFikaLoaded)
            throw Drift("Fika is loaded");

        // Without the server mod the profile corrupts after a respawn raid.
        if (!ServerModCheck.IsServerModAvailable)
            throw Drift("the server mod is unavailable");

        if (game == null)
            throw Drift("there is no game");

        if (game.Location?.Id == "hideout")
            throw Drift("the location is the hideout");

        if (!Settings.HasRespawnsRemaining(RespawnSessionState.RespawnsUsed))
            throw Drift("no respawns remain");

        var deadPlayer = game.LocalPlayer;
        if (deadPlayer == null || !deadPlayer.IsYourPlayer)
            throw Drift("there is no local player to respawn");

        var corpsePos = deadPlayer.Position;

        RespawnSessionState.CurrentLocalGame = game;
        RespawnSessionState.RegisterDeath(corpsePos);
        PmcScavSession.RecordDeath(game);
        RespawnSessionState.RespawnItemAvailable = HasRequiredRespawnItem(deadPlayer);
        SpawnSystemCache.Warm(game);

        RestoreRaidUiAfterDeath(game, deadPlayer);
        game.StartCoroutine(FixOwnCorpseRenders(deadPlayer));

        Plugin.ModLogger.LogInfo($"Death intercepted. Respawns used: {RespawnSessionState.RespawnsUsed}/{Settings.MaxRespawnsPerRaid.Value}");

        RespawnPromptPanel.Instance?.Show();
    }

    private static InvalidOperationException Drift(string reason)
        => new InvalidOperationException($"RedeployDriver claimed a death that RespawnService cannot serve: {reason} (driver gates drifted).");

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
        game.StartCoroutine(RespawnCoroutine(game, PmcScavSession.IsOffered));
    }

    private static IEnumerator RespawnCoroutine(LocalGame game, bool continueAsScav)
    {
        RespawnSessionState.IsRespawnInProgress = true;

        // Counted from the death: time spent watching a death replay or looking around already counts.
        var configured = Settings.RespawnCooldownSeconds.Value;
        var deadFor = RespawnSessionState.LastDeathTime >= 0f ? Time.time - RespawnSessionState.LastDeathTime : -1f;
        var cooldown = RespawnCooldown.Remaining(configured, RespawnSessionState.LastDeathTime, Time.time);
        Plugin.ModLogger.LogInfo(deadFor >= 0f
            ? $"Respawn chosen {deadFor:0.0}s after death; cooldown {configured:0.#}s leaves {cooldown:0.0}s."
            : $"Respawn chosen; death time unknown, full cooldown {cooldown:0.0}s.");

        // No countdown notification: a KillcamReplay death replay may still hold the respawn after
        // the cooldown, so any number shown here could promise too little (its own hint covers that).
        // At least one frame always passes: the click arrives inside the prompt's OnGUI, and the
        // pipeline's teardown must not run in that call.
        if (cooldown > 0f)
            yield return new WaitForSeconds(cooldown);
        else
            yield return null;

        // The cooldown is the one window in which the raid can end underneath a chosen
        // respawn (MIA timer, extract, menu leave). Nothing in the pipeline is reversible,
        // so it must not start then. IsRespawnInProgress stays set on purpose: it keeps the
        // owner-cleanup guard armed through the teardown, and the GameEnd reset clears it.
        if (!ReferenceEquals(RespawnSessionState.CurrentLocalGame, game)
            || !RespawnSessionState.IsInRaid
            || RespawnSessionState.HasGivenUp
            || game.Status != GameStatus.Started)
        {
            Plugin.ModLogger.LogInfo("Respawn cancelled: the raid ended during the cooldown.");
            yield break;
        }

        Profile scavProfile = null;
        ISpawnSelector scavSpawn = null;
        if (continueAsScav)
        {
            Exception prepareFailure = null;
            yield return game.StartCoroutine(PmcScavSession.Prepare(game,
                () => ConsumeRespawnItem(game.LocalPlayer, failOnError: true),
                (profile, selector, error) =>
                {
                    scavProfile = profile;
                    scavSpawn = selector;
                    prepareFailure = error;
                }));

            if (prepareFailure != null || scavProfile == null)
            {
                Plugin.ModLogger.LogWarning($"Scav continuation unavailable: {prepareFailure?.Message ?? "no profile returned"}");
                PmcScavSession.NotifyUnavailable(prepareFailure?.Message);
                if (game.Status == GameStatus.Started)
                    GiveUpRaid();
                RespawnSessionState.IsRespawnInProgress = false;
                yield break;
            }
        }

        var request = new RespawnRequest
        {
            Game = game,
            DeadPlayer = game.LocalPlayer,
            ReplacementProfile = scavProfile,
            SpawnSelector = scavSpawn ?? BuildSpawnSelector(),
            OnBeforeProfileMutation = continueAsScav ? null : dead => ConsumeRespawnItem(dead),
            EquipmentStrategy = continueAsScav ? null : (profile, dead) => EquipmentSurgeon.ReplaceEquipment(profile, dead),
            HealthPercent = Settings.RespawnHealthPercent.Value,
            EnablePipelineTiming = Settings.EnablePipelineTiming.Value,
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

        if (continueAsScav)
        {
            if (PmcScavSession.HasSwitched(game))
            {
                Exception bindingFailure = null;
                try { PmcScavSession.CompleteSwitch(game); }
                catch (Exception ex) { bindingFailure = ex; }
                if (failure == null)
                    failure = bindingFailure;
            }
            else
            {
                PmcScavSession.CancelPrepared();
            }
        }

        if (failure is OperationCanceledException)
        {
            // The raid ended before or while the pipeline ran (MIA, extract, menu leave). That
            // is the same race the cooldown check above catches, only later: a KillcamReplay
            // death replay may hold the respawn until it has faded out. No death screen either:
            // one on a stopping raid would only paint a Killed panel over the real exit status.
            Plugin.ModLogger.LogInfo($"Respawn cancelled: {failure.Message}");
        }
        else if (failure != null)
        {
            Plugin.ModLogger.LogError($"Respawn failed: {failure}");

            if (game.Status == GameStatus.Started)
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

    private static void ConsumeRespawnItem(LocalPlayer deadPlayer, bool failOnError = false)
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
            if (failOnError)
                throw new InvalidOperationException("The required respawn item is no longer available.");
            Plugin.ModLogger.LogWarning("Required respawn item not found at consume time; respawning anyway.");
            return;
        }

        var result = ItemManipulator.RemoveWithoutRestrictions(requiredItem, deadPlayer.InventoryController);
        if (result.Failed && failOnError)
            throw new InvalidOperationException($"The required respawn item could not be consumed: {result.Error}");
        if (result.Failed)
            Plugin.ModLogger.LogWarning($"Failed to consume respawn item: {result.Error}");
        else
            Plugin.ModLogger.LogInfo($"Consumed respawn item {Settings.RequiredRespawnItem.Value}.");
    }
}

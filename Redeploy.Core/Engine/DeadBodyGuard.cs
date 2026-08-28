using Comfort.Common;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Reflection;

namespace Redeploy.Core.Engine;

/// <summary>
/// Keeps the AI's dead-body registry away from the respawning player.
///
/// Bots loot corpses through <c>DeadBodiesController</c> records, and their loot
/// state machine resolves the tree LIVE via
/// <c>deadBody.Player.InventoryController.Inventory.Equipment</c>. A respawned
/// player shares ONE Profile — and therefore one Inventory object — with every
/// body it left behind (the equipment surgeon only repoints
/// <c>Inventory.Equipment</c> to the fresh tree). So a record for an own corpse
/// makes bots strip the LIVING player: weapons first, then rig/pocket contents
/// one item per tick, finally the vest and backpack thrown on the ground —
/// exactly the "my gear vanishes piece by piece" symptom, with the failed
/// operations surfacing as "hands controller can't perform this operation".
///
/// Suppressing the record costs nothing: without a respawn the raid is over the
/// moment the player dies, so nobody ever observes a bot looting their corpse.
/// The corpse stays fully lootable for the player (that path goes through the
/// corpse's own item controller, which is untouched).
/// </summary>
public static class DeadBodyGuard
{
    private static bool _patched;

    public static void EnsurePatched()
    {
        if (_patched)
            return;

        _patched = true;
        new SkipOwnBodyPatch().Enable();
    }

    /// <summary>
    /// Drops every record pointing at the given profile — defence for records
    /// created before the patch was live, or through a path it does not cover.
    /// </summary>
    public static void PurgeRecords(BotsController bots, string profileId)
    {
        if (string.IsNullOrEmpty(profileId))
            return;

        try
        {
            var controller = bots?.BotSpawner?._deadBodiesController;
            if (controller?.Bodies == null)
                return;

            var purged = 0;
            for (var i = controller.Bodies.Count - 1; i >= 0; i--)
            {
                var body = controller.Bodies[i];
                if (body?.Player == null || body.Player.ProfileId != profileId)
                    continue;

                // Also cleans the per-group lists.
                controller.RemoveBody(body);
                purged++;
            }

            if (purged > 0)
                CoreLog.LogInfo($"Purged {purged} own dead-body record(s) so bots cannot loot the live inventory.");
        }
        catch (Exception ex)
        {
            CoreLog.LogWarning($"Dead-body purge failed: {ex.Message}");
        }
    }

    private sealed class SkipOwnBodyPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(DeadBodiesController), nameof(DeadBodiesController.AddBody),
                new[] { typeof(IPlayer) });

        [PatchPrefix]
        private static bool Prefix(IPlayer player)
        {
            try
            {
                if (player == null || !Singleton<GameWorld>.Instantiated)
                    return true;

                var localId = Singleton<GameWorld>.Instance.MainPlayer?.ProfileId;
                return localId == null || player.ProfileId != localId;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }
}

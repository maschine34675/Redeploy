using System.Reflection;
using AmandsSense;
using AmandsSense.Components;
using AmandsSense.Models;
using AmandsSense.Patches;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Redeploy.Core.Compat;

internal static class Program
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }

    private static void Main()
    {
        var plugin = new AmandsSensePlugin();
        var metadata = plugin.GetType().GetCustomAttribute<BepInPlugin>();
        Require(AmandsSenseCompat.GetRejectionReason() == null, "absent mod is inert");
        Chainloader.PluginInfos.Add(metadata.GUID, new PluginInfo { Instance = plugin, Metadata = metadata });
        new Harmony("test.native.sense").Patch(AccessTools.Method(typeof(Player), nameof(Player.Init)),
            postfix: new HarmonyMethod(typeof(AmandsPlayerPatch), nameof(AmandsPlayerPatch.PatchPostFix)));
        Require(AmandsSenseCompat.GetRejectionReason() == null, "native contract accepted");
        var world = Singleton<GameWorld>.Instance = new GameWorld();
        var game = new LocalGame { GameWorld = world, Profile = new() };
        Singleton<AbstractGame>.Instance = game;
        LocalPlayer NewBody(Profile profile) => new() { GameWorld = world, Profile = profile, IsYourPlayer = true };
        var old = game.LocalPlayer = NewBody(game.Profile);
        var valid = NewBody(game.Profile);
        var destroyed = new Player { Destroyed = true, GameWorld = world };
        void Add(Player body) => AmandsSenseClass.DeadPlayers.Add(new SenseDeadPlayer { victim = body });
        var scanner = new AmandsSenseClass();
        Add(valid);
        old.Init();
        Require(AmandsSenseClass.DeadPlayers.Count == 0 && AmandsSenseClass.Reloads == 1,
            "ordinary first construction keeps native reset and file load");
        Add(valid);
        AmandsSenseClass.Active.Add(7, () => AmandsSenseClass.Active.Remove(7));
        var ownScav = new Profile();
        BodyReplacementContext.Begin(game, old, ownScav);
        var next = NewBody(ownScav);
        next.Init();
        Require(ReferenceEquals(next, AmandsSenseClass.Player) && ReferenceEquals(next.InventoryController, AmandsSenseClass.inventoryControllerClass),
            "native hook binds the replacement player and its inventory");
        Require(AmandsSenseClass.Active.ContainsKey(7) && AmandsSenseClass.Reloads == 1, "active marker survives creation without reloading audio");
        game.Profile = ownScav;
        game.LocalPlayer = next;
        End();
        Add(destroyed);
        Add(new Player { GameWorld = new GameWorld() });
        Add(old); // The own previous corpse is legitimate and has the same world.
        scanner.SenseDeadBodies();
        Require(AmandsSenseClass.DeadPlayers.Select(row => row.victim).SequenceEqual(new[] { valid, old }),
            "a scan retains the prior bot and local corpses, and removes only invalid victims");
        Require(ReferenceEquals(next, AmandsSenseClass.Player), "committed candidate stays bound even after a late pipeline failure");

        BodyReplacementContext.Begin(game, next);
        var failed = NewBody(game.Profile);
        failed.Init();
        End();
        Require(ReferenceEquals(next, AmandsSenseClass.Player) && ReferenceEquals(next.InventoryController, AmandsSenseClass.inventoryControllerClass),
            "failure before LocalGame commit restores both native references");
        Add(failed);
        scanner.SenseDeadBodies();
        Require(AmandsSenseClass.DeadPlayers.Count == 2, "a discarded candidate recorded later cannot become a corpse marker");

        world.MainPlayer = next;
        BodyReplacementContext.Begin(game, next);
        BodyReplacementContext.CancelPreparation();
        Require(AmandsSenseClass.Active.Count == 1 && ReferenceEquals(next, AmandsSenseClass.Player),
            "refused preparation does not affect markers or the bound player");
        AmandsSenseCompat.RetireWorld(world);
        BodyReplacementContext.Reset();
        bool failedNatively = false;
        try { next.Init(); } catch (InvalidOperationException) { failedNatively = true; }
        Require(failedNatively, "out-of-scope native Clear reaches the original collection exception");
        next.Init();
        Require(AmandsSenseClass.Reloads == 2 && AmandsSenseClass.DeadPlayers.Count == 0, "ordinary resets resume after world teardown");
        Console.WriteLine("AMANDS_SENSE_CHECKS_PASSED");
    }

    private static void End()
    {
        AmandsSenseCompat.EndAttempt();
        BodyReplacementContext.EndAttempt();
    }
}

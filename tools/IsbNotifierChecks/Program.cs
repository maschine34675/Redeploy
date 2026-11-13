using BepInEx.Bootstrap;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;
using System.Reflection;

internal static class Program
{
    private static int _checks;
    internal static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); _checks++; }
    private static bool Ended => Fixture.Get<bool>("LocalRaidPresentationEnded");
    private static void Close() => Fixture.Set("LocalRaidPresentationEnded", true);
    private static void Enable(bool value) => Fixture.Get<ConfigEntry<bool>>("EnablePlugin").Value = value;
    private static LocalGame Raid()
    {
        if (BodyReplacementContext.CurrentWorld != null) IsbNotifierCompat.RetireWorld(BodyReplacementContext.CurrentWorld);
        BodyReplacementContext.Reset();
        var world = new GameWorld();
        var old = new LocalPlayer { GameWorld = world };
        var game = new LocalGame { GameWorld = world, LocalPlayer = old, Profile = old.Profile };
        world.MainPlayer = old; world.AllAlivePlayersList.Add(old);
        Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
        Enable(true); Fixture.Headless = false; Close();
        return game;
    }
    private static LocalPlayer Prepare(LocalGame game)
    {
        var old = game.LocalPlayer;
        BodyReplacementContext.Begin(game, old);
        IsbNotifierCompat.Prepare(game, old);
        Check(Ended, "preparation must not clear the native latch");
        return old;
    }
    private static LocalPlayer Candidate(LocalGame game, bool scav = false)
    {
        var next = new LocalPlayer { GameWorld = game.GameWorld, Profile = scav ? BodyReplacementContext.ExpectedProfile : game.Profile };
        game.GameWorld.MainPlayer = next;
        Check(BodyReplacementContext.TryOwnCandidate(next), "fixture must obtain real Core physical ownership");
        game.Profile = next.Profile; game.LocalPlayer = next;
        game.GameWorld.AllAlivePlayersList.Clear(); game.GameWorld.AllAlivePlayersList.Add(next);
        return next;
    }
    private static void Publish(LocalPlayer old, LocalPlayer next)
    { RespawnApi.Publish(old, next); BodyReplacementContext.EndAttempt(); IsbNotifierCompat.Tick(); }

    private static void Main(string[] args)
    {
        var mode = args.Length == 0 ? "normal" : args[0];
        if (mode == "published") { PublishedMetadata.Check(args[1]); Console.WriteLine("ISB_NOTIFIER_CONTRACT_PASSED checks=" + _checks); return; }
        if (mode == "absent")
        {
            for (int i = 0; i < 10; i++) { Check(IsbNotifierCompat.GetRejectionReason() == null, "absent plugin remains optional"); IsbNotifierCompat.Tick(); }
            Console.WriteLine("ISB_NOTIFIER_CASE_PASSED absent"); return;
        }
        Fixture.Build(mode);
        var rejection = IsbNotifierCompat.GetRejectionReason();
        if (mode == "main-only")
        {
            Check(rejection == null, "actual main ISB plugin GUID alone must leave the optional notifier absent");
            Close(); IsbNotifierCompat.Tick();
            Check(Ended && Fixture.ResetCount == 0, "main-only presence must not touch presentation state");
            Check(HarmonyLib.Harmony.GetPatchInfo(Fixture.PluginType.GetMethod("ResetState", BindingFlags.NonPublic | BindingFlags.Static)) == null,
                "main-only presence must not install notifier hooks");
            Console.WriteLine("ISB_NOTIFIER_CASE_PASSED main-only"); return;
        }
        if (new[] { "older", "drift", "main-guid", "assembly" }.Contains(mode))
        { Check(rejection != null, "invalid private contract must refuse before teardown"); Console.WriteLine("ISB_NOTIFIER_CASE_PASSED " + mode); return; }
        Check(rejection == null, "reviewed or newer matching contract must bind");
        var history = Fixture.Get<Dictionary<string, int>>("History");
        if (mode == "missing-hook")
        {
            new HarmonyLib.Harmony("com.maschine.Redeploy.Core.IsbNotifierCompat").UnpatchSelf();
            Check(IsbNotifierCompat.GetRejectionReason() != null, "removed real lifecycle hook refuses before teardown");
            Console.WriteLine("ISB_NOTIFIER_CASE_PASSED missing-hook"); return;
        }
        if (mode == "metadata-change")
        {
            Chainloader.PluginInfos[IsbNotifierCompat.PluginGuid].Metadata = new BepInEx.BepInPlugin("com.samc137.isbspecialforces", "other", "2.0.4");
            Check(IsbNotifierCompat.GetRejectionReason() != null, "cached registration metadata drift refuses before teardown");
            Console.WriteLine("ISB_NOTIFIER_CASE_PASSED metadata-change"); return;
        }
        if (mode == "destroyed" || mode == "singleton-change")
        {
            var g = Raid(); var o = Prepare(g); var n = Candidate(g);
            if (mode == "destroyed") Fixture.Plugin.Destroyed = true; else Fixture.Set("Instance", null);
            Publish(o, n);
            Check(Ended && IsbNotifierCompat.GetRejectionReason() != null, "lost notifier owner stays closed and quarantines future replacement");
            Console.WriteLine("ISB_NOTIFIER_CASE_PASSED " + mode); return;
        }
        if (mode == "fault")
        {
            var g = Raid(); var o = Prepare(g); var n = Candidate(g); Fixture.ThrowEnabled = true; Publish(o, n);
            Check(Ended && IsbNotifierCompat.GetRejectionReason() != null, "predicate failure preserves latch and quarantines future replacement");
            Console.WriteLine("ISB_NOTIFIER_CASE_PASSED fault"); return;
        }
        var game = Raid(); var old = Prepare(game); var next = Candidate(game);
        Check(Ended, "candidate construction without commit does not reopen");
        Publish(old, next); Check(!Ended, "committed same-profile replacement reopens");
        Check(ReferenceEquals(old.Profile, next.Profile), "same-profile test aliases the exact profile");
        Check(Fixture.ResetCount == 0 && Fixture.Get<bool>("InitialMessageShown") && ReferenceEquals(history, Fixture.Get<Dictionary<string, int>>("History")) && history["commander"] == 7, "history and initial announcement stay intact");
        next.HealthController.IsAlive = false; Fixture.Refresh(); IsbNotifierCompat.Tick(); Check(Ended, "new death closes and one-shot lease cannot reopen it");
        old = Prepare(game); next = Candidate(game); Publish(old, next); Check(!Ended, "second same-profile replacement reopens independently");

        // Refusal from a later adapter must leave both foreign state and prior ownership intact.
        Close(); old = Prepare(game); IsbNotifierCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation(); IsbNotifierCompat.Tick(); Check(Ended, "refused preparation cannot reopen");
        old = Prepare(game); next = Candidate(game); BodyReplacementContext.EndAttempt(); RespawnApi.Fail(old, "rewire"); IsbNotifierCompat.Tick();
        Check(Ended && ReferenceEquals(game.LocalPlayer, next) && next.HealthController.IsAlive, "failed rewire with living published-looking candidate stays closed");
        foreach (var stage in new[] { "prepare", "create", "raid-ending" })
        {
            game = Raid(); old = Prepare(game); BodyReplacementContext.EndAttempt(); RespawnApi.Fail(old, stage); IsbNotifierCompat.Tick();
            Check(Ended, "failed " + stage + " cannot reopen the dead body");
        }

        game = Raid(); old = game.LocalPlayer; var scav = new Profile(); BodyReplacementContext.Begin(game, old, scav); IsbNotifierCompat.Prepare(game, old); next = Candidate(game, scav: true); Publish(old, next);
        Check(!Ended && !ReferenceEquals(old.Profile, next.Profile), "own-Scav committed physical replacement reopens without carrying old profile state");

        game = Raid(); old = Prepare(game); next = Candidate(game);
        var alias = new LocalPlayer { Profile = old.Profile, GameWorld = game.GameWorld };
        RespawnApi.Publish(alias, next); Check(Ended, "same-profile false old-body event cannot reopen");
        RespawnApi.Publish(old, alias); Check(Ended, "same-profile unowned candidate cannot reopen");
        game.GameWorld.MainPlayer = alias; RespawnApi.Publish(old, next); Check(Ended, "world MainPlayer mismatch cannot reopen");
        game.GameWorld.MainPlayer = next; Publish(old, next); Check(!Ended, "negative identity controls do not poison later exact commit");

        game = Raid(); old = game.LocalPlayer; BodyReplacementContext.Begin(game, old);
        bool wrongOldRefused = false;
        try { IsbNotifierCompat.Prepare(game, new LocalPlayer { GameWorld = game.GameWorld, Profile = old.Profile }); }
        catch (InvalidOperationException) { wrongOldRefused = true; }
        Check(wrongOldRefused && Ended, "preparation rejects same-profile wrong physical old body before mutation");
        IsbNotifierCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();

        game = Raid(); old = Prepare(game); next = Candidate(game); Enable(false); Publish(old, next);
        Check(Ended && !Fixture.Get<ConfigEntry<bool>>("EnablePlugin").Value, "disabled notifier remains disabled and closed");
        Enable(true); IsbNotifierCompat.Tick(); Check(!Ended, "enabling after commit reopens only its still-current living body");

        game = Raid(); old = Prepare(game); next = Candidate(game); Enable(false); Publish(old, next);
        Prepare(game); IsbNotifierCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation(); Enable(true); IsbNotifierCompat.Tick();
        Check(!Ended, "refused retry preserves the prior committed body's disabled pending lease");

        game = Raid(); old = Prepare(game); next = Candidate(game); Fixture.Headless = true; Publish(old, next); Check(Ended, "headless presentation stays closed");
        Fixture.Headless = false; IsbNotifierCompat.Tick(); Check(!Ended, "presentation capability can resume for exact committed body");

        game = Raid(); old = Prepare(game); next = Candidate(game); game.GameWorld.AllAlivePlayersList.Clear(); Publish(old, next); Check(Ended, "unregistered body cannot reopen");
        game.GameWorld.AllAlivePlayersList.Add(new LocalPlayer { Profile = next.Profile, GameWorld = game.GameWorld }); IsbNotifierCompat.Tick(); Check(Ended, "alive-list profile alias is not physical membership");
        game.GameWorld.AllAlivePlayersList.Add(next); IsbNotifierCompat.Tick(); Check(!Ended, "exact live membership completes reopening");

        game = Raid(); old = Prepare(game); next = Candidate(game); Enable(false); Publish(old, next); game.Status = GameStatus.Stopping; Enable(true); IsbNotifierCompat.Tick(); Check(Ended, "real game stopping discards pending reopen");
        game.Status = GameStatus.Started; IsbNotifierCompat.Tick(); Check(Ended, "stopped lease cannot revive on later status change");

        game = Raid(); old = Prepare(game); next = Candidate(game); Enable(false); Publish(old, next); Fixture.Call("MarkLocalRaidPresentationEnded"); Enable(true); IsbNotifierCompat.Tick(); Check(Ended, "explicit native end invalidates disabled pending commit");
        bool refused = false;
        try { BodyReplacementContext.Begin(game, next); IsbNotifierCompat.Prepare(game, next); } catch (InvalidOperationException) { refused = true; }
        Check(refused && Ended, "explicitly ended world rejects preparation"); BodyReplacementContext.CancelPreparation();

        game = Raid(); old = Prepare(game); next = Candidate(game); Fixture.Call("MarkLocalRaidPresentationEnded"); Publish(old, next); Check(Ended, "explicit end during creation prevents commit reopening");
        game = Raid(); old = Prepare(game); next = Candidate(game); Fixture.Call("ResetState"); Close(); Publish(old, next); Check(Ended && Fixture.ResetCount == 1, "native reset invalidates prepared ownership without adapter reset");

        game = Raid(); old = Prepare(game); next = Candidate(game); Enable(false); Publish(old, next); var oldWorld = game.GameWorld;
        game = Raid(); old = Prepare(game); next = Candidate(game); Enable(false); Publish(old, next); IsbNotifierCompat.RetireWorld(oldWorld); Enable(true); IsbNotifierCompat.Tick(); Check(!Ended, "late retirement of another world cannot discard new world's pending commit");
        Close(); old = Prepare(game); next = Candidate(game); Enable(false); Publish(old, next); IsbNotifierCompat.RetireWorld(game.GameWorld); Enable(true); IsbNotifierCompat.Tick(); Check(Ended, "world retirement cancels its own pending commit");
        Console.WriteLine("ISB_NOTIFIER_CASE_PASSED " + mode + " checks=" + _checks);
    }
}

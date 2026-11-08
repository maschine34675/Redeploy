using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using Mono.Cecil;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;
using SkillsExtended;
using SkillsExtended.Skills.Hacking;
using SkillsExtended.Skills.LockPicking;
using SkillsExtended.Skills.Shared.Patches;

internal static class Program
{
    private static int _checks;
    private static int _unpatchCalls;
    private static ActiveHealthController _failAfterAdd;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        _checks++;
    }

    private static void Published(string file)
    {
        using var published = AssemblyDefinition.ReadAssembly(file);
        using var fixture = AssemblyDefinition.ReadAssembly(typeof(Program).Assembly.Location);
        Check(published.Name.Name == fixture.Name.Name && published.Name.Version == fixture.Name.Version, "released assembly identity");
        foreach (var (type, fields, methods) in new[] {
            ("SkillsExtended.Skills.Shared.Patches.OnGameStartedPatch", new[] {"Player"}, new[] {"Postfix", "ApplyMedicalXp", "ApplyNatoRifleXp", "ApplyEasternRifleXp"}),
            ("SkillsExtended.Skills.Hacking.HackingView", new[] {"_player"}, new[] {"get_Current", "Abort"}),
            ("SkillsExtended.Skills.LockPicking.LockPickingGame", new[] {"_player"}, new[] {"get_Current", "Abort"}) })
        {
            var a = published.MainModule.GetType(type);
            var b = fixture.MainModule.GetType(type);
            foreach (string name in fields)
            {
                var left = a.Fields.Single(x => x.Name == name);
                var right = b.Fields.Single(x => x.Name == name);
                Check(left.FullName == right.FullName && left.IsStatic == right.IsStatic, "released field " + name);
            }
            foreach (string name in methods)
            {
                var left = a.Methods.Single(x => x.Name == name);
                var right = b.Methods.Single(x => x.Name == name);
                Check(left.FullName == right.FullName && left.IsStatic == right.IsStatic && left.HasBody, "released method " + name);
            }
        }
        var start = published.MainModule.GetType("SkillsExtended.Skills.Shared.Patches.OnGameStartedPatch").Methods.Single(m => m.Name == "Postfix");
        var calls = start.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>().ToArray();
        Check(calls.Count(m => m.Name == "add_OnMasteringExperienceChanged") == 2 && calls.Count(m => m.Name == "add_EffectStartedEvent") == 1,
            "upstream native subscription sites");
    }

    private static void Main(string[] args)
    {
        Published(args[0]);
        var probe = new Harmony("skills-body-checks.probe");
        probe.Patch(typeof(Harmony).GetMethod("UnpatchID", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static),
            prefix: new HarmonyMethod(typeof(Program), nameof(OnUnpatch)));
        new Harmony("com.maschine.Redeploy.Core.SkillsExtendedCompat").UnpatchSelf();
        Check(_unpatchCalls == 1, "positive global scan control");
        for (int i = 0; i < 1000; i++) Check(SkillsExtendedCompat.GetRejectionReason() == null, "absent");
        Check(_unpatchCalls == 1, "absent polls never unpatch");
        Chainloader.PluginInfos.Add(SkillsExtendedCompat.PluginGuid, new PluginInfo { Instance = new SkillsExtendedPlugin(),
            Metadata = new BepInPlugin(SkillsExtendedCompat.PluginGuid, "Skills Extended", "3.1.4") });
        Check(SkillsExtendedCompat.GetRejectionReason() == null, "binding: " + SkillsExtendedCompat.GetRejectionReason());
        Case(eastern: true);
        Case(eastern: false);
        EarlyStart(failed: true);
        EarlyStart(failed: false);
        Rollback(probe);
        Chainloader.PluginInfos["com.fika.core"] = new PluginInfo();
        Check(SkillsExtendedCompat.GetRejectionReason() == null, "Fika admission");
        Console.WriteLine("SKILLS_EXTENDED_BODY_CHECKS_PASSED " + _checks);
    }

    private static void OnUnpatch(string __0)
    {
        if (__0 == "com.maschine.Redeploy.Core.SkillsExtendedCompat") _unpatchCalls++;
    }

    private static void AfterAdd(ActiveHealthController __instance)
    {
        if (ReferenceEquals(__instance, _failAfterAdd))
        {
            _failAfterAdd = null;
            throw new InvalidOperationException("injected post-add failure");
        }
    }

    private static void Rollback(Harmony probe)
    {
        var world = new GameWorld();
        var old = new LocalPlayer { GameWorld = world };
        var game = new LocalGame { LocalPlayer = old, Profile = old.Profile, GameWorld = world };
        Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = world;
        world.RegisterPlayer(old); GamePlayerOwner.MyPlayer = old; OnGameStartedPatch.Start(old, true);
        var next = new LocalPlayer { GameWorld = world };
        BodyReplacementContext.Begin(game, old, next.Profile); SkillsExtendedCompat.Prepare(game, old);
        Wire(game, next);
        int foreign = 0;
        next.ActiveHealthController.EffectStartedEvent += _ => foreign++;
        probe.Patch(typeof(ActiveHealthController).GetEvent(nameof(ActiveHealthController.EffectStartedEvent)).GetAddMethod(),
            postfix: new HarmonyMethod(typeof(Program), nameof(AfterAdd)));
        _failAfterAdd = next.ActiveHealthController;
        RespawnApi.Publish(old, next);
        Check(_failAfterAdd == null && SkillsExtendedCompat.GetRejectionReason()?.Contains("injected") == true, "partial event failure reached");
        Check(ReferenceEquals(OnGameStartedPatch.Bound, old), "failed transfer preserves original static");
        BodyReplacementContext.EndAttempt(); SkillsExtendedCompat.RetireWorld(world); BodyReplacementContext.Reset();
        next.ActiveHealthController.Raise();
        Check(old.Medical == 0 && foreign == 1, "partial target changes rolled back without deleting other subscribers");
        old.ActiveHealthController.Raise(); Check(old.Medical == 1, "source subscription preserved");
    }

    private static void Case(bool eastern)
    {
        var world = new GameWorld();
        var old = new LocalPlayer {GameWorld = world};
        var game = new LocalGame { LocalPlayer = old, Profile = old.Profile, GameWorld = world };
        Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = world;
        GamePlayerOwner.MyPlayer = old; world.RegisterPlayer(old);
        var mastery = new Mastering();
        old.Skills.Mastering.Add(mastery.MasteringGroup.Id, mastery);
        OnGameStartedPatch.Start(old, eastern);
        int foreign = 0;
        old.Skills.OnMasteringExperienceChanged += _ => foreign++;
        old.Skills.Raise(mastery); old.ActiveHealthController.Raise();
        Check(old.Nato == 1 && old.Medical == 1 && foreign == 1, "ordinary XP");

        var hack = HackingView.Open(old);
        BodyReplacementContext.Begin(game, old);
        SkillsExtendedCompat.Prepare(game, old);
        SkillsExtendedCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        Check(!hack.Aborted, "preflight refusal preserves old minigame");
        // A reused upstream skill manager can already carry duplicate callbacks.
        OnGameStartedPatch.Start(old, eastern);
        BodyReplacementContext.Begin(game, old);
        SkillsExtendedCompat.Prepare(game, old);
        var practice = LockPickingGame.Open(null);
        bool refused = false;
        try { SkillsExtendedCompat.CompletePreparation(); } catch (InvalidOperationException) { refused = true; }
        Check(refused && !practice.Aborted && !hack.Aborted, "overlapping input owner keeps both views untouched");
        practice.Abort();
        SkillsExtendedCompat.CompletePreparation();
        Check(hack.Aborted, "only captured body view aborted");
        // Simulate death (old hands destroyed) and issue #136 while creation is pending.
        old.HandsController = null;
        old.Skills.Raise(mastery); old.ActiveHealthController.Raise();
        Check(old.Nato == 1 && old.Medical == 1 && foreign == 2, "old callbacks fenced during prepare");
        var pmc = new LocalPlayer { GameWorld = world, Profile = old.Profile, Skills = old.Skills };
        Wire(game, pmc);
        RespawnApi.Publish(old, pmc); BodyReplacementContext.EndAttempt();
        Check(ReferenceEquals(OnGameStartedPatch.Bound, pmc), "PMC cache");
        pmc.Skills.Raise(mastery); pmc.ActiveHealthController.Raise();
        Check(pmc.Nato == 1 && pmc.Eastern == (eastern ? 1 : 0) && pmc.Medical == 1 && foreign == 3, "exactly once/current/configured");
        old.ActiveHealthController.Raise(); OnGameStartedPatch.LateMedical(old.ActiveHealthController);
        Check(pmc.Medical == 1, "late old health event fenced");
        pmc.HandsController.Held = null; pmc.Skills.Raise(mastery);
        Check(pmc.Nato == 1 && foreign == 4, "empty item preserves damage chain");
        pmc.HandsController = null; pmc.Skills.Raise(mastery);
        Check(pmc.Nato == 1 && foreign == 5, "empty hands preserve damage chain");
        pmc.HandsController = new Hands();

        var picking = LockPickingGame.Open(pmc);
        BodyReplacementContext.Begin(game, pmc); SkillsExtendedCompat.Prepare(game, pmc);
        var superseding = LockPickingGame.Open(pmc);
        bool changed = false;
        try { SkillsExtendedCompat.CompletePreparation(); } catch (InvalidOperationException) { changed = true; }
        Check(changed && !picking.Aborted && !superseding.Aborted, "successor view retained on refusal");
        SkillsExtendedCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        BodyReplacementContext.Begin(game, pmc); SkillsExtendedCompat.Prepare(game, pmc);
        BodyReplacementContext.EndAttempt(); RespawnApi.Fail(pmc); SkillsExtendedCompat.Tick();
        pmc.Skills.Raise(mastery); Check(pmc.Nato == 2, "failed swap recovered old body");

        var scav = new LocalPlayer { GameWorld = world };
        var scavMastery = new Mastering(); scav.Skills.Mastering.Add(scavMastery.MasteringGroup.Id, scavMastery);
        BodyReplacementContext.Begin(game, pmc, scav.Profile); SkillsExtendedCompat.Prepare(game, pmc);
        SkillsExtendedCompat.CompletePreparation(); Check(superseding.Aborted, "owned picking abort");
        Wire(game, scav); BodyReplacementContext.EndAttempt(); RespawnApi.Fail(pmc); SkillsExtendedCompat.Tick();
        Check(ReferenceEquals(OnGameStartedPatch.Bound, scav), "Scav recovered body");
        pmc.Skills.Raise(mastery); scav.Skills.Raise(mastery); scav.Skills.Raise(scavMastery);
        scav.ActiveHealthController.Raise(); pmc.ActiveHealthController.Raise();
        Check(scav.Nato == 1 && scav.Eastern == (eastern ? 1 : 0) && scav.Medical == 1 && foreign == 7, "distinct profile transfer and stale mastery fenced");

        SkillsExtendedCompat.RetireWorld(new GameWorld()); scav.Skills.Raise(scavMastery);
        Check(scav.Nato == 2, "foreign retirement isolated");
        SkillsExtendedCompat.RetireWorld(world); BodyReplacementContext.Reset(); scav.Skills.Raise(scavMastery);
        Check(scav.Nato == 2, "retirement prevents duplicates in reused profile");
    }

    private static void Wire(LocalGame game, LocalPlayer next)
    {
        game.GameWorld.RegisterPlayer(next);
        Check(BodyReplacementContext.TryOwnCandidate(next), "exact candidate");
        game.LocalPlayer = next; game.Profile = next.Profile; GamePlayerOwner.MyPlayer = next;
    }

    private static void EarlyStart(bool failed)
    {
        var world = new GameWorld();
        var old = new LocalPlayer { GameWorld = world };
        var game = new LocalGame { LocalPlayer = old, Profile = old.Profile, GameWorld = world };
        Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = world;
        world.RegisterPlayer(old); GamePlayerOwner.MyPlayer = old;
        BodyReplacementContext.Begin(game, old); SkillsExtendedCompat.Prepare(game, old);
        var candidate = new LocalPlayer { GameWorld = world, Profile = old.Profile, Skills = old.Skills };
        if (failed)
        {
            world.RegisterPlayer(candidate);
            OnGameStartedPatch.Start(candidate, true);
            Check(BodyReplacementContext.OwnsBody(candidate), "upstream subscribed pre-validation candidate has physical lease");
            world.MainPlayer = old;
            BodyReplacementContext.EndAttempt(); RespawnApi.Fail(old); SkillsExtendedCompat.Tick();
        }
        else
        {
            Wire(game, candidate);
            RespawnApi.Publish(old, candidate); BodyReplacementContext.EndAttempt();
            OnGameStartedPatch.Start(candidate, true);
            OnGameStartedPatch.Start(candidate, true);
        }
        var survivor = failed ? old : candidate;
        var master = new Mastering();
        survivor.Skills.Mastering.Add(master.MasteringGroup.Id, master);
        survivor.Skills.Raise(master); survivor.ActiveHealthController.Raise();
        Check(ReferenceEquals(OnGameStartedPatch.Bound, survivor) && survivor.Medical == 1
            && survivor.Nato == 1 && survivor.Eastern == 1, "early startup binds exact surviving life once");
        var retired = failed ? candidate : old;
        retired.ActiveHealthController.Raise(); Check(survivor.Medical == 1, "startup candidate health removed");
        SkillsExtendedCompat.RetireWorld(world); BodyReplacementContext.Reset();
    }
}

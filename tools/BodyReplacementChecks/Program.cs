using ArenaMode.Compat;
using ArenaMode.Session;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using Redeploy.Core;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;
using Redeploy.Core.Engine;
using UnityEngine;
using System.Reflection;
using System.Reflection.Emit;

internal static class Program
{
    private static int _assertions;
    private static readonly (string Name, Action Check)[] Cases =
    [
        ("no-mod", NoMods),
        ("legacy-arena-conflict", LegacyArenaConflict),
        ("legacy-arena-inert", LegacyArenaInert),
        ("rebuilt-arena", RebuiltArena),
        ("admission", Admission),
        ("exact-begin", ExactBegin),
        ("prepare-order", PrepareOrder),
        ("cancel-first", CancelFirst),
        ("cancel-prior", CancelPrior),
        ("exact-candidate", ExactCandidate),
        ("profile-replacement", ProfileReplacement),
        ("created-fault", CreatedFault),
        ("stale-candidate", StaleCandidate),
        ("cached-ui", CachedUi),
        ("pump-current", PumpCurrent),
        ("pump-old", PumpOld),
        ("missed-end", MissedEnd),
        ("arena-scope", ArenaScope),
        ("registration", Registration),
        // The production update fault is deliberately sticky for this process.
        ("tick-fault", TickFault)
    ];

    private static int Main(string[] args)
    {
        var selected = args.Length == 0 ? Cases : args.Length == 2 && args[0] == "--case"
            ? Cases.Where(item => item.Name == args[1]).ToArray() : [];
        if (selected.Length == 0) { Console.Error.WriteLine("Usage: BodyReplacementChecks [--case <known-case>]"); return 2; }
        int passed = 0;
        foreach (var item in selected)
        {
            try
            {
                ResetFixture();
                item.Check();
                passed++;
                Console.WriteLine("PASS " + item.Name);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("BODY_REPLACEMENT_CHECK_FAILED " + item.Name + ": " + ex.Message);
                Console.Error.WriteLine(ex.StackTrace);
                return 1;
            }
        }
        Console.WriteLine($"BODY_REPLACEMENT_CHECKS_PASSED cases={passed} assertions={_assertions}");
        return 0;
    }

    private sealed record Raid(LocalGame Game, GameWorld World, LocalPlayer Body);
    private static Raid NewRaid(GameWorld world = null)
    {
        world ??= new GameWorld();
        var profile = new Profile();
        var body = new LocalPlayer { GameWorld = world, Profile = profile, IsYourPlayer = true };
        var game = new LocalGame { GameWorld = world, Profile = profile, LocalPlayer = body };
        world.MainPlayer = body;
        var raid = new Raid(game, world, body);
        Activate(raid);
        return raid;
    }
    private static void Activate(Raid raid)
    {
        Singleton<GameWorld>.Instance = raid.World;
        Singleton<AbstractGame>.Instance = raid.Game;
        Singleton<IBotGame>.Instance = raid.Game;
    }
    private static LocalPlayer Candidate(Raid raid)
        => new() { GameWorld = raid.World, Profile = raid.Game.Profile, IsYourPlayer = true };
    private static void Install(string guid = RecoilLegacyContract.PluginGuid) => Chainloader.PluginInfos[guid] = new PluginInfo { Instance = new MonoBehaviour() };
    private const string ArenaGuid = "com.maschine.ArenaMode";
    private static readonly (string Guid, string OldType)[] LegacyFamilies =
    [
        (RecoilLegacyContract.PluginGuid, "ArenaRecoilLegacyCompat"),
        (ContinuousLoadAmmoContract.PluginGuid, "ArenaContinuousLoadAmmoCompat"),
        (ContinuousHealingContract.PluginGuid, "ArenaContinuousHealingCompat")
    ];
    private static void InstallArena(params string[] adapterTypes)
    {
        // Independent assemblies are required: the production guard inspects the
        // loaded plugin instance's assembly, not arbitrary types in this harness.
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ArenaFixture" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
        var module = assembly.DefineDynamicModule("ArenaFixture");
        foreach (var name in adapterTypes) module.DefineType("ArenaMode.Compat." + name, TypeAttributes.Public).CreateType();
        var plugin = module.DefineType("ArenaMode.Plugin", TypeAttributes.Public, typeof(MonoBehaviour)).CreateType();
        Chainloader.PluginInfos[ArenaGuid] = new PluginInfo { Instance = (MonoBehaviour)Activator.CreateInstance(plugin) };
    }
    private static void ResetFixture()
    {
        ArenaMedicalBodyScope.Shutdown();
        Probes.Reset();
        RespawnApi.RaiseEnded();
        BodyReplacementCompatibility.EndAttempt();
        BodyReplacementContext.Reset();
        Probes.Reset();
        Chainloader.PluginInfos.Clear();
        CoreLog.Warnings.Clear();
        GameObject.Adds = 0;
        Singleton<GameWorld>.Instance = null;
        Singleton<AbstractGame>.Instance = null;
        Singleton<IBotGame>.Instance = null;
        ArenaSession.ActiveGame = null;
        ArenaSession.ActiveWorld = null;
    }
    private static void Check(bool condition, string message)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Same(object expected, object actual, string message) => Check(ReferenceEquals(expected, actual), message);
    private static void Throws(Action action, string fragment)
    {
        try { action(); }
        catch (InvalidOperationException ex) { Check(ex.Message.Contains(fragment, StringComparison.Ordinal), "Unexpected rejection: " + ex.Message); return; }
        throw new InvalidOperationException("Expected rejection containing: " + fragment);
    }
    private static LocalPlayer Replace(Raid raid, LocalPlayer oldBody = null)
    {
        BodyReplacementCompatibility.Prepare(raid.Game, oldBody ?? raid.Body);
        var fresh = Candidate(raid);
        raid.World.MainPlayer = fresh;
        BodyReplacementCompatibility.ValidateCreated(fresh);
        raid.Game.LocalPlayer = fresh;
        BodyReplacementCompatibility.EndAttempt();
        return fresh;
    }

    private static void NoMods()
    {
        var raid = NewRaid();
        BodyReplacementCompatibility.Prepare(raid.Game, raid.Body);
        Check(Probes.All.All(p => p.Checks == 1), "Absent-mod admission must still inspect every contract");
        Check(Probes.All.All(p => p.Preparations == 0), "Absent mods must not adopt adapter state");
        Same(null, BodyReplacementContext.CurrentGame, "Absent mods must not create a context");
        Check(!BodyReplacementContext.IsCreating && !BodyReplacementContext.OwnsBody(raid.Body), "Absent mods must not grant body ownership");
        Check(GameObject.Adds == 0, "Absent mods must not create an update pump");
        BodyReplacementCompatibility.ValidateCreated(null);
        BodyReplacementCompatibility.EndAttempt();
        BodyReplacementCompatibility.Prepare(null, null); // Original no-mod path never needs context validation.
        Check(GameObject.Adds == 0 && Probes.All.All(p => p.Preparations == 0), "No-mod repeat must stay inert");
        Check(RespawnApi.SubscriberCount == 1 && RaidLifecycle.SubscriberCount == 1, "Initialize must subscribe once");
    }

    private static void LegacyArenaConflict()
    {
        foreach (var family in LegacyFamilies)
        {
            ResetFixture();
            Install(family.Guid);
            InstallArena(family.OldType);
            var raid = NewRaid();
            Throws(() => BodyReplacementCompatibility.Prepare(raid.Game, raid.Body), "Update ArenaMode and its matching Redeploy.Core together");
            Check(Probes.All.All(p => p.Checks == 0 && p.Preparations == 0), "Legacy Arena conflict reached a Core adapter");
            Check(!BodyReplacementContext.OwnsBody(raid.Body) && !BodyReplacementContext.IsCreating, "Legacy Arena conflict leased a body");
            Same(null, BodyReplacementContext.CurrentWorld, "Legacy Arena conflict changed context");
            Check(GameObject.Adds == 0, "Legacy Arena conflict created a pump");
            Check(BodyReplacementCompatibility.CachedRejectionReason?.Contains("Update ArenaMode") == true, "Legacy Arena conflict did not reach admission cache");
        }
        foreach (var arena in new PluginInfo[] { null, new(), new() { Instance = new MonoBehaviour { Destroyed = true } } })
        {
            ResetFixture();
            Install();
            Chainloader.PluginInfos[ArenaGuid] = arena;
            Check(BodyReplacementCompatibility.GetRejectionReason()?.Contains("not finished loading") == true, "Unloaded Arena plugin was accepted before inspection");
            Check(Probes.All.All(p => p.Checks == 0), "Unloaded Arena plugin caused Core hook admission");
            InstallArena();
            Check(BodyReplacementCompatibility.GetRejectionReason() == null, "Ready rebuilt Arena did not recover load-order refusal");
            Check(Probes.All.All(p => p.Checks == 1), "Recovered Arena admission skipped adapter families");
        }
    }

    private static void LegacyArenaInert()
    {
        InstallArena(LegacyFamilies.Select(f => f.OldType).ToArray());
        var raid = NewRaid();
        BodyReplacementCompatibility.Prepare(raid.Game, raid.Body);
        Check(Probes.All.All(p => p.Checks == 1 && p.Preparations == 0), "Old Arena without targeted mods changed no-mod adapter behavior");
        Check(!BodyReplacementContext.OwnsBody(raid.Body) && GameObject.Adds == 0, "Old Arena without targeted mods changed no-mod ownership");
        Check(BodyReplacementCompatibility.CachedRejectionReason == null, "Old Arena without targeted mods was refused");
        Probes.Reset();
        Chainloader.PluginInfos[ArenaGuid] = new PluginInfo();
        BodyReplacementCompatibility.Prepare(null, null);
        Check(Probes.All.All(p => p.Checks == 1 && p.Preparations == 0), "No-mod admission unnecessarily depended on loaded Arena instance");
        for (int index = 0; index < LegacyFamilies.Length; index++)
        {
            ResetFixture();
            Install(LegacyFamilies[index].Guid);
            InstallArena(LegacyFamilies[(index + 1) % LegacyFamilies.Length].OldType);
            var compatible = NewRaid();
            var fresh = Replace(compatible);
            Check(BodyReplacementContext.OwnsBody(fresh), "An unrelated legacy adapter type blocked targeted family");
        }
    }

    private static void RebuiltArena()
    {
        foreach (var family in LegacyFamilies)
        {
            ResetFixture();
            Install(family.Guid);
            InstallArena("ArenaMedicalBodyScope");
            var raid = NewRaid();
            var fresh = Replace(raid);
            Check(Probes.All.All(p => p.Checks == 2 && p.Preparations == 1), "Rebuilt Arena did not reach all shared Core adapters");
            Check(BodyReplacementContext.OwnsBody(raid.Body) && BodyReplacementContext.OwnsBody(fresh), "Rebuilt Arena did not get exact Core ownership");
            Check(GameObject.Adds == 1 && BodyReplacementCompatibility.CachedRejectionReason == null, "Rebuilt Arena admission was not normal");
        }
    }

    private static void Admission()
    {
        Install();
        foreach (var refusing in Probes.All)
        {
            Probes.Reset();
            var raid = NewRaid();
            refusing.Rejection = refusing.Name + " invalid contract";
            Throws(() => BodyReplacementCompatibility.Prepare(raid.Game, raid.Body), refusing.Rejection);
            Check(Probes.All.All(p => p.Checks == 1), "An earlier rejection must not skip later contract checks");
            Check(Probes.All.All(p => p.Preparations == 0), "A rejected contract must refuse before adapter mutation");
            Same(null, BodyReplacementContext.CurrentGame, "A rejected contract must refuse before context mutation");
            Check(!BodyReplacementContext.OwnsBody(raid.Body) && GameObject.Adds == 0, "Refusal must not lease a body or create a pump");
        }
        foreach (var probe in Probes.All) probe.Rejection = probe.Name + " invalid contract";
        Check(BodyReplacementCompatibility.GetRejectionReason() == Probes.Recoil.Rejection, "Admission must return its first family rejection");
    }

    private static void ExactBegin()
    {
        var invalid = new (string Name, Action<Raid> Change)[]
        {
            ("foreign-profile-same-id", r => r.Body.Profile = new Profile { Id = r.Game.Profile.Id }),
            ("foreign-body-world", r => r.Body.GameWorld = new GameWorld()),
            ("foreign-game-player", r => r.Game.LocalPlayer = Candidate(r)),
            ("foreign-main-player", r => r.World.MainPlayer = Candidate(r)),
            ("not-local", r => r.Body.IsYourPlayer = false),
            ("foreign-world-singleton", _ => Singleton<GameWorld>.Instance = new GameWorld()),
            ("missing-world-singleton", _ => Singleton<GameWorld>.Instance = null),
            ("foreign-game-singleton", _ => Singleton<AbstractGame>.Instance = new LocalGame()),
            ("missing-game-singleton", _ => Singleton<AbstractGame>.Instance = null),
            ("destroyed-body", r => r.Body.Destroyed = true)
        };
        foreach (var item in invalid)
        {
            var raid = NewRaid();
            item.Change(raid);
            Throws(() => BodyReplacementContext.Begin(raid.Game, raid.Body), "could not verify");
            Check(!BodyReplacementContext.OwnsBody(raid.Body) && !BodyReplacementContext.IsCreating, item.Name + " acquired ownership");
        }
        var hideout = NewRaid(new HideoutGameWorld());
        Throws(() => BodyReplacementContext.Begin(hideout.Game, hideout.Body), "could not verify");
        Throws(() => BodyReplacementContext.Begin(null, null), "could not verify");
        var valid = NewRaid();
        BodyReplacementContext.Begin(valid.Game, valid.Body);
        foreach (var status in Enum.GetValues<GameStatus>())
        {
            valid.Game.Status = status;
            Check(BodyReplacementContext.OwnsRaid(valid.Game, valid.World) == (status is GameStatus.Starting or GameStatus.Started or GameStatus.Running), "Raid status scope: " + status);
        }
        valid.Game.Status = GameStatus.Running;
        Check(!BodyReplacementContext.OwnsRaid(new LocalGame(), valid.World), "Wrong game must not own a raid");
        Check(!BodyReplacementContext.OwnsRaid(valid.Game, new GameWorld()), "Wrong world must not own a raid");
        BodyReplacementContext.CancelPreparation();
    }

    private static void PrepareOrder()
    {
        foreach (var guid in new[] { RecoilLegacyContract.PluginGuid, ContinuousLoadAmmoContract.PluginGuid, ContinuousHealingContract.PluginGuid, FovFixContract.PluginGuid, ManimalAmmoContract.PluginGuid, MagazineCheckContract.PluginGuid, SimpleCrosshairContract.PluginGuid, RadarContract.PluginGuid, ImpactCueContract.PluginGuid, EnemyMarkersContract.PluginGuid, QuickCancelCompat.PluginGuid, PipDisablerContract.Guid, AmandsGraphicsCompat.PluginGuid, AiLimitContract.PluginGuid, HealingAutoCancelCompat.PluginGuid, QuestTrackerContract.PluginGuid, AudioIndicatorsContract.PluginGuid })
        {
            ResetFixture();
            Install(guid);
            var raid = NewRaid();
            var replacement = Replace(raid);
            Check(string.Join(",", Probes.Trace.Take(38)) == "recoil.check,ammo.check,healing.check,fov.check,manimal.check,magazineCheck.check,crosshair.check,radar.check,impactCue.check,enemyMarkers.check,quickCancel.check,pip.check,amands.check,aiLimit.check,healingAutoCancel.check,questTracker.check,audioIndicators.check,recoil.prepare,ammo.prepare,healing.prepare,fov.prepare,manimal.prepare,magazineCheck.prepare,crosshair.prepare,radar.prepare,impactCue.prepare,enemyMarkers.prepare,quickCancel.prepare,pip.prepare,amands.prepare,aiLimit.prepare,healingAutoCancel.prepare,questTracker.prepare,audioIndicators.prepare,recoil.complete,fov.complete,magazineCheck.complete,crosshair.complete", "Admission/adoption/complete order changed");
            Check(GameObject.Adds == 1 && raid.World.gameObject.GetComponent<BodyReplacementCompatPump>() != null, "Each optional family must get one world pump");
            Same(raid.World, raid.World.gameObject.GetComponent<BodyReplacementCompatPump>().World, "Pump must carry its exact world");
            Replace(raid, replacement);
            Check(GameObject.Adds == 1, "Repeated replacement must reuse its pump");
        }
    }

    private static void CancelFirst()
    {
        Install();
        foreach (var fault in Probes.All)
        {
            Probes.Reset();
            var raid = NewRaid();
            fault.ThrowPrepare = true;
            Throws(() => BodyReplacementCompatibility.Prepare(raid.Game, raid.Body), fault.Name + " prepare fault");
            Check(BodyReplacementContext.IsCreating && BodyReplacementContext.OwnsBody(raid.Body), "Prepare fault must be cancellable while provisional context exists");
            Check(GameObject.Adds == 0 && Probes.All.All(p => p.Completions == 0), "Faulting preparation must not complete or create pump");
            BodyReplacementCompatibility.CancelPreparation();
            Check(!BodyReplacementContext.OwnsBody(raid.Body), "Cancellation leaked first provisional body lease");
            Check(!BodyReplacementContext.IsCreating && BodyReplacementContext.PreviousBody == null, "Cancellation left an active attempt");
            Same(null, BodyReplacementContext.CurrentWorld, "Cancellation leaked first provisional world");
            Check(Probes.Recoil.Aborts == 1 && Probes.Ammo.Aborts == 1 && Probes.Fov.Aborts == 1 && Probes.Manimal.Aborts == 1 && Probes.MagazineCheck.Aborts == 1 && Probes.Crosshair.Aborts == 1 && Probes.Radar.Aborts == 1 && Probes.ImpactCue.Aborts == 1 && Probes.EnemyMarkers.Aborts == 1 && Probes.Pip.Aborts == 1 && Probes.Amands.Aborts == 1 && Probes.AiLimit.Aborts == 1 && Probes.HealingAutoCancel.Aborts == 1 && Probes.QuestTracker.Aborts == 1 && Probes.AudioIndicators.Aborts == 1 && Probes.QuickCancel.Aborts == 0, "Cancellation must abort every provisional adapter state");
            BodyReplacementCompatibility.ValidateCreated(null);
        }
    }

    private static void CancelPrior()
    {
        Install();
        var first = NewRaid();
        var successful = Replace(first);
        Probes.Healing.ThrowPrepare = true;
        Throws(() => BodyReplacementCompatibility.Prepare(first.Game, successful), "healing prepare fault");
        BodyReplacementCompatibility.CancelPreparation();
        Check(BodyReplacementContext.OwnsBody(first.Body) && BodyReplacementContext.OwnsBody(successful), "Cancellation erased earlier successful-body leases");
        Same(first.Game, BodyReplacementContext.CurrentGame, "Same-raid cancellation erased committed context");
        var other = NewRaid();
        Throws(() => BodyReplacementCompatibility.Prepare(other.Game, other.Body), "healing prepare fault");
        BodyReplacementCompatibility.CancelPreparation();
        Same(first.World, BodyReplacementContext.CurrentWorld, "Cancelled new-world attempt must restore prior world");
        Same(first.Game, BodyReplacementContext.CurrentGame, "Cancelled new-world attempt must restore prior game");
        Check(!BodyReplacementContext.OwnsBody(other.Body), "Cancellation retained another world's provisional lease");
        Check(BodyReplacementContext.OwnsBody(successful), "Cancellation lost prior committed body lease");
    }

    private static void ExactCandidate()
    {
        Install();
        var invalid = new (string Name, Action<Raid, LocalPlayer> Change)[]
        {
            ("foreign-profile-same-id", (r, p) => p.Profile = new Profile { Id = r.Body.Profile.Id }),
            ("foreign-game-profile", (r, _) => r.Game.Profile = new Profile { Id = r.Body.Profile.Id }),
            ("wrong-body-world", (_, p) => p.GameWorld = new GameWorld()),
            ("not-local", (_, p) => p.IsYourPlayer = false),
            ("destroyed", (_, p) => p.Destroyed = true),
            ("not-current-main", (r, _) => r.World.MainPlayer = Candidate(r)),
            ("third-game-player", (r, _) => r.Game.LocalPlayer = Candidate(r)),
            ("stopped-raid", (r, _) => r.Game.Status = GameStatus.Stopped),
            ("wrong-world-singleton", (_, _) => Singleton<GameWorld>.Instance = new GameWorld()),
            ("wrong-game-singleton", (_, _) => Singleton<AbstractGame>.Instance = new LocalGame())
        };
        foreach (var item in invalid)
        {
            var raid = NewRaid();
            BodyReplacementCompatibility.Prepare(raid.Game, raid.Body);
            var candidate = Candidate(raid);
            raid.World.MainPlayer = candidate;
            item.Change(raid, candidate);
            Check(!BodyReplacementContext.TryOwnCandidate(candidate), item.Name + " candidate was accepted");
            Check(!BodyReplacementContext.OwnsBody(candidate), item.Name + " candidate leaked ownership");
            Throws(() => BodyReplacementCompatibility.ValidateCreated(candidate), "no longer belongs");
            BodyReplacementCompatibility.EndAttempt();
        }
        var valid = NewRaid();
        BodyReplacementCompatibility.Prepare(valid.Game, valid.Body);
        Check(!BodyReplacementContext.TryOwnCandidate(null), "Null candidate accepted");
        Check(!BodyReplacementContext.TryOwnCandidate(valid.Body), "Previous body accepted as its own replacement");
        var bot = new Player { GameWorld = valid.World, Profile = valid.Body.Profile, IsYourPlayer = true };
        valid.World.MainPlayer = bot;
        Check(!BodyReplacementContext.TryOwnCandidate(bot), "Non-LocalPlayer candidate accepted");
        var current = Candidate(valid);
        valid.World.MainPlayer = current;
        Check(BodyReplacementContext.TryOwnCandidate(current), "Exact candidate was rejected while game still points to old body");
        valid.Game.LocalPlayer = current;
        Check(BodyReplacementContext.TryOwnCandidate(current), "Exact candidate was rejected after game rebind");
        Check(BodyReplacementContext.OwnsBody(current), "Exact candidate not leased");
        current.GameWorld = new GameWorld();
        Check(!BodyReplacementContext.OwnsBody(current), "Pooled body reused in another world retained authority");
        BodyReplacementCompatibility.EndAttempt();
    }

    // Own-Scav continuation: the engine creates the body with the replacement profile and commits
    // game.Profile only after validation, so adapters deciding during creation need the expected
    // profile from the context rather than the game's (Recoil Legacy and FOV Fix did not).
    private static void ProfileReplacement()
    {
        Install();
        var raid = NewRaid();
        var original = raid.Game.Profile;
        var scav = new Profile { Id = "replacement-profile-id" };
        BodyReplacementCompatibility.Prepare(raid.Game, raid.Body, scav);
        Same(scav, BodyReplacementContext.ExpectedProfile, "The context must expose the replacement profile of an own-Scav continuation");
        var keepsOldProfile = new LocalPlayer { GameWorld = raid.World, Profile = original, IsYourPlayer = true };
        raid.World.MainPlayer = keepsOldProfile;
        Check(!BodyReplacementContext.TryOwnCandidate(keepsOldProfile), "A body that kept the old profile was accepted as the Scav replacement");
        var unrelated = new LocalPlayer { GameWorld = raid.World, Profile = new Profile { Id = "replacement-profile-id" }, IsYourPlayer = true };
        raid.World.MainPlayer = unrelated;
        Check(!BodyReplacementContext.TryOwnCandidate(unrelated), "A same-ID impostor profile was accepted as the Scav replacement");
        var fresh = new LocalPlayer { GameWorld = raid.World, Profile = scav, IsYourPlayer = true };
        raid.World.MainPlayer = fresh;
        Same(original, raid.Game.Profile, "The game must still hold the old profile while the replacement is created");
        Check(BodyReplacementContext.TryOwnCandidate(fresh), "The replacement body carrying the replacement profile was rejected");
        BodyReplacementCompatibility.ValidateCreated(fresh);
        raid.Game.Profile = scav;
        raid.Game.LocalPlayer = fresh;
        Check(BodyReplacementContext.TryOwnCandidate(fresh) && BodyReplacementContext.OwnsBody(fresh), "The replacement body lost ownership after the profile commit");
        BodyReplacementCompatibility.EndAttempt();
        Same(null, BodyReplacementContext.ExpectedProfile, "The expected profile must not outlive the replacement attempt");

        ResetFixture();
        Install();
        var ordinary = NewRaid();
        BodyReplacementCompatibility.Prepare(ordinary.Game, ordinary.Body);
        Same(ordinary.Body.Profile, BodyReplacementContext.ExpectedProfile, "An ordinary respawn expects the old body's profile");
        BodyReplacementCompatibility.EndAttempt();
    }

    private static void CreatedFault()
    {
        Install();
        foreach (var fault in Probes.All)
        {
            Probes.Reset();
            var raid = NewRaid();
            BodyReplacementCompatibility.Prepare(raid.Game, raid.Body);
            var candidate = Candidate(raid);
            raid.World.MainPlayer = candidate;
            fault.Rejection = fault.Name + " construction fault";
            Throws(() => BodyReplacementCompatibility.ValidateCreated(candidate), fault.Rejection);
            Check(Probes.All.All(p => p.Checks == 2), "Post-construction validation skipped an adapter contract");
            Check(BodyReplacementContext.OwnsBody(candidate), "Rejected constructed body must remain fenced for delayed callbacks");
            BodyReplacementCompatibility.EndAttempt();
            Check(!BodyReplacementContext.IsCreating, "Construction failure did not end attempt");
        }
    }

    private static void StaleCandidate()
    {
        Install();
        var raid = NewRaid();
        var successful = Replace(raid);
        var late = Candidate(raid);
        raid.World.MainPlayer = late;
        Check(!BodyReplacementContext.TryOwnCandidate(late), "Late body acquired a closed attempt");
        Check(!BodyReplacementContext.OwnsBody(late), "Late body leaked a lease");
        successful.Destroyed = true;
        Check(BodyReplacementContext.OwnsBody(successful), "Unity-null retired body lost managed callback fence");
        Check(BodyReplacementContext.OwnsBody(raid.Body), "Original retired body lost callback fence");
    }

    private static void CachedUi()
    {
        Probes.Ammo.Rejection = "initial admission fault";
        Check(BodyReplacementCompatibility.GetRejectionReason() == Probes.Ammo.Rejection, "Admission did not populate UI cache");
        Probes.Ammo.Rejection = null;
        for (int i = 0; i < 100; i++) Check(BodyReplacementCompatibility.CachedRejectionReason == "initial admission fault", "UI cache refreshed outside admission");
        Check(Probes.All.All(p => p.Checks == 1), "Cached UI read must not refresh adapter contracts");
        Check(BodyReplacementCompatibility.GetRejectionReason() == null, "Fresh admission did not replace old UI fault");
        Check(Probes.All.All(p => p.Checks == 2), "Fresh admission must refresh every contract");
    }

    private static void PumpCurrent()
    {
        Install();
        var raid = NewRaid();
        var replacement = Replace(raid);
        raid.World.gameObject.Invoke<BodyReplacementCompatPump>("Update");
        Check(Probes.Ticking.All(p => p.Ticks == 1), "Current pump did not tick every adapter");
        raid.World.Destroyed = true;
        raid.World.gameObject.Invoke<BodyReplacementCompatPump>("OnDestroy");
        Same(null, BodyReplacementContext.CurrentWorld, "Destroyed current world retained context");
        Same(null, BodyReplacementContext.CurrentGame, "Destroyed current world retained game");
        Check(Probes.Recoil.Retired.Count == 1 && ReferenceEquals(Probes.Recoil.Retired[0], raid.World), "Disposal did not retire exact recoil world");
        Check(Probes.MagazineCheck.Retired.Count == 1 && ReferenceEquals(Probes.MagazineCheck.Retired[0], raid.World), "Disposal did not retire exact magazine-check world");
        foreach (var hud in new[] { Probes.Crosshair, Probes.Radar, Probes.ImpactCue, Probes.EnemyMarkers, Probes.QuickCancel, Probes.Pip, Probes.Amands, Probes.AiLimit, Probes.HealingAutoCancel, Probes.QuestTracker, Probes.AudioIndicators })
            Check(hud.Retired.Count == 1 && ReferenceEquals(hud.Retired[0], raid.World), hud.Name + " did not retire exact current world");
        Check(Probes.Ticking.All(p => p.Ticks == 2), "Disposal must flush controller cleanup immediately");
        Check(BodyReplacementContext.OwnsBody(replacement), "World retirement removed delayed-body fence");
        raid.World.gameObject.Invoke<BodyReplacementCompatPump>("Update");
        Check(Probes.Ticking.All(p => p.Ticks == 2), "Retired pump continued ticking");
    }

    private static void PumpOld()
    {
        Install();
        var old = NewRaid();
        Replace(old);
        var current = NewRaid();
        BodyReplacementCompatibility.Prepare(current.Game, current.Body);
        old.World.gameObject.Invoke<BodyReplacementCompatPump>("Update");
        Check(Probes.Ticking.All(p => p.Ticks == 0), "Late old pump ticked current adapter state");
        old.World.gameObject.Invoke<BodyReplacementCompatPump>("OnDestroy");
        Same(current.World, BodyReplacementContext.CurrentWorld, "Late old pump cleared new world's context");
        Check(BodyReplacementContext.IsCreating, "Late old pump ended new attempt");
        Check(Probes.Recoil.Retired.Count == 1 && ReferenceEquals(Probes.Recoil.Retired[0], old.World), "Late disposal retired wrong recoil world");
        Check(Probes.MagazineCheck.Retired.Count == 1 && ReferenceEquals(Probes.MagazineCheck.Retired[0], old.World), "Late disposal retired wrong magazine-check world");
        foreach (var hud in new[] { Probes.Crosshair, Probes.Radar, Probes.ImpactCue, Probes.EnemyMarkers, Probes.QuickCancel, Probes.Pip, Probes.Amands, Probes.AiLimit, Probes.HealingAutoCancel, Probes.QuestTracker, Probes.AudioIndicators })
            Check(hud.Retired.Count == 1 && ReferenceEquals(hud.Retired[0], old.World), hud.Name + " late disposal retired wrong world");
        Check(Probes.Ticking.All(p => p.Ticks == 0), "Late disposal flushed new controller state");
        var next = Candidate(current);
        current.World.MainPlayer = next;
        BodyReplacementCompatibility.ValidateCreated(next);
        Check(BodyReplacementContext.OwnsBody(next), "Late disposal invalidated new candidate validation");
        current.World.gameObject.Invoke<BodyReplacementCompatPump>("Update");
        Check(Probes.Ticking.All(p => p.Ticks == 1), "New pump stopped after old disposal");
    }

    private static void MissedEnd()
    {
        Install();
        var old = NewRaid();
        Replace(old);
        RaidLifecycle.RaiseStarted(old.World);
        Same(old.World, BodyReplacementContext.CurrentWorld, "Repeated same-world start retired active context");
        var ordinary = NewRaid();
        RaidLifecycle.RaiseStarted(ordinary.World);
        Same(null, BodyReplacementContext.CurrentWorld, "Missed-end fallback retained previous world");
        Check(Probes.Recoil.Retired.Count == 1 && ReferenceEquals(Probes.Recoil.Retired[0], old.World), "Missed-end fallback retired wrong world");
        Check(Probes.MagazineCheck.Retired.Count == 1 && ReferenceEquals(Probes.MagazineCheck.Retired[0], old.World), "Missed-end fallback retired wrong magazine-check world");
        foreach (var hud in new[] { Probes.Crosshair, Probes.Radar, Probes.ImpactCue, Probes.EnemyMarkers, Probes.QuickCancel, Probes.Pip, Probes.Amands, Probes.AiLimit, Probes.HealingAutoCancel, Probes.QuestTracker, Probes.AudioIndicators })
            Check(hud.Retired.Count == 1 && ReferenceEquals(hud.Retired[0], old.World), hud.Name + " missed-end retired wrong world");
        Check(Probes.Ticking.All(p => p.Ticks == 1), "Missed-end fallback skipped adapter cleanup");
        Check(!BodyReplacementContext.OwnsBody(ordinary.Body), "Ordinary second raid inherited body ownership");
        Check(ordinary.World.gameObject.GetComponent<BodyReplacementCompatPump>() == null, "Ordinary second raid received pump");
        Check(!BodyReplacementContext.OwnsRaid(ordinary.Game, ordinary.World), "Ordinary second raid inherited raid authority");
        old.World.gameObject.Invoke<BodyReplacementCompatPump>("OnDestroy");
        Check(!BodyReplacementContext.OwnsBody(ordinary.Body), "Late old cleanup adopted ordinary body");
        RespawnApi.RaiseEnded();
        Same(null, BodyReplacementContext.CurrentWorld, "Ordinary raid end recreated context");
        var replaced = NewRaid();
        Replace(replaced);
        RespawnApi.RaiseEnded();
        Same(null, BodyReplacementContext.CurrentWorld, "Normal RaidEnded did not clean context");
    }

    private static void ArenaScope()
    {
        var ordinary = NewRaid();
        var ordinaryBot = new Player { GameWorld = ordinary.World, Profile = new Profile() };
        Check(!BodyReplacementCompatibility.OwnsAdditionalHealingBody(ordinaryBot), "Core automatically claimed ordinary bot");
        ArenaMedicalBodyScope.Initialize();
        ArenaMedicalBodyScope.Initialize();
        Check(!BodyReplacementCompatibility.OwnsAdditionalHealingBody(null), "Null body acquired healing authority");
        Check(CoreLog.Warnings.Count == 0 && BodyReplacementCompatibility.GetRejectionReason() == null, "Null body faulted and quarantined the registered Arena scope");
        Check(!BodyReplacementCompatibility.OwnsAdditionalHealingBody(ordinaryBot), "Installed Arena scope claimed ordinary raid");
        var arena = NewRaid();
        ArenaSession.ActiveGame = arena.Game;
        ArenaSession.ActiveWorld = arena.World;
        var bot = new Player { GameWorld = arena.World, Profile = new Profile() };
        Check(!BodyReplacementCompatibility.OwnsAdditionalHealingBody(ordinaryBot), "Arena claimed unrelated old-world bot");
        Singleton<IBotGame>.Instance = new LocalGame();
        Check(!BodyReplacementCompatibility.OwnsAdditionalHealingBody(bot), "Arena accepted foreign IBotGame");
        Singleton<IBotGame>.Instance = arena.Game;
        Check(BodyReplacementCompatibility.OwnsAdditionalHealingBody(bot), "Explicit exact Arena bot was rejected");
        Check(BodyReplacementCompatibility.OwnsAdditionalHealingBody(arena.Body), "Explicit exact Arena local body was rejected");
        Check(!BodyReplacementContext.OwnsBody(bot) && !BodyReplacementContext.OwnsBody(arena.Body), "Healing registration extended Core recoil/ammo ownership");
        ArenaSession.ActiveGame = null;
        ArenaSession.ActiveWorld = null;
        bot.Destroyed = true;
        Check(BodyReplacementCompatibility.OwnsAdditionalHealingBody(bot), "Retired Arena bot lost delayed medical fence");
        Check(!BodyReplacementCompatibility.OwnsAdditionalHealingBody(new Player { GameWorld = arena.World }), "Inactive Arena claimed a new unrelated bot");
        bot.GameWorld = ordinary.World;
        Check(!BodyReplacementCompatibility.OwnsAdditionalHealingBody(bot), "Pooled Arena body retained lease across worlds");
        ArenaMedicalBodyScope.Shutdown();
        ArenaMedicalBodyScope.Shutdown();
        Check(!BodyReplacementCompatibility.OwnsAdditionalHealingBody(arena.Body), "Scope disposal retained Arena registration");
    }

    private static void Registration()
    {
        try { BodyReplacementCompatibility.RegisterHealingScope(null); throw new InvalidOperationException("Null registration was accepted"); }
        catch (ArgumentNullException) { _assertions++; }
        var body = NewRaid().Body;
        int faultCalls = 0;
        using var broken = BodyReplacementCompatibility.RegisterHealingScope(_ => { faultCalls++; throw new InvalidOperationException("consumer fault"); });
        using var good = BodyReplacementCompatibility.RegisterHealingScope(p => ReferenceEquals(p, body));
        Check(BodyReplacementCompatibility.OwnsAdditionalHealingBody(body), "A throwing scope blocked a valid independent scope");
        Check(BodyReplacementCompatibility.OwnsAdditionalHealingBody(body), "Valid scope lost subsequent ownership");
        Check(faultCalls == 1 && CoreLog.Warnings.Count == 1, "Broken scope must be quarantined and warned once");
        Check(BodyReplacementCompatibility.GetRejectionReason()?.Contains("consumer fault") == true, "Broken consumer did not block admission");
        Check(Probes.All.All(p => p.Checks == 1), "Consumer failure skipped optional contract refresh");
        broken.Dispose();
        broken.Dispose();
        Check(BodyReplacementCompatibility.GetRejectionReason() == null, "Disposed failed registration still blocks admission");
        good.Dispose();
        Check(!BodyReplacementCompatibility.OwnsAdditionalHealingBody(body), "Disposed successful scope still owns body");
        IDisposable self = null;
        self = BodyReplacementCompatibility.RegisterHealingScope(_ => { self.Dispose(); return false; });
        Check(!BodyReplacementCompatibility.OwnsAdditionalHealingBody(body), "Self-disposal during iteration was not isolated");
        Check(BodyReplacementCompatibility.GetRejectionReason() == null, "Self-disposal created a false scope failure");
    }

    private static void TickFault()
    {
        Probes.Ammo.ThrowTick = true;
        BodyReplacementCompatibility.Tick();
        Check(Probes.Recoil.Ticks == 1 && Probes.Ammo.Ticks == 1 && Probes.Healing.Ticks == 0, "Tick fault should stop current dispatch");
        Check(BodyReplacementCompatibility.CachedRejectionReason?.Contains("ammo tick fault") == true, "Tick fault missing from cached UI admission");
        BodyReplacementCompatibility.Tick();
        Check(Probes.Recoil.Ticks == 1 && Probes.Ammo.Ticks == 1, "Faulting tick loop continued");
        Check(CoreLog.Warnings.Count == 1, "Tick fault warning repeated");
        Check(BodyReplacementCompatibility.GetRejectionReason()?.Contains("ammo tick fault") == true, "Admission ignored stored tick fault");
        Check(Probes.All.All(p => p.Checks == 1), "Tick fault skipped contract refresh");
    }
}

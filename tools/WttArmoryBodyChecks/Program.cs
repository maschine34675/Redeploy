using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Animations;
using EFT.InventoryLogic;
using HarmonyLib;
using Redeploy.Core.Compat;
using Redeploy.Core.Api;
using WTTArmoryClient;
using WTTArmoryClient.Patches;

internal static class Program
{
    static int checks;
    static Plugin plugin;
    static void Check(bool value, string name)
    {
        if (!value)
            throw new Exception(name);
        checks++;
    }
    static (LocalGame game, LocalPlayer body) World()
    {
        var world = new GameWorld();
        var body = Body(world, new Profile(), true);
        var game = new LocalGame { GameWorld = world, LocalPlayer = body, Profile = body.Profile };
        world.MainPlayer = body;
        Singleton<GameWorld>.Instance = world;
        Singleton<AbstractGame>.Instance = game;
        return (game, body);
    }
    static LocalPlayer Body(GameWorld world, Profile profile, bool hyperburst)
    {
        var body = new LocalPlayer { GameWorld = world, Profile = profile };
        body.HandsController = new Player.FirearmController { _player = body, Weapon = new Weapon { TemplateId = hyperburst ? Plugin.GunIDs[0] : "ordinary" } };
        return body;
    }
    static ProceduralWeaponAnimation Pwa(Player body) => new() { _firearmController = (Player.FirearmController)body.HandsController };
    static void Dirty()
    {
        Plugin.GunHasHyperburst = true;
        Plugin.IsFiring = true;
        Plugin.ShotTimer = 7;
        Plugin.RecoilShotCount = 11;
        Plugin.ROFShotCount = 13;
    }
    static bool Clean() => !Plugin.IsFiring && Plugin.ShotTimer == 0 && Plugin.RecoilShotCount == 0 && Plugin.ROFShotCount == 0;
    static void Prepare(LocalGame game, LocalPlayer body, Profile replacement = null)
    {
        BodyReplacementContext.Begin(game, body, replacement);
        WttArmoryBodyCompatibility.Prepare(game, body);
    }
    static LocalPlayer Candidate(LocalGame game, LocalPlayer old, bool hyperburst = true, Profile replacement = null)
    {
        var next = Body(game.GameWorld, replacement ?? old.Profile, hyperburst);
        game.GameWorld.MainPlayer = next;
        Check(BodyReplacementContext.TryOwnCandidate(next), "candidate belongs to exact Core attempt");
        return next;
    }
    static void Finish()
    {
        WttArmoryBodyCompatibility.EndAttempt();
        BodyReplacementContext.EndAttempt();
    }
    static void End(LocalGame game)
    {
        WttArmoryBodyCompatibility.RetireWorld(game.GameWorld);
        BodyReplacementContext.Reset();
    }
    static void Normal()
    {
        var (game, old) = World();
        plugin.You = old;
        Dirty();
        ShootPatch.Run(Pwa(old));
        Check(Plugin.RecoilShotCount == 12, "ordinary initial body unchanged");
        Prepare(game, old);
        Check(plugin.You == old && Plugin.RecoilShotCount == 12 && Plugin.ShotTimer == 0, "read-only Prepare");
        WttArmoryBodyCompatibility.AbortPreparation();
        BodyReplacementContext.CancelPreparation();
        Check(typeof(WttArmoryBodyCompatibility).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) == null, "cancel releases first provisional game reference");
        Dirty();
        ShootPatch.Run(Pwa(old));
        Check(Plugin.RecoilShotCount == 12, "cancelled preparation unchanged");
        Prepare(game, old);
        Dirty();
        Finish();
        Check(plugin.You == old && Plugin.RecoilShotCount == 11, "failed creation preserves outgoing state");
        Prepare(game, old);
        var next = Candidate(game, old);
        Dirty();
        var other = Body(game.GameWorld, old.Profile, false);
        RespawnApi.Publish(old, other);
        Check(plugin.You == old && Plugin.RecoilShotCount == 11, "same-profile different body is not a replacement");
        RespawnApi.Publish(old, next);
        Check(plugin.You == old && Plugin.RecoilShotCount == 11, "uncommitted candidate leaves old state untouched");
        game.LocalPlayer = next;
        RespawnApi.Publish(old, next);
        Check(plugin.You == next && Clean(), "new body resets all outgoing burst counters");
        Check(Plugin.GunHasHyperburst, "new hyperburst weapon classification survives");
        Dirty();
        RespawnApi.Publish(old, next);
        Check(Plugin.RecoilShotCount == 11, "duplicate event preserves new burst");
        Finish();
        Check(Plugin.RecoilShotCount == 11, "EndAttempt does not reset successful new burst twice");
        Prepare(game, next);
        WttArmoryBodyCompatibility.AbortPreparation();
        BodyReplacementContext.CancelPreparation();
        Check(typeof(WttArmoryBodyCompatibility).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) == game && plugin.You == next, "cancel preserves previous committed game and player");
        ShootPatch.Run(Pwa(old));
        Check(Plugin.RecoilShotCount == 11 && Plugin.ROFShotCount == 13, "late old shoot callbacks preserve new burst");
        ((Player.FirearmController)old.HandsController).Weapon.TemplateId = "ordinary";
        UpdateWeaponVariablesPatch.Run(Pwa(old));
        Check(Plugin.GunHasHyperburst, "late old weapon callback preserves new classification");
        ShootPatch.Run(Pwa(next));
        Check(Plugin.RecoilShotCount == 12 && Plugin.ROFShotCount == 14, "new physical body callbacks remain active");
        ((Player.FirearmController)old.HandsController).autoFireOn = true;
        ((Player.FirearmController)next.HandsController).autoFireOn = false;
        plugin.Tick();
        Check(Clean(), "new trigger release resets burst despite retained firing old body");
        Prepare(game, next);
        var third = Candidate(game, next, false);
        game.LocalPlayer = third;
        Dirty();
        Finish();
        Check(plugin.You == third && Clean() && !Plugin.GunHasHyperburst, "failed rewire follows already committed ordinary weapon body");
        Dirty();
        RespawnApi.Publish(next, third);
        Check(Plugin.RecoilShotCount == 11, "late failed-attempt event cannot reset current burst");
        WttArmoryBodyCompatibility.RetireWorld(new GameWorld());
        Check(plugin.You == third && Plugin.RecoilShotCount == 11, "unrelated world retirement does not touch current state");
        // PMC-to-Scav / explicit replacement profile is still exact physical ownership.
        Prepare(game, third, new Profile());
        var newProfile = BodyReplacementContext.ExpectedProfile;
        var scav = Candidate(game, third, true, newProfile);
        game.Profile = newProfile;
        game.LocalPlayer = scav;
        RespawnApi.Publish(third, scav);
        Finish();
        Check(plugin.You == scav && Clean() && Plugin.GunHasHyperburst, "explicit replacement profile supported");
        Dirty();
        End(game);
        Check(plugin.You == null && Clean() && !Plugin.GunHasHyperburst, "owned world retirement releases player and transient state");
        ShootPatch.Run(Pwa(scav));
        Check(Clean(), "retired world late callback inert");
        var (ordinaryGame, ordinary) = World();
        plugin.You = ordinary;
        Dirty();
        ShootPatch.Run(Pwa(ordinary));
        Check(Plugin.RecoilShotCount == 12, "ordinary next raid remains unmodified");
        WttArmoryBodyCompatibility.RetireWorld(game.GameWorld);
        Check(plugin.You == ordinary && Plugin.RecoilShotCount == 12, "late old world retirement preserves ordinary next raid");
        // Cache already populated by Armory itself: retain classification but clear stale counters once.
        Prepare(ordinaryGame, ordinary);
        var fourth = Candidate(ordinaryGame, ordinary);
        ordinaryGame.LocalPlayer = fourth;
        plugin.You = fourth;
        Dirty();
        RespawnApi.Publish(ordinary, fourth);
        Finish();
        Check(plugin.You == fourth && Clean(), "already rebound candidate still retires outgoing counters");
        // No firearm on a correctly committed body is safe and cannot carry hyperburst across bodies.
        Prepare(ordinaryGame, fourth);
        var empty = Candidate(ordinaryGame, fourth);
        empty.HandsController = null;
        ordinaryGame.LocalPlayer = empty;
        Dirty();
        Finish();
        Check(plugin.You == empty && Clean() && !Plugin.GunHasHyperburst, "empty hands cannot inherit hyperburst classification");
        End(ordinaryGame);
    }
    static void Failure()
    {
        var (game, old) = World();
        plugin.You = old;
        Prepare(game, old);
        var next = Candidate(game, old);
        game.LocalPlayer = next;
        var foreign = Body(game.GameWorld, old.Profile, true);
        plugin.You = foreign;
        Dirty();
        Finish();
        Check(plugin.You == foreign && Plugin.RecoilShotCount == 11, "foreign cache owner preserved after committed failure");
        Check(WttArmoryBodyCompatibility.GetRejectionReason() != null && Redeploy.Core.CoreLog.Warnings == 1, "ownership failure refuses retries with one warning");
        WttArmoryBodyCompatibility.GetRejectionReason();
        Check(Redeploy.Core.CoreLog.Warnings == 1, "repeated admission does not spam");
    }
    static void Aborted()
    {
        var (game, old) = World();
        plugin.You = old;
        Prepare(game, old);
        var early = Body(game.GameWorld, old.Profile, false);
        game.GameWorld.MainPlayer = early;
        Dirty();
        UpdateWeaponVariablesPatch.Run(Pwa(early));
        Check(BodyReplacementContext.OwnsBody(early) && Plugin.GunHasHyperburst, "construction callback claims and fences exact candidate");
        game.GameWorld.MainPlayer = old;
        Finish();
        Dirty();
        ShootPatch.Run(Pwa(early));
        UpdateWeaponVariablesPatch.Run(Pwa(early));
        Check(Plugin.RecoilShotCount == 11 && Plugin.GunHasHyperburst, "failed uncommitted candidate late callbacks stay fenced");
        Prepare(game, old);
        var next = Candidate(game, old);
        Dirty();
        game.GameWorld.MainPlayer = old;
        Finish();
        Check(plugin.You == old && Plugin.RecoilShotCount == 11, "candidate rejected before commitment preserves old cache");
        ShootPatch.Run(Pwa(old));
        Check(Plugin.RecoilShotCount == 12, "restored outgoing body remains usable");
        Prepare(game, old);
        var final = Candidate(game, old);
        game.LocalPlayer = final;
        game.Status = GameStatus.Stopped;
        Dirty();
        Finish();
        Check(plugin.You == old && Plugin.RecoilShotCount == 11, "raid shutdown cannot publish a candidate");
        End(game);
        Check(plugin.You == old, "aborted provisional body is not claimed by world cleanup");
    }
    static void Destroyed()
    {
        var (game, old) = World();
        plugin.You = old;
        Prepare(game, old);
        var next = Candidate(game, old);
        game.LocalPlayer = next;
        RespawnApi.Publish(old, next);
        Finish();
        Prepare(game, next);
        next.Destroyed = true;
        Finish();
        Check(ReferenceEquals(typeof(WttArmoryBodyCompatibility).GetField("_active", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null), null),
            "destroyed committed body releases strong reference after failed attempt");
    }
    static void Published(string path)
    {
        using var dll = Mono.Cecil.AssemblyDefinition.ReadAssembly(path);
        Check(dll.Name.Name == "WTT-ArmoryClient", "release assembly name");
        var type = dll.MainModule.GetType("WTTArmoryClient.Plugin");
        var attr = type.CustomAttributes.Single(x => x.AttributeType.FullName == "BepInEx.BepInPlugin");
        Check((string)attr.ConstructorArguments[0].Value == "com.wtt.armory" && (string)attr.ConstructorArguments[2].Value == "2.0.5", "exact reviewed release identity");
        var contract = typeof(WttArmoryBodyCompatibility).GetNestedType("Contract", BindingFlags.NonPublic);
        var instance = Activator.CreateInstance(contract, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { typeof(Plugin) }, null);
        foreach (var field in contract.GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (field.GetValue(instance) is PropertyInfo property)
            {
                var actual = type.Properties.Single(x => x.Name == property.Name);
                Check(actual.PropertyType.FullName == property.PropertyType.FullName && actual.GetMethod.IsStatic == property.GetGetMethod().IsStatic && actual.SetMethod.IsStatic == property.GetSetMethod().IsStatic, "release property " + property.Name);
                Check(actual.GetMethod.IsPublic && actual.SetMethod.IsPublic, "release accessor visibility " + property.Name);
            }
            if (field.GetValue(instance) is FieldInfo f)
            {
                var actual = type.Fields.Single(x => x.Name == f.Name);
                Check(actual.FieldType.FullName == f.FieldType.FullName && actual.IsStatic && actual.IsPublic, "release field " + f.Name);
            }
        }
        foreach (var callback in (MethodInfo[])contract.GetField("Callbacks", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(instance))
        {
            var actual = dll.MainModule.GetType(callback.DeclaringType.FullName).Methods.Single(x => x.Name == callback.Name);
            Check(actual.IsStatic && actual.IsPrivate && actual.ReturnType.FullName == "System.Void" && actual.HasBody && actual.Parameters.Select(x => x.ParameterType.FullName).SequenceEqual(callback.GetParameters().Select(x => x.ParameterType.FullName)), "release callback " + callback.Name);
        }
        bool Calls(Mono.Cecil.MethodDefinition method, string name) => method.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.FullName.Contains(name));
        var update = type.Methods.Single(x => x.Name == "Update");
        var reset = type.Methods.Single(x => x.Name == "UpdateHyperburst");
        Check(Calls(update, "get_You") && Calls(update, "get_HandsController") && Calls(update, "UpdateHyperburst"), "installed Update uses cached body controller");
        Check(reset.Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "autoFireOn") && Calls(reset, "set_ShotTimer") && Calls(reset, "set_RecoilShotCount") && Calls(reset, "set_ROFShotCount"), "installed reset uses cached trigger and global counters");
        var variables = dll.MainModule.GetType("WTTArmoryClient.Patches.UpdateWeaponVariablesPatch").Methods.Single(x => x.Name == "PatchPostfix");
        Check(Calls(variables, "set_GunHasHyperburst") && !Calls(variables, "set_RecoilShotCount") && !Calls(variables, "set_ROFShotCount"), "installed weapon refresh does not reset counters");
    }
    static int Main(string[] args)
    {
        try
        {
            if (args[0] == "published")
            {
                Published(args[1]);
            }
            else
            {
                Check(WttArmoryBodyCompatibility.GetRejectionReason() == null, "absent plugin inert");
                Check(!Harmony.HasAnyPatches("com.maschine.Redeploy.Core.WttArmoryCompat"), "absent plugin installs no patches");
                plugin = new Plugin();
                Chainloader.PluginInfos[WttArmoryBodyCompatibility.PluginGuid] = new PluginInfo { Instance = plugin, Metadata = new BepInPlugin("com.wtt.armory", "fixture", args[0] == "older" ? "2.0.4" : "2.0.5") };
                if (args[0] == "older")
                {
                    Check(WttArmoryBodyCompatibility.GetRejectionReason()?.Contains("2.0.5 or newer") == true, "older contract refused");
                }
                else
                {
                    Check(WttArmoryBodyCompatibility.GetRejectionReason() == null, "matching contract admitted");
                    if (args[0] == "failure")
                        Failure();
                    else if (args[0] == "aborted")
                        Aborted();
                    else if (args[0] == "destroyed")
                        Destroyed();
                    else
                        Normal();
                }
            }
            Console.WriteLine("PASS " + args[0] + " " + checks + " assertions");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

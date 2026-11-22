using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;
using TarkovaCola.Client;

internal static class Program
{
    private static int _checks;
    private static void Check(bool result, string text) { _checks++; if (!result) throw new Exception(text); }
    private static void Install(string version = "4.0.0") => Chainloader.PluginInfos[TarkovaColaBodyCompatibility.PluginGuid] = new PluginInfo
        { Instance = new Plugin(), Metadata = new BepInPlugin(TarkovaColaBodyCompatibility.PluginGuid, "fixture", version) };
    private static LocalPlayer Body(GameWorld world, Profile profile) => new()
        { GameWorld = world, Profile = profile, HandsController = new Player.FirearmController { Item = new Weapon() } };
    private static (LocalGame Game, LocalPlayer Body) World()
    {
        var world = new GameWorld(); var body = Body(world, new Profile());
        var game = new LocalGame { GameWorld = world, LocalPlayer = body, Profile = body.Profile };
        world.MainPlayer = body; Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
        return (game, body);
    }
    private static void Prepare(LocalGame game, LocalPlayer body, Profile replacement = null)
    { BodyReplacementContext.Begin(game, body, replacement); TarkovaColaBodyCompatibility.Prepare(game, body); }
    private static LocalPlayer Candidate(LocalGame game)
    {
        var body = Body(game.GameWorld, BodyReplacementContext.ExpectedProfile);
        game.GameWorld.MainPlayer = body;
        return body;
    }
    private static void Finish() { TarkovaColaBodyCompatibility.EndAttempt(); BodyReplacementContext.EndAttempt(); }
    private static void End(LocalGame game) { TarkovaColaBodyCompatibility.RetireWorld(game.GameWorld); BodyReplacementContext.Reset(); }
    private static Item Dirty(LocalPlayer body)
    {
        var item = new Item();
        QuickRevive.Downed = true; QuickRevive._downedUntil = 1; QuickRevive._pistol = item; QuickRevive._tempPistol = item;
        QuickRevive._primary = item; QuickRevive._kit = new MedKitComponent(); QuickRevive._lethalInfo = new DamageInfo { Player = body };
        QuickRevive._reviveUsed = true; QuickRevive._pmcKills = 7;
        QuickRevive._locked.Add(EBodyPart.Head); QuickRevive._wasDestroyed.Add(EBodyPart.LeftArm); QuickRevive._bleeds.Add(new object());
        QuickRevive._botHit["bot"] = 5;
        MuleKick._cur = item; MuleKick._safetyDone = false; MuleKick._safetyHeld = true; MuleKick._safetyAt = 0;
        MuleKick.SlotIndex = 17; MuleKick._meters = 8; MuleKick._apexAt["old weapon"] = 11;
        ElectricCherry._reloading = true; ElectricCherry._bots[body.MovementContext] = 2;
        DoubleTap._hungryPending = 1; Tombstone.Specter = true; Tombstone._tombGo = new object();
        Tombstone._offeringUsed = true; Tombstone._readyAt = 150; Tombstone._botUsed.Add("bot");
        Perk.MyBreath = new object(); Tracker._hands = body.HandsController; Tracker._prevWeapon = (Weapon)((Player.FirearmController)body.HandsController).Item;
        Tracker.RaidProgress = 42; Tracker._deathDone = true;
        return item;
    }
    private static bool CleanBody(LocalPlayer body) => !body.Dead && body.ForcedItem == null && body.TriggerPresses == 0
        && body.ElectricDischarges == 0 && body.AmmoConsumed == 0 && body.SpecterEffects == 0;
    private static void Preserved()
    {
        Check(QuickRevive._reviveUsed && QuickRevive._pmcKills == 7 && QuickRevive._botHit["bot"] == 5, "revive charge and raid/bot counters preserved");
        Check(Tombstone._offeringUsed && Tombstone._readyAt == 150 && Tombstone._botUsed.Contains("bot"), "offering charge cooldown and bot effects preserved");
        Check(MuleKick.SlotIndex == 17 && MuleKick._meters == 8 && MuleKick._apexAt["old weapon"] == 11, "third slot and raid weapon cooldown preserved");
        Check(Perk.ActivePerks == 6 && PerkService._pendingOffering == "ordinary pending offering" && PerkService.Refunds == 13, "active perks and persistent requests unchanged");
        Check(Tracker.ReadyCalls == 0 && Tracker.RaidProgress == 42 && Tracker._deathDone, "no raid ready replay or progression reset");
    }
    private static void Normal()
    {
        var (game, old) = World(); var corpseItem = Dirty(old);
        Check(Util.IsMe(old), "ordinary player unchanged before Core ownership");
        Check(!DownedPronePatch.Prefix(old.MovementContext, false), "ordinary downed prone behavior retained");
        Prepare(game, old);
        Check(QuickRevive.Downed && ReferenceEquals(QuickRevive._tempPistol, corpseItem), "prepare does not mutate foreign state");
        TarkovaColaBodyCompatibility.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        Check(QuickRevive.Downed && Util.IsMe(old) && ReferenceEquals(QuickRevive._tempPistol, corpseItem), "cancelled preparation preserves old state and callbacks");
        Check(typeof(TarkovaColaBodyCompatibility).GetField("_game", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null) == null, "cancel releases provisional game");
        Prepare(game, old);
        var next = Candidate(game);
        int ticks = Tracker.Ticks;
        Tracker.Tick();
        Check(Tracker.Ticks == ticks && CleanBody(next), "construction cannot tick old state on new candidate");
        Check(DownedPronePatch.Prefix(next.MovementContext, false), "construction does not inherit downed prone guard");
        RespawnApi.Publish(old, next);
        Check(QuickRevive.Downed && ReferenceEquals(QuickRevive._tempPistol, corpseItem), "uncommitted event preserves outgoing state");
        var impostor = Body(game.GameWorld, old.Profile); RespawnApi.Publish(old, impostor);
        Check(QuickRevive.Downed, "same profile is insufficient replacement authority");
        game.LocalPlayer = next; RespawnApi.Publish(old, next);
        Check(!QuickRevive.Downed && QuickRevive._tempPistol == null && QuickRevive._pistol == null && QuickRevive._primary == null
            && QuickRevive._kit == null && QuickRevive._lethalInfo.Player == null, "outgoing health and item references retired");
        Check(QuickRevive._locked.Count == 0 && QuickRevive._wasDestroyed.Count == 0 && QuickRevive._bleeds.Count == 0, "body limb and effect caches retired");
        Check(corpseItem.Slot.Removed == 0, "retirement never removes old physical inventory");
        Finish(); Tracker.Tick();
        Check(CleanBody(next), "successor cannot inherit lethal timer trigger reload discharge or ammo debt");
        Check(next.PhysicalRefreshes == 1, "replacement physical perk cache refreshed once");
        Check(!Util.IsMe(old) && Util.IsMe(next), "late retained old body is fenced and new body remains active");
        Preserved();
        // Duplicate and delayed events must not erase current-body state.
        QuickRevive.Downed = true; QuickRevive._downedUntil = 999;
        RespawnApi.Publish(old, next); Finish();
        Check(QuickRevive.Downed && next.PhysicalRefreshes == 1, "duplicate event cannot reset successor state");
        Prepare(game, next); TarkovaColaBodyCompatibility.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        Check(QuickRevive.Downed && Util.IsMe(next), "cancel after prior success retains active body state");
        // A new profile (PMC-to-Scav) still follows physical commitment, not profile ID equality.
        var newProfile = new Profile(); Prepare(game, next, newProfile); var scav = Candidate(game);
        Check(BodyReplacementContext.TryOwnCandidate(scav), "explicit replacement profile candidate owned");
        game.Profile = newProfile; game.LocalPlayer = scav;
        Dirty(next); Finish(); Tracker.Tick();
        Check(CleanBody(scav) && scav.PhysicalRefreshes == 1, "failed rewire adopts exact committed replacement profile body");
        Check(!Util.IsMe(next) && Util.IsMe(scav), "second replacement retires prior current body");
        Preserved();
        TarkovaColaBodyCompatibility.RetireWorld(new GameWorld());
        Check(Util.IsMe(scav), "unrelated world retirement has no effect");
        Dirty(scav); game.Status = GameStatus.Stopped; End(game);
        Check(QuickRevive._tempPistol == null && Tracker._hands == null && MuleKick._cur == null, "owned world retirement releases physical references");
        Check(!Util.IsMe(scav), "late callback after world retirement remains fenced");
        var (ordinaryGame, ordinary) = World(); var ordinaryItem = Dirty(ordinary);
        TarkovaColaBodyCompatibility.RetireWorld(game.GameWorld);
        Check(QuickRevive.Downed && QuickRevive._tempPistol == ordinaryItem && Util.IsMe(ordinary), "next ordinary raid survives late old retirement");
        Check(!DownedPronePatch.Prefix(ordinary.MovementContext, false), "next ordinary raid retains native downed guard");
        // A new ordinary world may become visible before the previous raid-end callback.
        Prepare(ordinaryGame, ordinary);
        var (laterGame, later) = World();
        int beforeLaterTick = Tracker.Ticks;
        Tracker.Tick();
        Check(Tracker.Ticks == beforeLaterTick + 1, "new ordinary world is not gated by stale replacement context");
        TarkovaColaBodyCompatibility.AbortPreparation(); BodyReplacementContext.CancelPreparation();
    }
    private static void FailedCandidate()
    {
        var (game, old) = World(); var item = Dirty(old); Prepare(game, old);
        var next = Candidate(game); Tracker.Tick(); // Claims only the exact local factory candidate.
        Check(BodyReplacementContext.OwnsBody(next), "failed candidate is weakly owned before validation");
        Finish(); int before = Tracker.Ticks; Tracker.Tick();
        Check(Tracker.Ticks == before && CleanBody(next), "uncommitted failed candidate remains fenced after attempt");
        Check(QuickRevive.Downed && QuickRevive._tempPistol == item && item.Slot.Removed == 0, "failed creation preserves outgoing perk and inventory state");
        game.GameWorld.MainPlayer = old;
        Check(Util.IsMe(old) && !Util.IsMe(next), "recovered old owner remains active and candidate stays retired");
        End(game);
    }
    private static void FailedContract()
    {
        var target = typeof(Util).GetMethod("IsMe", BindingFlags.Static | BindingFlags.NonPublic);
        new Harmony("com.maschine.Redeploy.Core.TarkovaColaCompat").Unpatch(target, HarmonyPatchType.Prefix, "com.maschine.Redeploy.Core.TarkovaColaCompat");
        Check(TarkovaColaBodyCompatibility.GetRejectionReason()?.Contains("hook is missing") == true, "missing mandatory hook rejects replacement");
    }
    private static void FailedCommit()
    {
        var (game, old) = World(); Dirty(old); Prepare(game, old);
        var next = Candidate(game); Check(BodyReplacementContext.TryOwnCandidate(next), "commit failure candidate owned");
        next.ThrowOnRefresh = true; game.LocalPlayer = next; RespawnApi.Publish(old, next); Finish();
        Dirty(old); int before = Tracker.Ticks; Tracker.Tick();
        Check(Tracker.Ticks == before && CleanBody(next), "failed commit keeps foreign transient tick fenced");
        Check(!Util.IsMe(old) && !Util.IsMe(next), "failed commit cannot admit old or unbound new local callbacks");
        Check(TarkovaColaBodyCompatibility.GetRejectionReason()?.Contains("fixture refresh failure") == true, "failed commit remains visible at next admission");
        End(game);
    }
    private static void MissingHookAfterCommit()
    {
        var (game, old) = World(); Dirty(old); Prepare(game, old); var next = Candidate(game);
        Check(BodyReplacementContext.TryOwnCandidate(next), "hook failure candidate owned");
        game.LocalPlayer = next; RespawnApi.Publish(old, next); Finish();
        var target = typeof(Util).GetMethod("IsMe", BindingFlags.Static | BindingFlags.NonPublic);
        new Harmony("com.maschine.Redeploy.Core.TarkovaColaCompat").Unpatch(target, HarmonyPatchType.Prefix, "com.maschine.Redeploy.Core.TarkovaColaCompat");
        Check(TarkovaColaBodyCompatibility.GetRejectionReason()?.Contains("hook is missing") == true, "post-commit hook loss rejects next replacement");
        Dirty(old); int before = Tracker.Ticks; Tracker.Tick();
        Check(Tracker.Ticks == before && CleanBody(next), "post-commit hook loss retains surviving transient fence");
        Check(Harmony.GetPatchInfo(typeof(Tracker).GetMethod("Tick", BindingFlags.Static | BindingFlags.NonPublic))?.Prefixes
            .Any(p => p.owner == "com.maschine.Redeploy.Core.TarkovaColaCompat") == true, "post-commit admission failure keeps remaining hooks");
        End(game);
    }
    private static void Published(string path)
    {
        // Compare production-resolved declarations with the exact distributed DLL without loading its dependencies.
        using var dll = Mono.Cecil.AssemblyDefinition.ReadAssembly(path);
        var plugin = dll.MainModule.GetType("TarkovaCola.Client.Plugin");
        var identity = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        Check((string)identity.ConstructorArguments[0].Value == TarkovaColaBodyCompatibility.PluginGuid
            && (string)identity.ConstructorArguments[2].Value == "4.0.0", "published plugin identity and runtime version");
        var contract = new TarkovaColaBodyContract(typeof(Plugin).Assembly);
        var fields = new HashSet<FieldInfo>();
        foreach (var f in typeof(TarkovaColaBodyContract).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            var value = f.GetValue(contract);
            if (value is FieldInfo field) fields.Add(field);
            if (value is List<(FieldInfo Field, object Value)> list) foreach (var entry in list) fields.Add(entry.Field);
        }
        foreach (var expected in fields)
        {
            var actual = dll.MainModule.GetType(expected.DeclaringType.FullName).Fields.Single(f => f.Name == expected.Name);
            Check(actual.IsStatic && actual.FieldType.FullName == CecilType(expected.FieldType), "published field " + expected.DeclaringType.Name + "." + expected.Name);
        }
        var calls = new[] { ("Tracker", "Tick"), ("Util", "IsMe"), ("DownedPronePatch", "Prefix"), ("Tombstone", "DestroyTomb"), ("PerkFx", "RefreshPhysical") };
        foreach (var (name, method) in calls)
        {
            var expected = typeof(Plugin).Assembly.GetType("TarkovaCola.Client." + name).GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var actual = dll.MainModule.GetType(expected.DeclaringType.FullName).Methods.Single(m => m.Name == method);
            Check(actual.IsStatic && actual.HasBody && actual.ReturnType.FullName == CecilType(expected.ReturnType)
                && actual.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(expected.GetParameters().Select(p => CecilType(p.ParameterType))), "published method " + name + "." + method);
        }
    }
    private static string CecilType(Type type) => type.IsGenericType ? type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(CecilType)) + ">" : type.FullName;
    private static int Main(string[] args)
    {
        try
        {
            var mode = args.Length == 0 ? "normal" : args[0];
            if (mode == "published") Published(args[1]);
            else
            {
                Check(TarkovaColaBodyCompatibility.GetRejectionReason() == null, "absent plugin remains inert");
                Check(!Harmony.HasAnyPatches("com.maschine.Redeploy.Core.TarkovaColaCompat"), "absent plugin installs no patches");
                Install(mode == "older" ? "3.0.1" : "4.0.0");
                Check(TarkovaColaBodyCompatibility.GetRejectionReason() == null, "reviewed contract admitted");
                if (mode == "older") Check(!Harmony.HasAnyPatches("com.maschine.Redeploy.Core.TarkovaColaCompat"), "older supported family remains on original path");
                else if (mode == "failed-candidate") FailedCandidate();
                else if (mode == "failed-contract") FailedContract();
                else if (mode == "failed-commit") FailedCommit();
                else if (mode == "post-commit-hook-loss") MissingHookAfterCommit();
                else Normal();
            }
            Console.WriteLine("PASS " + args[0] + " " + _checks + " assertions"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

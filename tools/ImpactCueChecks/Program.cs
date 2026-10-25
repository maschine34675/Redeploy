using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using Dizzlow.ImpactCue;
using EFT;
using EFT.Vehicle;
using HarmonyLib;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;
using Hud = Dizzlow.ImpactCue.HudTelemetryRuntime;
using Combat = Dizzlow.ImpactCue.CombatIntrospection;
using Version = System.Version;

internal static class Program
{
    private static int _checks;
    private static LocalGame Game => (LocalGame)Singleton<AbstractGame>.Instance;
    private static GameWorld World => Singleton<GameWorld>.Instance;
    private static MethodInfo Method(Type type, string name) => type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    private static void Check(bool ok, string name) { _checks++; if (!ok) throw new Exception("ASSERT: " + name); }
    private static void Throws(Action action, string name)
    {
        try { action(); } catch (Exception) { Check(true, name); return; }
        throw new Exception("Expected rejection: " + name);
    }
    // Execute the actual production-registered hooks and real prefix/result semantics.
    // Unity/EFT and the foreign implementation are bounded stand-ins; no native claim.
    private static object Call(Type type, string name, object instance = null, params object[] args)
    {
        var reflected = Method(type, name);
        // Harmony intercepts the actual method body. Reflection through a leaf
        // class can produce a different MethodInfo wrapper for that same body.
        // Resolve by its runtime handle, independently of production's lookup.
        var method = (MethodInfo)MethodBase.GetMethodFromHandle(reflected.MethodHandle, reflected.DeclaringType.TypeHandle);
        var patches = Harmony.GetPatchInfo(method); bool run = true; object result = null;
        object Hook(MethodInfo hook)
        {
            var parameters = hook.GetParameters(); var inputs = new object[parameters.Length];
            for (int i = 0; i < inputs.Length; i++)
                inputs[i] = parameters[i].Name switch { "__instance" => instance, "__result" => result, _ => args[int.Parse(parameters[i].Name[2..])] };
            var output = hook.Invoke(null, inputs);
            for (int i = 0; i < inputs.Length; i++) if (parameters[i].Name == "__result") result = inputs[i];
            return output;
        }
        if (patches != null) foreach (var p in patches.Prefixes.OrderByDescending(p => p.priority))
        {
            var output = Hook(p.PatchMethod);
            if (p.PatchMethod.ReturnType == typeof(bool) && !(bool)output) run = false;
        }
        if (run) result = method.Invoke(instance, args);
        if (patches != null) foreach (var p in patches.Postfixes.OrderByDescending(p => p.priority)) Hook(p.PatchMethod);
        return result;
    }
    private static bool Ready() => (bool)Call(typeof(Hud), "EnsureRaidReady", null, 10f);
    private static Player Local() => (Player)Call(typeof(Combat), "GetLocalPlayer");
    private static void Cache(Player body) => Call(typeof(Combat), "CacheLocalPlayer", null, body);
    private static bool Actor(Player body) => (bool)Call(typeof(Combat), "IsLocalActor", null, body);
    private static void Btr(Player body) => Call(typeof(Hud), "NotifyLocalBtrPassengerAttached", null, body, new BTRSide(), 0);
    private static LocalPlayer Enter(bool active = true)
    {
        if (World != null) ImpactCueCompat.RetireWorld(World);
        BodyReplacementContext.Reset();
        var world = new LocalGameWorld(); var body = new LocalPlayer { GameWorld = world };
        world.RegisterPlayer(body);
        Singleton<GameWorld>.Instance = world;
        Singleton<AbstractGame>.Instance = new LocalGame { GameWorld = world, LocalPlayer = body, Profile = body.Profile };
        GamePlayerOwner.MyPlayer = body;
        Hud._raidActive = active; Hud._raidWorld = active ? world : null; Hud._raidPlayer = active ? body : null;
        Combat._cachedLocalPlayer = body;
        return body;
    }
    private static void Prepare(LocalPlayer body) { BodyReplacementContext.Begin(Game, body); ImpactCueCompat.Prepare(Game, body); }
    private static void Start(LocalPlayer body) => Call(World.GetType(), "UnregisterPlayer", World, body);
    private static LocalPlayer Candidate(LocalPlayer body)
    {
        var next = new LocalPlayer { GameWorld = World, Profile = body.Profile, HealthValue = 62 };
        World.RegisterPlayer(next);
        Check(BodyReplacementContext.TryOwnCandidate(next), "Core candidate recorded");
        return next;
    }
    private static void Wire(LocalPlayer body) { Game.LocalPlayer = body; GamePlayerOwner.MyPlayer = body; }
    private static void Finish(LocalPlayer old, LocalPlayer body)
    {
        Wire(body); RespawnApi.Publish(old, body); BodyReplacementContext.EndAttempt(); ImpactCueCompat.Tick();
    }
    private static void Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Pass the reviewed ImpactCue 2.0.3 client DLL path.");
        PublishedMetadata.Verify(args[0], Check);
        var ordinary = Enter();
        Check(ImpactCueCompat.GetRejectionReason() == null && Harmony.Bound.Count == 0, "absent plugin does not install hooks");
        ImpactCueCompat.Prepare(Game, ordinary); ImpactCueCompat.Tick();
        Check(Hud._raidPlayer == ordinary && Hud.Kills == 7, "absent plugin leaves telemetry unchanged");

        var plugin = typeof(Plugin);
        Chainloader.PluginInfos[ImpactCueContract.PluginGuid] = new PluginInfo { Instance = new Plugin(), Metadata = plugin.GetCustomAttribute<BepInPlugin>() };
        Check(ImpactCueCompat.GetRejectionReason() == null, "reviewed fixture contract admitted");
        var inheritedUnregister = Method(typeof(LocalGameWorld), nameof(GameWorld.UnregisterPlayer));
        var declaredUnregister = Method(typeof(ClientGameWorld), nameof(GameWorld.UnregisterPlayer));
        Check(inheritedUnregister.ReflectedType == typeof(LocalGameWorld) && inheritedUnregister.DeclaringType == typeof(ClientGameWorld)
            && inheritedUnregister != declaredUnregister && inheritedUnregister.MethodHandle == declaredUnregister.MethodHandle,
            "three-level world exposes distinct inherited wrapper for the same method body");
        Throws(() => new Harmony("negative.inherited-target").Patch(inheritedUnregister), "patch fixture rejects inherited MethodInfo targets");
        Throws(() => ImpactCueContract.Resolve(plugin, ImpactCueContract.PluginGuid, new Version(2, 0, 2), out _), "older version refused");
        Throws(() => ImpactCueContract.Resolve(plugin, ImpactCueContract.PluginGuid, new Version(2, 0, 4), out _), "metadata disagreement refused");
        Check(Ready() && Local() == ordinary, "ordinary ready and local lookup unchanged");
        var unrelated = new LocalPlayer { GameWorld = World, Profile = ordinary.Profile };
        Cache(unrelated); Check(Combat._cachedLocalPlayer == unrelated, "ordinary physical cache behavior unchanged"); Cache(ordinary);
        Prepare(ordinary);
        Check(Harmony.Bound.Keys.Any(m => m.DeclaringType == typeof(ClientGameWorld) && m.ReflectedType == typeof(ClientGameWorld)
            && m.Name == nameof(GameWorld.UnregisterPlayer)), "production registers the declaring override without inherited wrapper");
        Call(typeof(GameWorld), "UnregisterPlayer", new GameWorld(), ordinary);
        Check(Ready(), "wrong world unregister grants no scope");
        ImpactCueCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        Check(Ready() && Local() == ordinary, "canceled preflight leaves old body and cache untouched");

        // The failing old path is asserted before exercising corrected behavior.
        ordinary.HealthController.IsAlive = false;
        var oldKills = Hud.Kills; var oldFeed = Hud.FeedEntries;
        World.RegisteredPlayers.Remove(ordinary); // Native override now returns before its base method.
        Prepare(ordinary); Start(ordinary);
        Check(!Ready() && Local() == null && !(bool)Call(typeof(Hud), "get_CanRenderTacticalHudNow"), "teardown suspends stale HUD without resetting raid");
        var next = Candidate(ordinary); Wire(next);
        Check(Hud.ReadyOriginal(10) && Hud._raidPlayer == ordinary, "negative control: retained corpse still passes upstream ready");
        Check(Actor(ordinary), "delayed old-body kill attribution stays local");
        Finish(ordinary, next);
        Check(Hud._raidPlayer == next && Local() == next && Hud._raidPlayer.HealthValue == 62, "death replacement rebinds physical telemetry");
        Check(Hud.Kills == oldKills && Hud.FeedEntries == oldFeed && Hud.Resets == 0, "raid counts and killfeed preserved");
        Check(Hud.Refreshes > 0 && Hud.QuestRebinds > 0 && Hud._cachedPhysical == null && Hud._nextBreathRefresh == 0
            && Hud._cachedWeapon == null && Hud._cachedMagazineInventoryController == null && Hud._cachedHandsItem == null,
            "seeded body caches invalidated and refresh armed");
        Cache(ordinary); Btr(ordinary); Call(typeof(Hud), "NotifyLocalBtrPassengerDetached", null, ordinary);
        Check(Actor(ordinary) && Local() == next && Combat._cachedLocalPlayer == next && Hud._raidPlayer == next, "late cache actor and BTR callbacks cannot steal current body");
        Btr(next); Check(Hud.BtrCallbacks == 1, "current body BTR callback preserved");

        Hud.Enabled = false; Hud.PreferCustomHud = false;
        Prepare(next); Start(next); var live = Candidate(next); Finish(next, live);
        Check(Hud._raidPlayer == live && !Hud.Enabled && !Hud.PreferCustomHud, "living swap rebinds without changing preferences");
        Check(Hud.Kills == oldKills && Hud.FeedEntries == oldFeed, "repeat swap preserves raid history");
        Game.Status = GameStatus.Stopping;
        Check(!Ready() && Local() == null, "stopping game is not authoritative");
        Game.Status = GameStatus.Running;
        var realOwner = GamePlayerOwner.MyPlayer; GamePlayerOwner.MyPlayer = unrelated;
        Check(!Ready() && Local() == null, "mismatched owner cannot authorize telemetry");
        GamePlayerOwner.MyPlayer = realOwner;
        Prepare(live); ImpactCueCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        Check(Local() == live && Ready(), "refused later preflight does not poison previous successful scope");

        Prepare(live); Start(live); BodyReplacementContext.EndAttempt(); ImpactCueCompat.Tick();
        Check(!Ready(), "failed attempt remains frozen while current body is unregistered");
        World.RegisterPlayer(live); Wire(live); ImpactCueCompat.Tick();
        Check(Ready() && Hud._raidPlayer == live && Local() == live, "failed replacement recovers exact rewired old body");
        Prepare(live); Start(live); var recovered = Candidate(live); Wire(recovered); BodyReplacementContext.EndAttempt(); ImpactCueCompat.Tick();
        Check(Hud._raidPlayer == recovered && Local() == recovered, "failed rewire recovers exact Core candidate without Replaced");

        var previousWorld = World;
        var initial = Enter(false); Prepare(initial); Start(initial); var early = Candidate(initial); Finish(initial, early);
        Check(!Hud._raidActive && Hud._raidPlayer == null && Hud.Resets == 0, "initial Arena swap does not force raid activation or reset");
        Check(Ready() && Hud._raidPlayer == early && Local() == early, "normal first activation uses replacement body");
        ImpactCueCompat.RetireWorld(previousWorld);
        Cache(recovered); Btr(recovered); Check(Local() == early && Hud._raidPlayer == early, "old world callbacks and late pump retirement cannot touch new world");
        previousWorld = World;
        var normal = Enter();
        Check(Ready() && Local() == normal, "new ordinary raid retains upstream lifecycle");
        Btr(early); Check(Hud._raidPlayer == normal, "tombstone outlives scope retirement");
        Btr(normal); Check(Hud._raidPlayer == normal, "unowned ordinary BTR callback preserved");
        early.Destroyed = true;
        Cache(early); Btr(early); Check(Local() == normal && Hud._raidPlayer == normal, "destroyed Unity wrapper retains tombstone protection");
        Hud._raidPlayer = unrelated;
        BodyReplacementContext.Begin(Game, normal);
        Throws(() => ImpactCueCompat.Prepare(Game, normal), "active foreign physical identity mismatch rejected before teardown");
        ImpactCueCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation(); Hud._raidPlayer = normal;
        var patch = Harmony.GetPatchInfo(Method(typeof(Hud), "EnsureRaidReady")); patch.Prefixes.Clear();
        Check(ImpactCueCompat.GetRejectionReason() != null, "missing installed hook refuses replacement");
        Check(ImpactCueCompat.GetRejectionReason() == null, "unowned failed admission can retry a repaired hook installation");
        Prepare(normal); Start(normal); var last = Candidate(normal); Finish(normal, last);
        int hooks = Harmony.Bound.Values.Sum(p => p.Prefixes.Count + p.Postfixes.Count);
        Chainloader.PluginInfos.Remove(ImpactCueContract.PluginGuid);
        Check(ImpactCueCompat.GetRejectionReason()?.Contains("disappeared") == true, "unexpected owned plugin disappearance refuses replacement");
        Check(Harmony.Bound.Values.Sum(p => p.Prefixes.Count + p.Postfixes.Count) == hooks, "hot disappearance retains existing old-body fences");
        Btr(normal); Check(Hud._raidPlayer == last, "retired callback remains fenced after plugin disappearance");
        Console.WriteLine($"IMPACT_CUE_CHECKS_PASSED ({_checks} assertions; production source + shipped DLL contract; no deployment)");
    }
}

using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;

public static class Program
{
    private static int _checks;
    private static Type _managerType, _movementType, _waypointType, _stuckType, _hardType;
    private static PropertyInfo _singleton;
    private static PluginInfo _info;
    private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static FieldInfo Field(object owner, string name) => owner.GetType().GetField(name, Fields);
    private static void Set(object owner, string name, object value) => Field(owner, name).SetValue(owner, value);
    private static object Get(object owner, string name) => Field(owner, name).GetValue(owner);
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); _checks++; }
    private static void Throws(Action action, string reason)
    {
        try { action(); } catch (InvalidOperationException) { _checks++; return; }
        throw new Exception(reason);
    }

    private static void Build(string mode)
    {
        var version = mode == "newer" ? "3.2.0" : mode == "older" ? "3.1.0" : "3.1.1";
        var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(mode == "assembly" ? "ForeignOrbit" : "ORBIT")
            { Version = new Version(version + ".28828") }, AssemblyBuilderAccess.Run);
        var module = asm.DefineDynamicModule("test");
        var plugin = module.DefineType("Orbit.Plugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.SetCustomAttribute(new CustomAttributeBuilder(typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }),
            new object[] { OrbitContract.PluginGuid, "ORBIT", version }));
        var pluginType = plugin.CreateType();
        var movement = module.DefineType("Orbit.Systems.MovementSystem", TypeAttributes.Public);
        var hard = movement.DefineNestedType("HardStuckRemediation", TypeAttributes.NestedPrivate);
        hard.DefineDefaultConstructor(MethodAttributes.Public);
        hard.DefineField(mode == "drift" ? "changedHumans" : "<humanPlayers>P", typeof(List<Player>), FieldAttributes.Private);
        var stuck = movement.DefineNestedType("StuckRemediation", TypeAttributes.NestedPrivate);
        stuck.DefineDefaultConstructor(MethodAttributes.Public);
        stuck.DefineField("_hardRemediation", hard, FieldAttributes.Private | FieldAttributes.InitOnly);
        movement.DefineField("_humanPlayers", typeof(List<Player>), FieldAttributes.Private | FieldAttributes.InitOnly);
        movement.DefineField("_stuckRemediation", stuck, FieldAttributes.Private | FieldAttributes.InitOnly);
        _movementType = movement.CreateType();
        _hardType = hard.CreateType();
        _stuckType = stuck.CreateType();
        var waypoints = module.DefineType("Orbit.Systems.WaypointSystem", TypeAttributes.Public);
        waypoints.DefineField("_humanPlayers", typeof(List<Player>), FieldAttributes.Private | FieldAttributes.InitOnly);
        _waypointType = waypoints.CreateType();
        var manager = module.DefineType("Orbit.Core.OrbitManager", TypeAttributes.Public);
        manager.DefineField("_botsController", typeof(BotsController), FieldAttributes.Private | FieldAttributes.InitOnly);
        manager.DefineField("MovementSystem", _movementType, FieldAttributes.Public | FieldAttributes.InitOnly);
        manager.DefineField("WaypointSystem", _waypointType, FieldAttributes.Public | FieldAttributes.InitOnly);
        manager.DefineField("UnrelatedState", typeof(object), FieldAttributes.Public | FieldAttributes.InitOnly);
        _managerType = manager.CreateType();
        _singleton = typeof(Singleton<>).MakeGenericType(_managerType).GetProperty("Instance");
        _info = new PluginInfo { Instance = (BaseUnityPlugin)Activator.CreateInstance(pluginType), Metadata = pluginType.GetCustomAttribute<BepInPlugin>() };
        if (mode == "guid") _info.Metadata = new BepInPlugin("foreign.orbit", "ORBIT", version);
        if (mode == "metadata") _info.Metadata = new BepInPlugin(OrbitContract.PluginGuid, "ORBIT", "3.1.2");
        Chainloader.PluginInfos[OrbitContract.PluginGuid] = _info;
    }

    private sealed class Raid
    {
        internal readonly GameWorld World = new();
        internal readonly LocalGame Game;
        internal readonly LocalPlayer Old, Other;
        internal readonly List<Player> Humans = new();
        internal readonly object BotState = new();
        internal object Manager, Movement, Waypoints, Hard;
        internal Raid(bool manager = true)
        {
            Old = new LocalPlayer { GameWorld = World, Position = 100 };
            Other = new LocalPlayer { GameWorld = World, Profile = Old.Profile, IsYourPlayer = false, Position = 500 };
            Game = new LocalGame { GameWorld = World, LocalPlayer = Old, Profile = Old.Profile };
            World.MainPlayer = Old; World.RegisteredPlayers.Add(Old); World.RegisteredPlayers.Add(Other);
            Singleton<GameWorld>.Instance = World; Singleton<AbstractGame>.Instance = Game; GamePlayerOwner.MyPlayer = Old;
            Humans.AddRange(new Player[] { Other, Old });
            _singleton.SetValue(null, null);
            if (manager) Attach();
        }
        internal void Attach()
        {
            Hard = Activator.CreateInstance(_hardType); Set(Hard, "<humanPlayers>P", Humans);
            var stuck = Activator.CreateInstance(_stuckType); Set(stuck, "_hardRemediation", Hard);
            Movement = Activator.CreateInstance(_movementType); Set(Movement, "_humanPlayers", Humans); Set(Movement, "_stuckRemediation", stuck);
            Waypoints = Activator.CreateInstance(_waypointType); Set(Waypoints, "_humanPlayers", Humans);
            Manager = Activator.CreateInstance(_managerType); Set(Manager, "_botsController", Game.BotsController);
            Set(Manager, "MovementSystem", Movement); Set(Manager, "WaypointSystem", Waypoints); Set(Manager, "UnrelatedState", BotState);
            _singleton.SetValue(null, Manager);
        }
        internal void Prepare(Profile nextProfile = null)
        { BodyReplacementContext.Begin(Game, (LocalPlayer)Game.LocalPlayer, nextProfile); OrbitCompat.Prepare(Game, (LocalPlayer)Game.LocalPlayer); }
        internal LocalPlayer Next(Profile profile = null, bool own = true)
        {
            var next = new LocalPlayer { GameWorld = World, Profile = profile ?? Game.Profile, Position = 10 };
            World.MainPlayer = next;
            if (own) Check(BodyReplacementContext.TryOwnCandidate(next), "Core authorizes exact candidate");
            Game.Profile = next.Profile; Game.LocalPlayer = next; GamePlayerOwner.MyPlayer = next;
            World.RegisteredPlayers.Remove(Old); World.RegisteredPlayers.Add(next);
            return next;
        }
        internal void Finish()
        {
            OrbitCompat.RetireWorld(World); BodyReplacementContext.Reset();
            Chainloader.PluginInfos.Clear(); OrbitCompat.GetRejectionReason(); Chainloader.PluginInfos[OrbitContract.PluginGuid] = _info;
        }
        internal void Untouched(string reason)
            => Check(Humans.Count == 2 && ReferenceEquals(Humans[0], Other) && ReferenceEquals(Humans[1], Old), reason);
        internal void OtherState()
        {
            Check(ReferenceEquals(Get(Movement, "_humanPlayers"), Humans) && ReferenceEquals(Get(Waypoints, "_humanPlayers"), Humans)
                && ReferenceEquals(Get(Hard, "<humanPlayers>P"), Humans), "all three native consumers retain the original list");
            Check(ReferenceEquals(Get(Manager, "UnrelatedState"), BotState) && ReferenceEquals(Humans[0], Other), "unrelated human and bot state preserved");
        }
    }

    // These model the released consumers' alive-body/position reads; release IL checks anchor those reads below.
    private static float Nearest(object owner, string field, float target)
        => ((List<Player>)Get(owner, field)).Where(p => p.HealthController.IsAlive).Min(p => Math.Abs(p.Position - target));

    private static void Behavior(string mode)
    {
        Check(OrbitCompat.GetRejectionReason() == null, "absent plugin no-op");
        OrbitCompat.Prepare(null, null); OrbitCompat.Tick(); OrbitCompat.RetireWorld(null);
        Build(mode);
        var issue = OrbitCompat.GetRejectionReason();
        if (mode != "normal" && mode != "newer")
        {
            Check(issue != null, "invalid contract rejected before teardown");
            Check(OrbitCompat.GetRejectionReason() == issue && Redeploy.Core.CoreLog.Warnings == 1, "bounded refusal warning");
            Check(ReferenceEquals(typeof(OrbitCompat).GetField("_plugin", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null), _info.Instance), "rejected shape cached by plugin identity");
            return;
        }
        Check(issue == null, "supported released API");
        Check(OrbitCompat.GetRejectionReason() == null && Redeploy.Core.CoreLog.Warnings == (mode == "newer" ? 1 : 0), "matching newer versions remain open with one warning");
        var raid = new Raid();
        RespawnApi.Publish(raid.Old, raid.Other); OrbitCompat.Tick(); raid.Untouched("ordinary raid untouched");
        Throws(() => OrbitCompat.Prepare(raid.Game, raid.Old), "preparation requires Core ownership");
        raid.Prepare(); raid.Untouched("Prepare has no foreign mutation"); OrbitCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        RespawnApi.Publish(raid.Old, raid.Other); raid.Untouched("cancelled preparation untouched");
        raid.Prepare(); var next = raid.Next(); raid.Old.HealthController.IsAlive = false;
        OrbitCompat.Tick(); raid.Untouched("candidate not adopted before publication");
        RespawnApi.Publish(raid.Other, next); raid.Untouched("same-profile foreign publication ignored");
        OrbitCompat.RetireWorld(new GameWorld()); RespawnApi.Publish(raid.Old, next);
        Check(ReferenceEquals(raid.Humans[1], next), "published replacement updates exact old physical slot");
        Check(Nearest(raid.Movement, "_humanPlayers", 12) == 2 && Nearest(raid.Waypoints, "_humanPlayers", 12) == 2
            && Nearest(raid.Hard, "<humanPlayers>P", 12) == 2, "movement convergence and hard rescue observe the new live position");
        raid.OtherState(); BodyReplacementContext.EndAttempt(); RespawnApi.Publish(raid.Old, raid.Other);
        Check(ReferenceEquals(raid.Humans[1], next), "late old publication ignored");
        raid.Prepare(); var third = raid.Next(); RespawnApi.Publish(next, third); BodyReplacementContext.EndAttempt();
        Check(ReferenceEquals(raid.Humans[1], third), "repeated same-profile replacement");
        var scav = new Profile { Id = "own-scav" }; raid.Prepare(scav); var scavBody = raid.Next(scav);
        RespawnApi.Publish(third, scavBody); BodyReplacementContext.EndAttempt();
        Check(ReferenceEquals(raid.Humans[1], scavBody) && scavBody.Profile.Id != raid.Old.Profile.Id, "own-Scav follows physical ownership");
        raid.OtherState(); raid.Finish();

        raid = new Raid(); raid.Prepare(); next = raid.Next(own: false); RespawnApi.Publish(raid.Old, next);
        raid.Untouched("unowned replacement publication cannot steal cache"); BodyReplacementContext.EndAttempt(); RespawnApi.Fail(raid.Old); OrbitCompat.Tick();
        raid.Untouched("unowned failure recovery cannot steal cache"); raid.Finish();
        raid = new Raid(); raid.Prepare(); next = raid.Next(); RespawnApi.Fail(raid.Old); OrbitCompat.Tick();
        raid.Untouched("failure cannot adopt while creating"); BodyReplacementContext.EndAttempt(); OrbitCompat.Tick();
        Check(ReferenceEquals(raid.Humans[1], next), "committed failure recovery repairs cache"); raid.OtherState(); raid.Finish();
        raid = new Raid(); raid.Prepare(); RespawnApi.Fail(raid.Old); BodyReplacementContext.EndAttempt(); OrbitCompat.Tick();
        raid.Untouched("creation failure leaves old cache untouched"); raid.Finish();

        raid = new Raid(); raid.Prepare(); next = raid.Next(); raid.World.RegisteredPlayers.Remove(next); RespawnApi.Publish(raid.Old, next);
        raid.Untouched("unregistered candidate ignored"); raid.World.RegisteredPlayers.Add(next); GamePlayerOwner.MyPlayer = raid.Old;
        RespawnApi.Publish(raid.Old, next); raid.Untouched("uncommitted owner ignored"); GamePlayerOwner.MyPlayer = next;
        next.HealthController.IsAlive = false; RespawnApi.Publish(raid.Old, next); raid.Untouched("dead candidate ignored"); next.HealthController.IsAlive = true;
        raid.Game.Status = GameStatus.Stopped; RespawnApi.Publish(raid.Old, next); raid.Untouched("stopped raid ignored"); raid.Finish();

        raid = new Raid(manager: false); raid.Prepare(); next = raid.Next(); raid.Attach(); RespawnApi.Publish(raid.Old, next);
        Check(ReferenceEquals(raid.Humans[1], next), "late native manager retains and repairs copied old list"); raid.OtherState(); raid.Finish();
        raid = new Raid(manager: false); raid.Prepare(); next = raid.Next(); raid.Humans.Remove(raid.Old); raid.Attach(); RespawnApi.Publish(raid.Old, next);
        Check(raid.Humans.Count == 2 && ReferenceEquals(raid.Humans[1], next), "manager initialized during teardown adds only committed local body"); raid.OtherState(); raid.Finish();
        raid = new Raid(manager: false); raid.Prepare(); next = raid.Next(); raid.Humans[1] = next; raid.Attach(); RespawnApi.Publish(raid.Old, next);
        Check(raid.Humans.Count == 2 && ReferenceEquals(raid.Humans[1], next), "native initialization with new body not duplicated"); raid.Finish();
        raid = new Raid(manager: false); raid.Prepare(); next = raid.Next(); RespawnApi.Publish(raid.Old, next);
        raid.Untouched("absent manager never synthesized"); raid.Finish();

        var former = new Raid(); former.Prepare(); var formerNext = former.Next(); former.Finish();
        raid = new Raid(); raid.Prepare(); next = raid.Next(); OrbitCompat.RetireWorld(former.World); RespawnApi.Publish(former.Old, formerNext); RespawnApi.Fail(former.Old);
        raid.Untouched("old world callbacks ignored"); RespawnApi.Publish(raid.Old, next);
        Check(ReferenceEquals(raid.Humans[1], next), "old world retirement preserves current preparation"); raid.Finish();
        raid = new Raid(); raid.Prepare(); next = raid.Next(); var foreign = new Raid(); RespawnApi.Publish(raid.Old, next); OrbitCompat.Tick();
        raid.Untouched("world transition fences old mutation"); foreign.Untouched("world transition preserves new raid"); raid.Finish(); foreign.Finish();

        foreach (var defect in new[] { "bots", "waypoint", "hard", "missing", "duplicate", "foreign", "empty" })
        {
            raid = new Raid();
            if (defect == "bots") Set(raid.Manager, "_botsController", new BotsController());
            if (defect == "waypoint") Set(raid.Waypoints, "_humanPlayers", new List<Player>(raid.Humans));
            if (defect == "hard") Set(raid.Hard, "<humanPlayers>P", new List<Player>(raid.Humans));
            if (defect == "missing") Set(raid.Movement, "_humanPlayers", null);
            if (defect == "duplicate") raid.Humans.Add(raid.Old);
            if (defect == "foreign") raid.Humans.Remove(raid.Old);
            if (defect == "empty") raid.Humans.Clear();
            var snapshot = raid.Humans.ToArray();
            Throws(() => raid.Prepare(), "invalid prepared cache rejected: " + defect);
            Check(raid.Humans.SequenceEqual(snapshot), "preparation refusal is read-only: " + defect); raid.Finish();
        }
        foreach (var defect in new[] { "manager", "hard", "list", "duplicate", "removed", "both" })
        {
            raid = new Raid(); raid.Prepare(); next = raid.Next();
            if (defect == "manager") raid.Attach();
            if (defect == "hard") Set(raid.Hard, "<humanPlayers>P", new List<Player>(raid.Humans));
            if (defect == "list")
            {
                var list = new List<Player>(raid.Humans); Set(raid.Movement, "_humanPlayers", list); Set(raid.Waypoints, "_humanPlayers", list); Set(raid.Hard, "<humanPlayers>P", list);
            }
            if (defect == "duplicate") raid.Humans.Add(raid.Old);
            if (defect == "removed") raid.Humans.Remove(raid.Old);
            if (defect == "both") raid.Humans.Add(next);
            var snapshot = raid.Humans.ToArray(); RespawnApi.Publish(raid.Old, next);
            Check(OrbitCompat.GetRejectionReason() != null && raid.Humans.SequenceEqual(snapshot), "runtime drift refuses without mutation: " + defect); raid.Finish();
        }
        raid = new Raid(); raid.Prepare(); next = raid.Next(); Chainloader.PluginInfos.Clear(); RespawnApi.Publish(raid.Old, next);
        raid.Untouched("plugin removal before publication is inert"); raid.Finish();
        raid = new Raid(); raid.Prepare(); next = raid.Next();
        var changedInfo = new PluginInfo { Instance = (BaseUnityPlugin)Activator.CreateInstance(_info.Instance.GetType()), Metadata = _info.Metadata };
        Chainloader.PluginInfos[OrbitContract.PluginGuid] = changedInfo; RespawnApi.Publish(raid.Old, next);
        raid.Untouched("changed plugin before publication is inert"); Check(OrbitCompat.GetRejectionReason() != null, "changed plugin refuses prepared operation"); raid.Finish();
    }

    private static void Published(string path)
    {
        using var dll = Mono.Cecil.AssemblyDefinition.ReadAssembly(path);
        Check(dll.Name.Name == "ORBIT" && dll.Name.Version == new Version(3, 1, 1, 28828), "released assembly identity including wildcard revision");
        var plugin = dll.MainModule.GetType("Orbit.Plugin");
        var metadata = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        Check(plugin.BaseType.FullName == "BepInEx.BaseUnityPlugin" && (string)metadata.ConstructorArguments[0].Value == OrbitContract.PluginGuid
            && (string)metadata.ConstructorArguments[2].Value == "3.1.1", "released plugin metadata");
        var manager = dll.MainModule.GetType("Orbit.Core.OrbitManager");
        var movement = dll.MainModule.GetType("Orbit.Systems.MovementSystem");
        var waypoints = dll.MainModule.GetType("Orbit.Systems.WaypointSystem");
        var stuck = movement.NestedTypes.Single(t => t.Name == "StuckRemediation");
        var hard = movement.NestedTypes.Single(t => t.Name == "HardStuckRemediation");
        void FieldContract(Mono.Cecil.TypeDefinition type, string name, string value, bool readOnly)
        {
            var field = type.Fields.Single(f => f.Name == name);
            Check(!field.IsStatic && field.IsInitOnly == readOnly && field.FieldType.FullName == value, "released field " + type.Name + "." + name);
        }
        const string humans = "System.Collections.Generic.List`1<EFT.Player>";
        FieldContract(manager, "_botsController", "EFT.BotsController", true);
        FieldContract(manager, "MovementSystem", movement.FullName, true); FieldContract(manager, "WaypointSystem", waypoints.FullName, true);
        FieldContract(movement, "_humanPlayers", humans, true); FieldContract(waypoints, "_humanPlayers", humans, true);
        FieldContract(movement, "_stuckRemediation", stuck.FullName, true); FieldContract(stuck, "_hardRemediation", hard.FullName, true);
        FieldContract(hard, "<humanPlayers>P", humans, false);
        bool Uses(Mono.Cecil.MethodDefinition method, string name) => method.HasBody && method.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MemberReference member && member.Name == name);
        var ctor = manager.Methods.Single(m => m.IsConstructor && !m.IsStatic);
        Check(Uses(ctor, "AllAlivePlayersList") && Uses(ctor, "get_IsAI"), "manager copies native human players at construction");
        Check(movement.Methods.Any(m => m.Name == "TeleportSafe" && Uses(m, "get_IsAlive") && Uses(m, "get_Position")), "released teleport safety consumes live physical positions");
        Check(movement.Methods.Any(m => m.Name == "IsDoorOccupied" && Uses(m, "_humanPlayers")), "released occupied-door checks use shared human cache");
        Check(waypoints.Methods.Any(m => Uses(m, "_humanPlayers") && Uses(m, "get_IsAlive") && Uses(m, "get_Position")), "released convergence consumes live physical positions");
        Check(hard.Methods.Any(m => Uses(m, "<humanPlayers>P") && Uses(m, "TeleportSafe")), "hard rescue retains its own reference to the original list");
        var init = dll.MainModule.GetType("Orbit.Patches.OrbitInitPatch").Methods.Single(m => m.Name == "Postfix");
        Check(Uses(init, "Create") && init.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.DeclaringType.FullName.Contains("Singleton`1<Orbit.Core.OrbitManager>")), "released manager singleton lifecycle");
        using var hash = SHA256.Create();
        Check(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "") == "008EFDB871A5696740E57A5CD5E32E6063591454E7EEDA78A16E0A4656735EDA", "reviewed release bytes");
    }

    public static int Main(string[] args)
    {
        try
        {
            var mode = args.Length == 0 ? "normal" : args[0];
            if (mode == "published") Published(args[1]); else Behavior(mode);
            Console.WriteLine("PASS " + mode + " " + _checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

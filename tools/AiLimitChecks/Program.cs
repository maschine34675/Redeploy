using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;
using UnityEngine;

public static class Program
{
    private static int _checks;
    private static Type _componentType;
    private static FieldInfo _humans, _world, _botState;
    private static MethodInfo _distance;
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); _checks++; }
    private static void Throws(Action action, string reason)
    {
        try { action(); } catch (InvalidOperationException) { _checks++; return; }
        throw new Exception(reason);
    }
    // Model the released distance method's use of its cached physical player positions.
    public static float Distance(List<Player> humans, Player bot) => humans.Min(p => (p.Position - bot.Position) * (p.Position - bot.Position));
    private static void Build(string mode)
    {
        var version = mode == "newer" ? "1.9.3" : mode == "older" ? "1.9.1" : "1.9.2";
        var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("dvize.AILimit") { Version = new Version(version + ".0") }, AssemblyBuilderAccess.Run);
        var module = asm.DefineDynamicModule("test");
        var plugin = module.DefineType("AIlimit.AILimitPlugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.SetCustomAttribute(new CustomAttributeBuilder(typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }),
            new object[] { AiLimitContract.PluginGuid, "dvize.AILimit", version }));
        var pluginType = plugin.CreateType();
        var component = module.DefineType("AILimit.AILimitComponent", TypeAttributes.Public, typeof(MonoBehaviour));
        var humans = component.DefineField(mode == "drift" ? "changedPlayers" : "players", typeof(List<Player>), FieldAttributes.Private);
        component.DefineField("gameWorld", typeof(GameWorld), FieldAttributes.Private | FieldAttributes.Static);
        component.DefineField("botList", typeof(object), FieldAttributes.Private | FieldAttributes.Static);
        foreach (var name in new[] { "Start", "GetPlayers", "Enable" })
        {
            var method = component.DefineMethod(name, name == "Enable" ? MethodAttributes.Public | MethodAttributes.Static : MethodAttributes.Private, typeof(void), Type.EmptyTypes);
            method.GetILGenerator().Emit(OpCodes.Ret);
        }
        var distance = component.DefineMethod("getMinDistanceToBot", MethodAttributes.Private, typeof(float), new[] { typeof(Player) });
        var il = distance.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, humans); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(Distance))); il.Emit(OpCodes.Ret);
        _componentType = component.CreateType();
        _humans = _componentType.GetField(humans.Name, BindingFlags.NonPublic | BindingFlags.Instance);
        _world = _componentType.GetField("gameWorld", BindingFlags.NonPublic | BindingFlags.Static);
        _botState = _componentType.GetField("botList", BindingFlags.NonPublic | BindingFlags.Static);
        _distance = _componentType.GetMethod("getMinDistanceToBot", BindingFlags.NonPublic | BindingFlags.Instance);
        Chainloader.PluginInfos[AiLimitContract.PluginGuid] = new PluginInfo
        { Instance = (BaseUnityPlugin)Activator.CreateInstance(pluginType), Metadata = pluginType.GetCustomAttribute<BepInPlugin>() };
    }
    private sealed class Raid
    {
        internal readonly GameWorld World = new();
        internal readonly LocalGame Game;
        internal readonly LocalPlayer Old;
        internal readonly LocalPlayer Other;
        internal readonly object BotState = new();
        internal readonly List<Player> Humans = new();
        internal Component Component;
        internal Raid(bool component = true, bool empty = false)
        {
            Old = new LocalPlayer { GameWorld = World, Position = 100 };
            Other = new LocalPlayer { GameWorld = World, Profile = Old.Profile, IsYourPlayer = false, Position = 500 };
            Game = new LocalGame { GameWorld = World, LocalPlayer = Old, Profile = Old.Profile };
            World.MainPlayer = Old; World.RegisteredPlayers.Add(Old); World.RegisteredPlayers.Add(Other);
            Singleton<GameWorld>.Instance = World; Singleton<AbstractGame>.Instance = Game; GamePlayerOwner.MyPlayer = Old;
            if (!empty) Humans.AddRange(new Player[] { Other, Old });
            if (component) Attach();
        }
        internal void Attach()
        {
            Component = (Component)Activator.CreateInstance(_componentType); Component.gameObject = World.gameObject;
            World.gameObject.Components.Add(Component); _world.SetValue(null, World); _humans.SetValue(Component, Humans); _botState.SetValue(null, BotState);
        }
        internal void Prepare(Profile nextProfile = null) { BodyReplacementContext.Begin(Game, (LocalPlayer)Game.LocalPlayer, nextProfile); AiLimitCompat.Prepare(Game, (LocalPlayer)Game.LocalPlayer); }
        internal LocalPlayer Next(Profile profile = null, bool own = true)
        {
            var next = new LocalPlayer { GameWorld = World, Profile = profile ?? Game.Profile, Position = 10 };
            World.MainPlayer = next;
            if (own) Check(BodyReplacementContext.TryOwnCandidate(next), "Core authorizes exact candidate");
            Game.Profile = next.Profile; Game.LocalPlayer = next; GamePlayerOwner.MyPlayer = next; World.RegisteredPlayers.Add(next);
            return next;
        }
        internal void Finish()
        { AiLimitCompat.RetireWorld(World); BodyReplacementContext.Reset(); }
        internal void CheckUntouched(string reason)
        { Check(Humans.Count == 2 && ReferenceEquals(Humans[0], Other) && ReferenceEquals(Humans[1], Old), reason); }
        internal void CheckOtherState()
        { Check(ReferenceEquals(_humans.GetValue(Component), Humans) && ReferenceEquals(_botState.GetValue(null), BotState) && ReferenceEquals(Humans[0], Other), "limiter list identity, unrelated same-profile human and bot state preserved"); }
    }
    private static void Behavior(string mode)
    {
        Check(AiLimitCompat.GetRejectionReason() == null, "absent plugin no-op");
        AiLimitCompat.Prepare(null, null); AiLimitCompat.Tick(); AiLimitCompat.RetireWorld(null);
        Build(mode);
        var issue = AiLimitCompat.GetRejectionReason();
        if (mode == "older" || mode == "drift")
        {
            Check(issue != null, "invalid contract rejected before body teardown");
            Check(AiLimitCompat.GetRejectionReason() == issue && Redeploy.Core.CoreLog.Warnings == 1, "bounded refusal warning");
            return;
        }
        Check(issue == null, "supported released API");
        Check(AiLimitCompat.GetRejectionReason() == null && Redeploy.Core.CoreLog.Warnings == (mode == "newer" ? 1 : 0), "forward versions use matching API with one warning");

        var raid = new Raid();
        RespawnApi.Publish(raid.Old, raid.Other); AiLimitCompat.Tick(); raid.CheckUntouched("ordinary raid and unprepared publication untouched");
        Throws(() => AiLimitCompat.Prepare(raid.Game, raid.Old), "preparation requires Core ownership");
        raid.Prepare(); raid.CheckUntouched("Prepare has no foreign mutation");
        AiLimitCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        RespawnApi.Publish(raid.Old, raid.Other); raid.CheckUntouched("cancelled preparation leaves old cache untouched");
        raid.Prepare();
        var next = raid.Next();
        AiLimitCompat.Tick(); raid.CheckUntouched("creating candidate not adopted before publication");
        RespawnApi.Publish(raid.Other, next); raid.CheckUntouched("same-profile foreign publication ignored");
        AiLimitCompat.RetireWorld(new GameWorld());
        RespawnApi.Publish(raid.Old, next);
        Check(ReferenceEquals(raid.Humans[1], next), "published replacement updates exact old physical slot");
        Check((float)_distance.Invoke(raid.Component, new object[] { new Player { Position = 12 } }) == 4,
            "limiter distance now uses new body position");
        raid.CheckOtherState(); BodyReplacementContext.EndAttempt();
        RespawnApi.Publish(raid.Old, raid.Other); Check(ReferenceEquals(raid.Humans[1], next), "late old publication cannot overwrite current body");
        raid.Prepare(); var third = raid.Next(); RespawnApi.Publish(next, third); BodyReplacementContext.EndAttempt();
        Check(ReferenceEquals(raid.Humans[1], third), "repeated same-profile body replacement");
        var scav = new Profile { Id = "own-scav" }; raid.Prepare(scav); var scavBody = raid.Next(scav);
        RespawnApi.Publish(third, scavBody); BodyReplacementContext.EndAttempt();
        Check(ReferenceEquals(raid.Humans[1], scavBody) && scavBody.Profile.Id != raid.Old.Profile.Id, "PMC to Scav follows physical ownership without profile-id equality");
        raid.CheckOtherState(); raid.Finish();

        raid = new Raid(); raid.Prepare(); next = raid.Next(own: false);
        RespawnApi.Publish(raid.Old, next); raid.CheckUntouched("unowned replacement publication cannot steal cache");
        BodyReplacementContext.EndAttempt(); RespawnApi.Fail(raid.Old); AiLimitCompat.Tick(); raid.CheckUntouched("unowned failure recovery cannot steal cache"); raid.Finish();

        raid = new Raid(); raid.Prepare(); next = raid.Next(); RespawnApi.Fail(raid.Old); AiLimitCompat.Tick();
        raid.CheckUntouched("failure cannot adopt while creating"); BodyReplacementContext.EndAttempt(); AiLimitCompat.Tick();
        Check(ReferenceEquals(raid.Humans[1], next), "committed failure recovery repairs exact old slot"); raid.CheckOtherState(); raid.Finish();

        raid = new Raid(); raid.Prepare(); RespawnApi.Fail(raid.Old); BodyReplacementContext.EndAttempt(); AiLimitCompat.Tick();
        raid.CheckUntouched("creation failure with old body current retains native cache"); raid.Finish();

        raid = new Raid(); raid.Prepare(); next = raid.Next(); raid.World.RegisteredPlayers.Remove(next);
        RespawnApi.Publish(raid.Old, next); raid.CheckUntouched("unregistered candidate cannot be adopted");
        raid.World.RegisteredPlayers.Add(next); GamePlayerOwner.MyPlayer = raid.Old;
        RespawnApi.Publish(raid.Old, next); raid.CheckUntouched("owner not committed cannot be adopted");
        GamePlayerOwner.MyPlayer = next; next.HealthController.IsAlive = false;
        RespawnApi.Publish(raid.Old, next); raid.CheckUntouched("dead candidate cannot be adopted");
        next.HealthController.IsAlive = true; raid.Game.Status = GameStatus.Stopped;
        RespawnApi.Publish(raid.Old, next); raid.CheckUntouched("stopping raid cannot be adopted"); raid.Finish();

        raid = new Raid(component: false); raid.Prepare(); next = raid.Next(); raid.Attach();
        RespawnApi.Publish(raid.Old, next); Check(ReferenceEquals(raid.Humans[1], next), "late native Start cache is rebound after publication"); raid.Finish();
        raid = new Raid(empty: true); raid.Prepare(); next = raid.Next(); raid.Humans.Add(next);
        RespawnApi.Publish(raid.Old, next); Check(raid.Humans.Count == 1 && ReferenceEquals(raid.Humans[0], next), "native initialization already at new body is retained"); raid.Finish();
        raid = new Raid(empty: true); raid.Prepare(); raid.Humans.Add(raid.Old); next = raid.Next();
        RespawnApi.Publish(raid.Old, next); Check(raid.Humans.Count == 1 && ReferenceEquals(raid.Humans[0], next), "delayed Start in existing component follows published replacement"); raid.Finish();
        raid = new Raid(component: false, empty: true); raid.Prepare(); next = raid.Next();
        RespawnApi.Publish(raid.Old, next); Check(raid.Humans.Count == 0, "absent initial component remains native initialized"); raid.Finish();

        var former = new Raid(); former.Prepare(); var formerNext = former.Next(); former.Finish();
        raid = new Raid(); raid.Prepare(); next = raid.Next();
        AiLimitCompat.RetireWorld(former.World); RespawnApi.Publish(former.Old, formerNext); RespawnApi.Fail(former.Old);
        raid.CheckUntouched("old world callbacks cannot mutate new prepared raid");
        RespawnApi.Publish(raid.Old, next); Check(ReferenceEquals(raid.Humans[1], next), "old world retirement preserves new preparation"); raid.Finish();

        raid = new Raid(); raid.Prepare(); next = raid.Next();
        var unrelated = new Raid(); RespawnApi.Publish(raid.Old, next); AiLimitCompat.Tick();
        raid.CheckUntouched("changed live world prevents stale prepared mutation");
        unrelated.CheckUntouched("changed live world keeps ordinary new raid untouched");
        RespawnApi.Fail(raid.Old); AiLimitCompat.Tick(); unrelated.CheckUntouched("late failure after world transition ignored"); raid.Finish(); unrelated.Finish();

        raid = new Raid(); _world.SetValue(null, new GameWorld()); BodyReplacementContext.Begin(raid.Game, raid.Old);
        Throws(() => AiLimitCompat.Prepare(raid.Game, raid.Old), "foreign static world rejected before teardown");
        raid.CheckUntouched("foreign static world leaves cache untouched"); raid.Finish();
        raid = new Raid(); _humans.SetValue(raid.Component, null); BodyReplacementContext.Begin(raid.Game, raid.Old);
        Throws(() => AiLimitCompat.Prepare(raid.Game, raid.Old), "missing native cache rejected before teardown");
        Check(_humans.GetValue(raid.Component) == null, "missing cache is never synthesized"); raid.Finish();
        raid = new Raid(); raid.Attach(); BodyReplacementContext.Begin(raid.Game, raid.Old);
        Throws(() => AiLimitCompat.Prepare(raid.Game, raid.Old), "ambiguous components rejected before teardown"); raid.Finish();

        raid = new Raid(); raid.Humans.Remove(raid.Old); BodyReplacementContext.Begin(raid.Game, raid.Old);
        Throws(() => AiLimitCompat.Prepare(raid.Game, raid.Old), "foreign cache refuses before teardown");
        Check(raid.Humans.Count == 1 && ReferenceEquals(raid.Humans[0], raid.Other), "foreign-only cache is not rebuilt from registry"); raid.Finish();
        raid = new Raid(); raid.Humans.Add(raid.Old); BodyReplacementContext.Begin(raid.Game, raid.Old);
        Throws(() => AiLimitCompat.Prepare(raid.Game, raid.Old), "duplicate old physical entries rejected"); raid.Finish();

        raid = new Raid(); raid.Prepare(); next = raid.Next();
        var foreignList = new List<Player> { raid.Other }; _humans.SetValue(raid.Component, foreignList);
        RespawnApi.Publish(raid.Old, next);
        Check(foreignList.Count == 1 && ReferenceEquals(foreignList[0], raid.Other), "runtime foreign cache replacement untouched");
        Check(AiLimitCompat.GetRejectionReason() != null, "runtime ownership drift refuses future replacement"); raid.Finish();
        Chainloader.PluginInfos.Clear(); Check(AiLimitCompat.GetRejectionReason() == null, "optional mod removed without global mutation");
    }
    private static void Published(string path)
    {
        using var dll = Mono.Cecil.AssemblyDefinition.ReadAssembly(path);
        Check(dll.Name.Name == "dvize.AILimit" && dll.Name.Version == new Version(1, 9, 2, 0), "released assembly identity");
        var plugin = dll.MainModule.GetType("AIlimit.AILimitPlugin");
        var metadata = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        Check((string)metadata.ConstructorArguments[0].Value == AiLimitContract.PluginGuid && (string)metadata.ConstructorArguments[2].Value == "1.9.2", "released GUID/version");
        var component = dll.MainModule.GetType("AILimit.AILimitComponent");
        Check(component.BaseType.FullName == "UnityEngine.MonoBehaviour", "released component owner");
        var humans = component.Fields.Single(f => f.Name == "players");
        var world = component.Fields.Single(f => f.Name == "gameWorld");
        Check(!humans.IsStatic && !humans.IsInitOnly && humans.FieldType.FullName == "System.Collections.Generic.List`1<EFT.Player>", "released human list contract");
        Check(world.IsStatic && world.FieldType.FullName == "EFT.GameWorld", "released world contract");
        var getPlayers = component.Methods.Single(m => m.Name == "GetPlayers");
        var callers = component.Methods.Where(m => m.HasBody && m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference call && call.FullName == getPlayers.FullName)).Select(m => m.Name).ToArray();
        Check(callers.SequenceEqual(new[] { "Start" }), "human players populated only at native Start");
        Check(getPlayers.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference call && call.Name == "get_IsAI"), "native cache excludes bots");
        var distance = component.Methods.Single(m => m.Name == "getMinDistanceToBot");
        Check(!distance.IsStatic && distance.ReturnType.FullName == "System.Single" && distance.Parameters.Single().ParameterType.FullName == "EFT.Player", "released distance API");
        Check(distance.Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference f && f.Name == "players")
            && distance.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference call && call.Name == "get_Position"), "distance uses cached body position");
        Check(component.Methods.Any(m => m.Name == "Enable" && m.IsStatic && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 0), "released initialization API");
        using var hash = SHA256.Create();
        Check(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "") == "578EBFE8510087F1B6B58EEA7F53F4B4EE8979640D929E735661A4F3E476CA75", "reviewed release bytes");
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

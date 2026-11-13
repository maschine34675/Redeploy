using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using Redeploy.Core.Compat;
using Redeploy.Core.Engine;
using Redeploy.Core.Api;
using UnityEngine;

public static class Program
{
    private static Type _monitorType, _locationType;
    private static FieldInfo _players, _allies, _enemies, _location;
    private static MethodInfo _check;
    private static int _checks;
    private static void Check(bool condition, string reason) { if (!condition) throw new Exception(reason); _checks++; }
    private static void Throws(Action action, string reason)
    { try { action(); } catch (Exception ex) when (ex.GetBaseException() is InvalidOperationException) { _checks++; return; } throw new Exception(reason); }

    private static void Build(string mode)
    {
        var version = mode == "newer" ? "1.3.0" : mode == "older" ? "1.1.9" : "1.2.0";
        var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName(mode == "identity" ? "Impostor" : "QuestingBots-Client")
            { Version = new Version(version + ".0") }, AssemblyBuilderAccess.Run);
        var module = asm.DefineDynamicModule("fixture");
        var plugin = module.DefineType("QuestingBots.QuestingBotsPlugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.SetCustomAttribute(new CustomAttributeBuilder(typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }),
            new object[] { QuestingBotsCompat.PluginGuid, "Questing Bots", version }));
        var pluginType = plugin.CreateType();
        var location = module.DefineType("QuestingBots.Components.LocationData", TypeAttributes.Public, typeof(MonoBehaviour));
        var point = location.DefineMethod("IsPointOnLightkeeperIsland", MethodAttributes.Public, typeof(bool), new[] { typeof(Vector3?) });
        var pointIl = point.GetILGenerator(); pointIl.Emit(OpCodes.Ldarg_1); pointIl.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(NativePoint))); pointIl.Emit(OpCodes.Ret);
        _locationType = location.CreateType();
        var monitor = module.DefineType("QuestingBots.Components.LightkeeperIslandMonitor", TypeAttributes.Public, typeof(MonoBehaviour));
        monitor.DefineField("locationData", _locationType, FieldAttributes.Private);
        monitor.DefineField(mode == "drift" ? "renamedPlayers" : "playersOnIsland", typeof(List<Player>), FieldAttributes.Private);
        monitor.DefineField("originalAllies", typeof(Dictionary<Player, IPlayer[]>), FieldAttributes.Private);
        monitor.DefineField("originalEnemies", typeof(Dictionary<Player, IPlayer[]>), FieldAttributes.Private);
        var method = monitor.DefineMethod("IsPlayerOnLightkeeperIsland", MethodAttributes.Public, typeof(bool), new[] { typeof(Player) });
        method.SetImplementationFlags(MethodImplAttributes.NoInlining);
        var il = method.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(NativeCheck))); il.Emit(OpCodes.Ret);
        _monitorType = monitor.CreateType();
        _location = _monitorType.GetField("locationData", BindingFlags.NonPublic | BindingFlags.Instance);
        _players = _monitorType.GetField("playersOnIsland", BindingFlags.NonPublic | BindingFlags.Instance);
        _allies = _monitorType.GetField("originalAllies", BindingFlags.NonPublic | BindingFlags.Instance);
        _enemies = _monitorType.GetField("originalEnemies", BindingFlags.NonPublic | BindingFlags.Instance);
        _check = _monitorType.GetMethod("IsPlayerOnLightkeeperIsland");
        Chainloader.PluginInfos[QuestingBotsCompat.PluginGuid] = new PluginInfo
            { Instance = (BaseUnityPlugin)Activator.CreateInstance(pluginType), Metadata = pluginType.GetCustomAttribute<BepInPlugin>() };
    }

    public static bool NativePoint(Vector3? position) => position?.x > 0;

    // Bounded model of release 1.2.0's island entry/exit and profile-ID alliance
    // resolution. The unpatched controls below must reproduce both actual hazards.
    public static bool NativeCheck(Component monitor, Player body)
    {
        var players = (List<Player>)_players.GetValue(monitor);
        var allies = (Dictionary<Player, IPlayer[]>)_allies.GetValue(monitor);
        var enemies = (Dictionary<Player, IPlayer[]>)_enemies.GetValue(monitor);
        if (players.Contains(body) == body.OnIsland) return body.OnIsland;
        if (body.OnIsland)
        {
            players.Add(body);
            var group = body.AIData?.BotOwner?.BotsGroup;
            if (group != null) { allies[body] = group.Allies.ToArray(); enemies[body] = group.Enemies.Keys.ToArray(); }
            foreach (var peer in players.Where(peer => peer.Profile.Id != body.Profile.Id))
            { FormAlliance(body, peer); FormAlliance(peer, body); }
        }
        else
        {
            players.Remove(body);
            Revert(body, null, allies, enemies);
            foreach (var peer in players.Where(peer => peer.Profile.Id != body.Profile.Id)) Revert(peer, body, allies, enemies);
        }
        return body.OnIsland;
    }
    private static void FormAlliance(Player bot, Player target)
    {
        var group = bot.AIData?.BotOwner?.BotsGroup;
        if (group == null) return;
        foreach (var enemy in group.Enemies.Keys.Where(enemy => enemy.Profile.Id == target.Profile.Id).ToArray()) group.Enemies.Remove(enemy);
        var candidates = bot.GameWorld.AllAlivePlayersList.Where(player => player.Profile.Id == target.Profile.Id).ToArray();
        group.AddAlly(candidates.Length == 1 ? candidates[0] : null);
    }
    private static void Revert(Player bot, Player target, Dictionary<Player, IPlayer[]> allies, Dictionary<Player, IPlayer[]> enemies)
    {
        var group = bot.AIData?.BotOwner?.BotsGroup;
        if (group == null) return;
        group.Allies.RemoveAll(ally => (target == null || ally?.Profile.Id == target.Profile.Id) && !allies[bot].Contains(ally));
        foreach (var enemy in enemies[bot]) if ((target == null || enemy?.Profile.Id == target.Profile.Id) && !group.Enemies.ContainsKey(enemy)) group.Enemies.Add(enemy, new());
    }

    private sealed class Raid
    {
        internal readonly GameWorld World = new ClientGameWorld();
        internal readonly LocalGame Game;
        internal readonly LocalPlayer Old;
        internal Component Monitor;
        internal readonly List<Player> Players = new();
        internal readonly Dictionary<Player, IPlayer[]> Allies = new(), Enemies = new();
        internal Raid(bool monitor = true)
        {
            Old = new LocalPlayer { GameWorld = World, OnIsland = true };
            Game = new LocalGame { LocalPlayer = Old, GameWorld = World, Profile = Old.Profile };
            World.Register(Old); Singleton<GameWorld>.Instance = World; Singleton<AbstractGame>.Instance = Game;
            if (monitor)
            {
                Monitor = (Component)Activator.CreateInstance(_monitorType); Monitor.gameObject = World.gameObject; World.gameObject.Components.Add(Monitor);
                var location = (Component)Activator.CreateInstance(_locationType); location.gameObject = World.gameObject; World.gameObject.Components.Add(location);
                _location.SetValue(Monitor, location); _players.SetValue(Monitor, Players); _allies.SetValue(Monitor, Allies); _enemies.SetValue(Monitor, Enemies);
                Scan(Old);
            }
        }
        internal bool Scan(Player player) => (bool)_check.Invoke(Monitor, new object[] { player });
        internal Player Bot(string id, bool island = true)
        {
            var player = new Player { GameWorld = World, IsAI = true, IsYourPlayer = false, OnIsland = island, Profile = new() { Id = id } };
            player.AIData = new() { BotOwner = new() { GetPlayer = player } }; World.Register(player); return player;
        }
        internal void Prepare(Profile profile = null)
        { BodyReplacementContext.Begin(Game, Game.LocalPlayer, profile); QuestingBotsCompat.Prepare(Game, Game.LocalPlayer); RespawnEngineState.IsPipelineRunning = true; }
        internal LocalPlayer Next(Profile profile = null, bool island = false)
        {
            var next = new LocalPlayer { GameWorld = World, Profile = profile ?? Game.Profile, OnIsland = island };
            World.Register(next); Check(BodyReplacementContext.TryOwnCandidate(next), "candidate belongs to exact Core replacement");
            var old = Game.LocalPlayer; Game.LocalPlayer = next; Game.Profile = next.Profile; RespawnApi.Publish(old, next);
            RespawnEngineState.IsPipelineRunning = false; BodyReplacementContext.EndAttempt(); QuestingBotsCompat.Tick(); return next;
        }
        internal void End() { QuestingBotsCompat.RetireWorld(World); BodyReplacementContext.Reset(); RespawnEngineState.IsPipelineRunning = false; }
    }

    private static void Behavior(string mode)
    {
        Check(QuestingBotsCompat.GetRejectionReason() == null, "absent mod is inert");
        QuestingBotsCompat.Prepare(null, null); QuestingBotsCompat.Tick(); QuestingBotsCompat.RetireWorld(null);
        Build(mode);
        var rejection = QuestingBotsCompat.GetRejectionReason();
        if (mode is "older" or "drift" or "identity")
        {
            Check(rejection != null, "invalid contract refuses before teardown");
            Check(QuestingBotsCompat.GetRejectionReason() == rejection && Redeploy.Core.CoreLog.Warnings == 1, "refusal is cached and warning bounded");
            return;
        }
        Check(rejection == null, "reviewed or upward-compatible structural contract accepted");
        Check(QuestingBotsCompat.GetRejectionReason() == null && Redeploy.Core.CoreLog.Warnings == (mode == "newer" ? 1 : 0), "newer contract warns once");
        foreach (var scav in new[] { false, true })
        {
            var control = new Raid(); control.World.UnregisterPlayer(control.Old);
            var next = new LocalPlayer { GameWorld = control.World, Profile = scav ? new() { Id = "scav" } : control.Old.Profile };
            control.World.Register(next);
            var bot = control.Bot("control"); control.Scan(bot);
            Check(scav ? bot.AIData.BotOwner.BotsGroup.NullAllyAdds == 1 : bot.AIData.BotOwner.BotsGroup.Allies.Contains(next),
                "unprepared positive control reproduces stale alias/null ally hazard");
            control.End();
        }

        var raid = new Raid(); var resident = raid.Bot("resident"); raid.Scan(resident);
        var group = resident.AIData.BotOwner.BotsGroup;
        var peer = new LocalPlayer { GameWorld = raid.World, Profile = raid.Old.Profile, IsYourPlayer = false };
        group.Allies.Add(peer); raid.Allies[resident] = new IPlayer[] { raid.Old, peer }; raid.Enemies[resident] = new IPlayer[] { raid.Old, peer };
        var originalAllyArray = raid.Allies[resident]; var originalEnemyArray = raid.Enemies[resident];
        Throws(() => QuestingBotsCompat.Prepare(raid.Game, raid.Old), "preparation requires Core ownership");
        raid.Prepare();
        Check(raid.Players.Contains(raid.Old) && group.Allies.Contains(raid.Old) && ReferenceEquals(raid.Allies[resident], originalAllyArray)
            && ReferenceEquals(raid.Enemies[resident], originalEnemyArray), "preparation is read-only including arrays and group allies");
        QuestingBotsCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation(); raid.World.UnregisterPlayer(raid.Old);
        Check(raid.Players.Contains(raid.Old) && group.Allies.Contains(raid.Old), "cancelled preparation cannot retire original body");
        raid.World.Register(raid.Old); raid.Prepare();
        QuestingBotsCompat.RetireWorld(new GameWorld());
        raid.World.UnregisterPlayer(peer);
        Check(raid.Players.Contains(raid.Old), "same-profile foreign unregistration is inert");
        raid.World.UnregisterPlayer(raid.Old);
        Check(!raid.Players.Contains(raid.Old) && !group.Allies.Contains(raid.Old) && group.Allies.Contains(peer), "unregistration retires exact body and preserves alias peer");
        Check(raid.Allies[resident].SequenceEqual(new[] { peer }) && raid.Enemies[resident].SequenceEqual(new[] { peer }), "snapshots drop exact old references and preserve alias peer");
        Check(ReferenceEquals(_players.GetValue(raid.Monitor), raid.Players) && ReferenceEquals(_allies.GetValue(raid.Monitor), raid.Allies), "foreign collection identities remain stable");
        Check(!raid.Scan(raid.Old) && !raid.Players.Contains(raid.Old), "late old-body island check cannot reinsert corpse");
        var outside = raid.Next(); var arriving = raid.Bot("arriving"); raid.Scan(arriving);
        Check(!arriving.AIData.BotOwner.BotsGroup.Allies.Contains(outside) && arriving.AIData.BotOwner.BotsGroup.NullAllyAdds == 0, "outside replacement not aliased into island alliance");
        outside.OnIsland = true; raid.Scan(outside);
        Check(raid.Players.Contains(outside) && arriving.AIData.BotOwner.BotsGroup.Allies.Contains(outside), "replacement inside assessed by own location");
        raid.Prepare(); raid.World.UnregisterPlayer(outside); var second = raid.Next(island: true); raid.Scan(second);
        Check(!raid.Players.Contains(outside) && raid.Players.Contains(second) && group.Allies.Contains(second), "second physical replacement gets independent membership");
        second.OnIsland = false; raid.Scan(second);
        Check(!group.Allies.Contains(second) && !group.Enemies.ContainsKey(raid.Old) && !group.Enemies.ContainsKey(outside), "later exit cannot restore retired enemies");
        var ownScav = new Profile { Id = "own-scav" }; raid.Prepare(ownScav); raid.World.UnregisterPlayer(second); raid.Next(ownScav);
        raid.End();
    }

    private static void EdgeCases()
    {
        var raid = new Raid(); var scav = new Profile { Id = "scav" }; raid.Prepare(scav); raid.World.UnregisterPlayer(raid.Old); var next = raid.Next(scav);
        var bot = raid.Bot("new-bot"); raid.Scan(bot);
        Check(bot.AIData.BotOwner.BotsGroup.NullAllyAdds == 0 && !bot.AIData.BotOwner.BotsGroup.Allies.Contains(next), "own Scav outside cannot add null ally");
        next.OnIsland = true; raid.Scan(next);
        Check(bot.AIData.BotOwner.BotsGroup.Allies.Contains(next), "own Scav inside assessed by physical location"); raid.End();

        raid = new Raid(); var resident = raid.Bot("hostile-resident"); var residentGroup = resident.AIData.BotOwner.BotsGroup;
        residentGroup.Enemies.Add(raid.Old, new()); raid.Scan(resident);
        Check(!residentGroup.Enemies.ContainsKey(raid.Old) && raid.Enemies[resident].Contains(raid.Old), "native island alliance snapshots prior hostility");
        raid.Prepare(); raid.World.UnregisterPlayer(raid.Old); BodyReplacementContext.EndAttempt(); QuestingBotsCompat.Tick();
        Check(!raid.Scan(raid.Old), "failed creation never readopts unregistered old body");
        raid.World.Register(raid.Old);
        Check(raid.Scan(raid.Old) && raid.Players.Contains(raid.Old), "explicit alive old-body recovery receives fresh native assessment");
        raid.Old.OnIsland = false; raid.Scan(raid.Old);
        Check(residentGroup.Enemies.ContainsKey(raid.Old), "recovered same old body preserves pre-island hostility on exit"); raid.End();

        raid = new Raid(); resident = raid.Bot("hostile-resident"); residentGroup = resident.AIData.BotOwner.BotsGroup;
        residentGroup.Enemies.Add(raid.Old, new()); raid.Scan(resident); raid.Prepare(); raid.World.UnregisterPlayer(raid.Old);
        BodyReplacementContext.EndAttempt(); QuestingBotsCompat.Tick(); raid.Old.OnIsland = false; raid.World.Register(raid.Old); raid.Scan(raid.Old);
        Check(!raid.Players.Contains(raid.Old) && residentGroup.Enemies.ContainsKey(raid.Old) && !residentGroup.Allies.Contains(raid.Old),
            "old-body recovery directly outside performs native hostility restoration"); raid.End();

        raid = new Raid(); raid.Prepare(); raid.World.UnregisterPlayer(raid.Old); var newcomer = raid.Bot("recovery-newcomer"); raid.Scan(newcomer);
        BodyReplacementContext.EndAttempt(); QuestingBotsCompat.Tick(); raid.World.Register(raid.Old); raid.Scan(raid.Old);
        Check(newcomer.AIData.BotOwner.BotsGroup.Allies.Contains(raid.Old), "inside old-body recovery includes newly arrived island bot"); raid.End();

        raid = new Raid(); resident = raid.Bot("changed-snapshot"); residentGroup = resident.AIData.BotOwner.BotsGroup;
        residentGroup.Enemies.Add(raid.Old, new()); raid.Scan(resident); raid.Prepare(); raid.World.UnregisterPlayer(raid.Old);
        var newerRow = new IPlayer[0]; raid.Enemies[resident] = newerRow;
        BodyReplacementContext.EndAttempt(); QuestingBotsCompat.Tick(); raid.World.Register(raid.Old); raid.Scan(raid.Old);
        Check(ReferenceEquals(raid.Enemies[resident], newerRow), "old-body recovery never overwrites newer native snapshot row"); raid.End();

        raid = new Raid(); raid.Prepare(); var originalPlayers = raid.Players;
        _players.SetValue(raid.Monitor, new List<Player>(raid.Players));
        Throws(() => raid.World.UnregisterPlayer(raid.Old), "changed foreign collection refuses before mutation");
        Check(originalPlayers.Contains(raid.Old) && raid.World.RegisteredPlayers.Contains(raid.Old), "collection refusal has no foreign mutation"); raid.End();

        raid = new Raid(); var detached = raid.Bot("detached"); raid.Scan(detached);
        var destroyed = raid.Bot("destroyed"); raid.Scan(destroyed);
        var recycled = raid.Bot("recycled"); raid.Scan(recycled);
        detached.GameWorld = null; destroyed.Destroyed = true; recycled.AIData.BotOwner.GetPlayer = new Player();
        raid.Enemies[detached] = new IPlayer[] { raid.Old }; raid.Enemies[destroyed] = new IPlayer[] { raid.Old };
        raid.Prepare(); raid.World.UnregisterPlayer(raid.Old);
        Check(!raid.Players.Contains(raid.Old) && raid.Enemies[detached].Length == 0 && raid.Enemies[destroyed].Length == 0,
            "historical destroyed and detached bot snapshots cannot veto retirement");
        Check(detached.AIData.BotOwner.BotsGroup.Allies.Contains(raid.Old) && destroyed.AIData.BotOwner.BotsGroup.Allies.Contains(raid.Old)
            && recycled.AIData.BotOwner.BotsGroup.Allies.Contains(raid.Old), "unowned historical bot groups are never mutated"); raid.End();

        raid = new Raid(); var foreignBody = raid.Bot("foreign"); raid.Scan(foreignBody); foreignBody.GameWorld = new GameWorld();
        Throws(() => raid.Prepare(), "live foreign-world snapshot owner refused"); raid.End();

        raid = new Raid(); raid.Prepare(); RespawnEngineState.IsPipelineRunning = false; raid.World.UnregisterPlayer(raid.Old);
        Check(raid.Players.Contains(raid.Old) && !raid.World.RegisteredPlayers.Contains(raid.Old), "provisional unregister never mutates island state"); raid.End();

        raid = new Raid(); raid.Prepare(); raid.Game.Status = GameStatus.Stopped; raid.World.UnregisterPlayer(raid.Old);
        Check(raid.Players.Contains(raid.Old) && !raid.World.RegisteredPlayers.Contains(raid.Old), "real raid stop remains native and never throws"); raid.End();

        raid = new Raid(); raid.Old.HealthController.IsAlive = false; raid.Prepare(); raid.World.UnregisterPlayer(raid.Old);
        Check(!HasRollback(raid.Old), "dead body retains no rollback collections"); raid.Next(); raid.End();
        raid = new Raid(); raid.Old.HealthController.IsAlive = false; raid.World.UnregisterPlayer(raid.Old); raid.Prepare(); raid.World.UnregisterPlayer(raid.Old);
        Check(!raid.Players.Contains(raid.Old) && !HasRollback(raid.Old), "client override retires already unregistered dead body before base is skipped");
        var recoveredScav = new Profile { Id = "scav-after-early-unregister" };
        // This attempt is same-profile; the ordinary arriving bot must never resolve its retired old body.
        raid.Next(); var afterEarlyUnregister = raid.Bot("after-early-unregister"); raid.Scan(afterEarlyUnregister);
        Check(afterEarlyUnregister.AIData.BotOwner.BotsGroup.Allies.Count == 0, "early-unregistered corpse cannot ally successor when bot arrives"); raid.End();
        raid = new Raid(); raid.Prepare(); raid.World.UnregisterPlayer(raid.Old);
        Check(HasRollback(raid.Old), "living body retains rollback until commit"); raid.Next();
        Check(!HasRollback(raid.Old), "published replacement releases old rollback collections"); raid.End();

        raid = new Raid(); raid.Prepare(); var info = Chainloader.PluginInfos[QuestingBotsCompat.PluginGuid]; Chainloader.PluginInfos.Remove(QuestingBotsCompat.PluginGuid);
        raid.World.UnregisterPlayer(raid.Old);
        Check(raid.Players.Contains(raid.Old), "plugin disappearing before unregister cannot mutate stale foreign state");
        QuestingBotsCompat.Prepare(null, null); Chainloader.PluginInfos.Add(QuestingBotsCompat.PluginGuid, info); raid.End();

        raid = new Raid(); _enemies.SetValue(raid.Monitor, null);
        Throws(() => raid.Prepare(), "malformed snapshot refused before teardown");
        Check(raid.Players.Contains(raid.Old), "malformed preparation preserves membership"); raid.End();

        raid = new Raid(); var foreign = (Component)Activator.CreateInstance(_locationType); foreign.gameObject = new GameObject(); _location.SetValue(raid.Monitor, foreign);
        Throws(() => raid.Prepare(), "foreign location owner refused"); raid.End();

        raid = new Raid(); raid.World.gameObject.Components.Add(raid.Monitor);
        Throws(() => raid.Prepare(), "ambiguous monitors refused"); raid.End();

        raid = new Raid(); raid.Prepare(); QuestingBotsCompat.RetireWorld(raid.World); BodyReplacementContext.Reset(); raid.World.UnregisterPlayer(raid.Old);
        Check(raid.Players.Contains(raid.Old), "world retirement discards pending authority"); raid.End();

        raid = new Raid(false); raid.Prepare(); raid.World.UnregisterPlayer(raid.Old); raid.Next(); raid.End();
        Check(true, "non-Lighthouse replacement remains native");

        raid = new Raid(); raid.Prepare(); BodyReplacementContext.EndAttempt(); QuestingBotsCompat.Tick(); raid.World.UnregisterPlayer(raid.Old);
        Check(raid.Players.Contains(raid.Old), "failure before unregister cancels pending authority"); raid.End();

        raid = new Raid(); raid.Prepare(); raid.World.UnregisterPlayer(raid.Old); raid.Next(); raid.End();
        var fresh = new Raid(); Check(fresh.Scan(fresh.Old), "fresh raid ordinary body untouched");
        Check(!raid.Scan(raid.Old), "retired old-world callback remains fenced"); fresh.End();
    }

    private static bool HasRollback(Player body)
    {
        var table = typeof(QuestingBotsCompat).GetField("Retired", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        var args = new object[] { body, null };
        if (!(bool)table.GetType().GetMethod("TryGetValue").Invoke(table, args)) throw new Exception("expected retired body record");
        return args[1].GetType().GetField("Restore", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(args[1]) != null;
    }

    private static void Published(string path)
    {
        using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
            Check(BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "") == "3FE5181F2EF50EE2D99EBE9A1202F9FFA4A6C5DAD411789773852D4BF77CC94C", "audited release DLL hash matches");
        using var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly(path);
        Check(assembly.Name.Name == "QuestingBots-Client" && assembly.Name.Version == new Version(1, 2, 0, 0), "release assembly identity");
        var type = assembly.MainModule.GetType("QuestingBots.Components.LightkeeperIslandMonitor");
        Check(type != null && type.Fields.Single(field => field.Name == "playersOnIsland").FieldType.FullName == "System.Collections.Generic.List`1<EFT.Player>", "release private player list matches");
        foreach (var name in new[] { "originalAllies", "originalEnemies" })
            Check(type.Fields.Single(field => field.Name == name).FieldType.FullName == "System.Collections.Generic.Dictionary`2<EFT.Player,EFT.IPlayer[]>", "release snapshot matches " + name);
        Check(type.Fields.Single(field => field.Name == "locationData").FieldType.FullName == "QuestingBots.Components.LocationData", "release location owner matches");
        Check(assembly.MainModule.GetType("QuestingBots.Components.LocationData").Methods.Any(method => method.Name == "IsPointOnLightkeeperIsland"
            && method.ReturnType.FullName == "System.Boolean" && method.Parameters.Count == 1
            && method.Parameters[0].ParameterType.FullName == "System.Nullable`1<UnityEngine.Vector3>"), "release physical location query matches");
        Check(type.Methods.Any(method => method.Name == "IsPlayerOnLightkeeperIsland" && method.ReturnType.FullName == "System.Boolean"
            && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == "EFT.Player"), "release location assessment signature matches");
    }

    public static int Main(string[] args)
    {
        try
        {
            var mode = args.Length > 0 ? args[0] : "normal";
            if (mode == "published") Published(args[1]);
            else { Behavior(mode); if (mode is "normal" or "newer") EdgeCases(); }
            Console.WriteLine($"QUESTING_BOTS_{mode.ToUpperInvariant()}_PASSED checks={_checks}"); return 0;
        }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
    }
}

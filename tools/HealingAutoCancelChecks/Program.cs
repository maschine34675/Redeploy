using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
using HarmonyLib;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;

public static class Program
{
    private static int _checks;
    private static Type _globals;
    private static MethodInfo _started;
    private static Action<EBodyPart, float, DamageInfo> _callback;
    public static Action BeforeForeignHandler;
    private static readonly DamageInfo Medicine = new() { DamageType = EDamageType.Medicine };
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); _checks++; }
    private static Player GlobalPlayer => (Player)_globals.GetProperty("player").GetValue(null);
    private static ActiveHealthController GlobalHealth => (ActiveHealthController)_globals.GetProperty("activeHealthController").GetValue(null);
    public static void ForeignStart()
    {
        var player = Singleton<GameWorld>.Instance.MainPlayer;
        _globals.GetMethod("SetPlayer").Invoke(null, new object[] { player });
        _globals.GetMethod("SetPlayerHealthController").Invoke(null, new object[] { player.ActiveHealthController });
        player.ActiveHealthController.HealthChangedEvent += _callback;
    }
    public static void ForeignHandler(EBodyPart part, float amount, DamageInfo damage)
    {
        var hook = BeforeForeignHandler; BeforeForeignHandler = null; hook?.Invoke();
        if (damage.DamageType != EDamageType.Medicine) return;
        var player = GlobalPlayer; var health = GlobalHealth;
        if (player.HasMeds && !health.Broken && (health.Full && !health.Bleeding || player.Resource < 1)) health.RemoveMedEffect();
    }
    private static void Build(string mode)
    {
        var version = mode == "newer" ? "1.2.0" : mode == "older" ? "1.0.0" : "1.1.0";
        var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("HealingAutoCancel"), AssemblyBuilderAccess.Run);
        var module = asm.DefineDynamicModule("test");
        var plugin = module.DefineType("ImprovedSelfcare.Plugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.SetCustomAttribute(new CustomAttributeBuilder(typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }), new object[] { HealingAutoCancelCompat.PluginGuid, "HealingAutoCancel", version }));
        var runtime = plugin.CreateType();
        var globals = module.DefineType("ImprovedSelfcare.Globals", TypeAttributes.Public);
        void Global(string name, string setter, Type fieldType)
        {
            var field = globals.DefineField("_" + name, fieldType, FieldAttributes.Private | FieldAttributes.Static);
            var prop = globals.DefineProperty(name, PropertyAttributes.None, fieldType, Type.EmptyTypes);
            var get = globals.DefineMethod("get_" + name, MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.SpecialName, fieldType, Type.EmptyTypes);
            var il = get.GetILGenerator(); il.Emit(OpCodes.Ldsfld, field); il.Emit(OpCodes.Ret); prop.SetGetMethod(get);
            var set = globals.DefineMethod(setter, MethodAttributes.Public | MethodAttributes.Static, typeof(void), new[] { fieldType });
            il = set.GetILGenerator(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Stsfld, field); il.Emit(OpCodes.Ret);
        }
        Global("player", "SetPlayer", typeof(Player)); Global("activeHealthController", "SetPlayerHealthController", typeof(ActiveHealthController)); _globals = globals.CreateType();
        var patch = module.DefineType("ImprovedSelfcare.HealingAutoCancelPatch", TypeAttributes.Public);
        var handler = patch.DefineMethod(mode == "drift" ? "RenamedHandler" : "ActiveHealthController_HealthChangedEvent", MethodAttributes.Private | MethodAttributes.Static,
            typeof(void), new[] { typeof(EBodyPart), typeof(float), typeof(DamageInfo) }); handler.SetImplementationFlags(MethodImplAttributes.NoInlining);
        var code = handler.GetILGenerator(); code.Emit(OpCodes.Ldarg_0); code.Emit(OpCodes.Ldarg_1); code.Emit(OpCodes.Ldarg_2); code.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(ForeignHandler))); code.Emit(OpCodes.Ret);
        var start = patch.DefineMethod("PostFix", MethodAttributes.Private | MethodAttributes.Static, typeof(void), Type.EmptyTypes); start.SetImplementationFlags(MethodImplAttributes.NoInlining);
        code = start.GetILGenerator(); code.Emit(OpCodes.Call, typeof(Program).GetMethod(nameof(ForeignStart))); code.Emit(OpCodes.Ret);
        var resolved = patch.CreateType(); _started = resolved.GetMethod("PostFix", BindingFlags.NonPublic | BindingFlags.Static);
        _callback = (Action<EBodyPart, float, DamageInfo>)Delegate.CreateDelegate(typeof(Action<EBodyPart, float, DamageInfo>), resolved.GetMethod(handler.Name, BindingFlags.NonPublic | BindingFlags.Static));
        Chainloader.PluginInfos[HealingAutoCancelCompat.PluginGuid] = new PluginInfo { Instance = (BaseUnityPlugin)Activator.CreateInstance(runtime), Metadata = runtime.GetCustomAttribute<BepInPlugin>() };
    }
    private static LocalGame Raid()
    {
        var world = new DerivedClientWorld(); var body = new LocalPlayer { GameWorld = world };
        var game = new LocalGame { GameWorld = world, LocalPlayer = body, Profile = body.Profile };
        world.MainPlayer = body; world.RegisteredPlayers.Add(body); GamePlayerOwner.MyPlayer = body;
        Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game; return game;
    }
    private static LocalPlayer Commit(LocalGame game, LocalPlayer old, bool scav = false, bool publish = true)
    {
        var body = new LocalPlayer { GameWorld = game.GameWorld, Profile = scav ? new Profile() : old.Profile };
        if (scav) { BodyReplacementContext.EndAttempt(); BodyReplacementContext.Begin(game, old, body.Profile); }
        game.GameWorld.MainPlayer = body; Check(BodyReplacementContext.TryOwnCandidate(body), "physical candidate acquired");
        game.LocalPlayer = body; game.Profile = body.Profile; GamePlayerOwner.MyPlayer = body; game.GameWorld.RegisteredPlayers.Add(body);
        if (publish) RespawnApi.Publish(old, body); BodyReplacementContext.EndAttempt(); HealingAutoCancelCompat.Tick(); return body;
    }
    private static void Heal(Player body) => body.ActiveHealthController.ChangeHealth(EBodyPart.Chest, 5, Medicine);
    private static void Published(string path, string nativePath)
    {
        using var dll = Mono.Cecil.AssemblyDefinition.ReadAssembly(path);
        Check(dll.Name.Name == "HealingAutoCancel" && dll.Name.Version == new Version(1, 1, 0, 0), "release assembly identity");
        var plugin = dll.MainModule.GetType("ImprovedSelfcare.Plugin");
        var metadata = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        Check((string)metadata.ConstructorArguments[0].Value == HealingAutoCancelCompat.PluginGuid && (string)metadata.ConstructorArguments[2].Value == "1.1.0", "released plugin GUID and version");
        var globals = dll.MainModule.GetType("ImprovedSelfcare.Globals");
        Check(globals.Properties.Single(p => p.Name == "player").PropertyType.FullName == "EFT.Player"
            && globals.Properties.Single(p => p.Name == "activeHealthController").PropertyType.FullName == "EFT.HealthSystem.ActiveHealthController", "released global properties");
        foreach (var setter in new[] { "SetPlayer", "SetPlayerHealthController" })
            Check(globals.Methods.Single(m => m.Name == setter).IsStatic, "released global setter " + setter);
        var patch = dll.MainModule.GetType("ImprovedSelfcare.HealingAutoCancelPatch");
        var handler = patch.Methods.Single(m => m.Name == "ActiveHealthController_HealthChangedEvent");
        Check(handler.IsStatic && handler.IsPrivate && handler.ReturnType.FullName == "System.Void"
            && string.Join(",", handler.Parameters.Select(p => p.ParameterType.FullName)) == "EBodyPart,System.Single,EFT.Ballistics.DamageInfo", "released medical callback signature");
        var start = patch.Methods.Single(m => m.Name == "PostFix");
        Check(start.IsStatic && start.IsPrivate && start.Parameters.Count == 0
            && start.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "add_HealthChangedEvent"), "released start-only subscription");
        Check(!dll.MainModule.Types.SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions)
            .Any(i => i.Operand is Mono.Cecil.MethodReference m && m.Name == "remove_HealthChangedEvent"), "released mod never detaches its body event");
        using var hash = SHA256.Create(); Check(BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "")
            == "FA9478A151FDD797640F6B093C710227355ED0FAE56BFF8E3CEA7A9952081C11", "exact reviewed released bytes");
        using var eft = Mono.Cecil.AssemblyDefinition.ReadAssembly(nativePath);
        var health = eft.MainModule.GetType("EFT.HealthSystem.ActiveHealthController");
        Check(health.Events.Single(e => e.Name == "HealthChangedEvent").EventType.FullName
            == "System.Action`3<EBodyPart,System.Single,EFT.Ballistics.DamageInfo>", "native medical event signature");
        var readers = health.Methods.Where(m => m.HasBody && !m.IsAddOn && !m.IsRemoveOn && m.Body.Instructions.Any(i =>
            i.OpCode.Code == Mono.Cecil.Cil.Code.Ldfld && i.Operand is Mono.Cecil.FieldReference f && f.Name == "HealthChangedEvent")).ToArray();
        Check(readers.Length == 1 && readers[0].Name == "ChangeHealth"
            && string.Join(",", readers[0].Parameters.Select(p => p.ParameterType.FullName)) == "EBodyPart,System.Single,EFT.Ballistics.DamageInfo",
            "native ChangeHealth is the sole event emitter");
    }
    public static int Main(string[] args)
    {
        try
        {
            var mode = args.Length == 0 ? "normal" : args[0];
            if (mode == "published") { Published(args[1], args[2]); Console.WriteLine("PASS published " + _checks); return 0; }
            Check(HealingAutoCancelCompat.GetRejectionReason() == null, "absent plugin no-op");
            HealingAutoCancelCompat.Prepare(null, null); HealingAutoCancelCompat.Tick(); HealingAutoCancelCompat.AbortPreparation();
            HealingAutoCancelCompat.RetireWorld(null); Build(mode);
            var reason = HealingAutoCancelCompat.GetRejectionReason();
            if (mode == "drift" || mode == "older")
            { Check(reason != null, "unsupported contract rejected"); Check(HealingAutoCancelCompat.GetRejectionReason() == reason && Redeploy.Core.CoreLog.Warnings == 1, "bounded refusal warning"); Console.WriteLine("PASS " + mode + " " + _checks); return 0; }
            Check(reason == null, "valid released contract"); Check(Redeploy.Core.CoreLog.Warnings == (mode == "newer" ? 1 : 0), "newer version only warns");
            if (mode == "recovery")
            {
                var recovery = Raid(); var before = (LocalPlayer)recovery.LocalPlayer; _started.Invoke(null, null);
                BodyReplacementContext.Begin(recovery, before); HealingAutoCancelCompat.Prepare(recovery, before);
                recovery.GameWorld.UnregisterPlayer(before);
                // Recovery retains the exact old body and controller after physical
                // unregister. Its weak retired-source tombstone deliberately remains.
                recovery.GameWorld.RegisteredPlayers.Add(before); BodyReplacementContext.EndAttempt(); HealingAutoCancelCompat.Tick();
                Heal(before); Check(before.ActiveHealthController.Removed == 1 && ReferenceEquals(GlobalPlayer, before), "recovered old body heals despite retained source tombstone");
                var captured = (Action<EBodyPart, float, DamageInfo>)before.ActiveHealthController.Callbacks.Single();
                BodyReplacementContext.Begin(recovery, before); HealingAutoCancelCompat.Prepare(recovery, before);
                HealingAutoCancelCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation(); Heal(before);
                Check(before.ActiveHealthController.Removed == 2, "cancelled retry preserves recovered body's healing");
                BodyReplacementContext.Begin(recovery, before); HealingAutoCancelCompat.Prepare(recovery, before); recovery.GameWorld.UnregisterPlayer(before);
                var after = Commit(recovery, before); Heal(after); captured(EBodyPart.Chest, 1, Medicine);
                Check(after.ActiveHealthController.Removed == 1 && before.ActiveHealthController.Callbacks.Length == 0, "later replacement fences recovered body's captured callback");
                Console.WriteLine("PASS recovery " + _checks); return 0;
            }
            if (mode == "entry")
            {
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    var entry = Raid(); var before = (LocalPlayer)entry.LocalPlayer; var previousGlobal = GlobalPlayer;
                    BodyReplacementContext.Begin(entry, before); HealingAutoCancelCompat.Prepare(entry, before); entry.GameWorld.UnregisterPlayer(before);
                    var after = Commit(entry, before); Check(ReferenceEquals(GlobalPlayer, previousGlobal), "entry kit does not initialize foreign globals prematurely");
                    _started.Invoke(null, null); Heal(after);
                    Check(ReferenceEquals(GlobalPlayer, after) && after.ActiveHealthController.Removed == 1 && after.ActiveHealthController.Callbacks.Length == 1,
                        "entry kit adopts native OnGameStarted initialization once");
                    _started.Invoke(null, null); Heal(after); Check(after.ActiveHealthController.Removed == 2 && after.ActiveHealthController.Callbacks.Length == 1,
                        "repeated foreign initialization does not duplicate owned callback");
                    HealingAutoCancelCompat.RetireWorld(entry.GameWorld); BodyReplacementContext.Reset();
                }
                Console.WriteLine("PASS entry " + _checks); return 0;
            }
            var game = Raid(); var old = (LocalPlayer)game.LocalPlayer; _started.Invoke(null, null); Heal(old);
            Check(old.ActiveHealthController.Removed == 1 && old.ActiveHealthController.Callbacks.Length == 1, "ordinary initial raid unchanged");
            BodyReplacementContext.Begin(game, old); HealingAutoCancelCompat.Prepare(game, old);
            var targets = (IEnumerable<MethodInfo>)typeof(HealingAutoCancelCompat).GetField("UnregisterTargets", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            Check(targets.Any(m => m.DeclaringType == typeof(ClientWorld)) && targets.All(m => m.ReflectedType == m.DeclaringType),
                "inherited runtime unregister normalized to declared implementation");
            Check(ReferenceEquals(GlobalPlayer, old) && old.ActiveHealthController.Callbacks.Length == 1, "Prepare does not mutate foreign state");
            HealingAutoCancelCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation(); Heal(old);
            Check(old.ActiveHealthController.Removed == 2, "cancelled attempt leaves ordinary healing functional");
            BodyReplacementContext.Begin(game, old); HealingAutoCancelCompat.Prepare(game, old);
            var other = new LocalPlayer { GameWorld = game.GameWorld, Profile = old.Profile }; game.GameWorld.UnregisterPlayer(other);
            Check(old.ActiveHealthController.Callbacks.Length == 1, "same-profile unrelated unregister ignored");
            new GameWorld().UnregisterPlayer(old); Check(old.ActiveHealthController.Callbacks.Length == 1, "wrong world unregister ignored");
            game.GameWorld.UnregisterPlayer(old); Check(old.ActiveHealthController.Callbacks.Length == 0, "actual old body subscription detached");
            old.ActiveHealthController.HealthChangedEvent += _callback; Heal(old);
            Check(old.ActiveHealthController.Removed == 2, "retired native source blocked before next body commits");
            old.ActiveHealthController.HealthChangedEvent -= _callback;
            var next = Commit(game, old); Heal(next);
            Check(ReferenceEquals(GlobalPlayer, next) && ReferenceEquals(GlobalHealth, next.ActiveHealthController) && next.ActiveHealthController.Removed == 1, "committed replacement medical event works");
            old.ActiveHealthController.HealthChangedEvent += _callback;
            BeforeForeignHandler = () => Heal(old); Heal(next);
            Check(next.ActiveHealthController.Removed == 2, "nested old source inside owned handler cannot redirect to replacement");
            old.ActiveHealthController.HealthChangedEvent -= _callback;
            next.ActiveHealthController.Removed = 1;
            _callback(EBodyPart.Chest, 1, Medicine); Check(next.ActiveHealthController.Removed == 1, "foreign captured static callback cannot redirect to replacement");
            var saved = (Action<EBodyPart, float, DamageInfo>)next.ActiveHealthController.Callbacks.Single();
            HealingAutoCancelCompat.Tick(); Heal(next); Check(next.ActiveHealthController.Removed == 2 && next.ActiveHealthController.Callbacks.Length == 1, "repeat ticks do not duplicate medical callback");
            next.ActiveHealthController.Full = false; Heal(next); Check(next.ActiveHealthController.Removed == 2, "incomplete healing continues");
            next.ActiveHealthController.Full = true; next.ActiveHealthController.Bleeding = true; Heal(next); Check(next.ActiveHealthController.Removed == 2, "bleeding continues treatment");
            next.Resource = 0; Heal(next); Check(next.ActiveHealthController.Removed == 3, "empty medical resource retains original cancellation");
            next.Resource = 100; next.ActiveHealthController.Bleeding = false;
            BodyReplacementContext.Begin(game, next); HealingAutoCancelCompat.Prepare(game, next); game.GameWorld.UnregisterPlayer(next);
            var third = Commit(game, next, scav: true, publish: false); Heal(third);
            Check(ReferenceEquals(GlobalPlayer, third) && third.ActiveHealthController.Removed == 1, "failure-recovery commit and PMC-to-Scav bind through Tick");
            saved(EBodyPart.Chest, 1, Medicine); Check(third.ActiveHealthController.Removed == 1, "late old closure cannot heal new body");
            HealingAutoCancelCompat.RetireWorld(new GameWorld()); Heal(third); Check(third.ActiveHealthController.Removed == 2, "unrelated world retirement ignored");
            new Harmony("com.maschine.Redeploy.Core.HealingAutoCancelCompat").Unpatch(_started, HarmonyPatchType.Postfix, "com.maschine.Redeploy.Core.HealingAutoCancelCompat");
            Check(HealingAutoCancelCompat.GetRejectionReason() != null && third.ActiveHealthController.Callbacks.Length == 1,
                "late hook drift refuses without clearing owned event");
            Heal(third); saved(EBodyPart.Chest, 1, Medicine);
            Check(third.ActiveHealthController.Removed == 3, "remaining owned callback and stale-body fences survive refusal");
            var savedThird = (Action<EBodyPart, float, DamageInfo>)third.ActiveHealthController.Callbacks.Single();
            HealingAutoCancelCompat.RetireWorld(game.GameWorld); BodyReplacementContext.Reset();
            Check(third.ActiveHealthController.Callbacks.Length == 0, "world retirement detaches owned event");
            var ordinaryGame = Raid(); var ordinary = ordinaryGame.LocalPlayer; _started.Invoke(null, null); Heal(ordinary);
            Check(ordinary.ActiveHealthController.Removed == 1, "later ordinary raid healing remains functional");
            var bot = new Player { IsYourPlayer = false, GameWorld = ordinaryGame.GameWorld, Profile = ordinary.Profile };
            bot.ActiveHealthController.HealthChangedEvent += _callback; Heal(bot);
            Check(ordinary.ActiveHealthController.Removed == 1 && bot.ActiveHealthController.Removed == 0, "unrelated bot medical source cannot redirect into local player");
            savedThird(EBodyPart.Chest, 1, Medicine); Check(ordinary.ActiveHealthController.Removed == 1, "old closure fenced after next ordinary raid");
            _callback(EBodyPart.Chest, 1, Medicine); Check(ordinary.ActiveHealthController.Removed == 1, "unattributed captured static callback cannot touch later ordinary raid");
            // Model an event snapshot captured before unregister but dispatched after
            // the globals have been reassigned by a later ordinary raid.
            old.ActiveHealthController.HealthChangedEvent += _callback; Heal(old);
            Check(ordinary.ActiveHealthController.Removed == 1, "retired native source cannot touch later ordinary raid");
            ordinary.ActiveHealthController.BeforeDispatch = () => Heal(old); Heal(ordinary);
            Check(ordinary.ActiveHealthController.Removed == 2, "nested native source restored before outer dispatch");
            ordinary.ActiveHealthController.BeforeDispatch = () => throw new InvalidOperationException("test");
            try { Heal(ordinary); } catch (InvalidOperationException) { } ordinary.ActiveHealthController.BeforeDispatch = null; Heal(ordinary);
            Check(ordinary.ActiveHealthController.Removed == 3, "exception restores native event source");
            new Harmony("com.maschine.Redeploy.Core.HealingAutoCancelCompat").UnpatchSelf(); Check(HealingAutoCancelCompat.GetRejectionReason() != null, "removed hook rejects next replacement");
            Chainloader.PluginInfos.Clear(); Check(HealingAutoCancelCompat.GetRejectionReason() != null, "mid-process plugin disappearance refuses safely");
            Console.WriteLine("PASS " + mode + " " + _checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

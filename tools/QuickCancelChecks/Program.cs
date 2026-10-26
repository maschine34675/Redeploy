using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Redeploy.Core.Compat;

internal static class Program
{
    private static int _checks;
    private static MethodInfo _mark;
    private static HashSet<Player.MedsController> _pending;
    private static void Check(bool yes, string why) { if (!yes) throw new Exception(why); _checks++; }
    private static bool Mark(Player.MedsController controller) => (bool)_mark.Invoke(null, new object[] { controller });
    private static void Build(string mode)
    {
        var version = mode == "newer" ? "2.0.1" : mode == "older" ? "1.0.0" : "2.0.0";
        var asm = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("QuickCancelClient"), AssemblyBuilderAccess.Run);
        var module = asm.DefineDynamicModule("test");
        var plugin = module.DefineType("Manimal.QuickCancel.Plugin", TypeAttributes.Public, typeof(BaseUnityPlugin));
        plugin.SetCustomAttribute(new CustomAttributeBuilder(typeof(BepInPlugin).GetConstructor(new[] { typeof(string), typeof(string), typeof(string) }), new object[] { QuickCancelCompat.PluginGuid, "Manimal Quick Cancel", version }));
        var runtime = plugin.CreateType();
        var state = module.DefineType("Manimal.QuickCancel.QuickCancelState", TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed);
        var field = state.DefineField(mode == "drift" ? "ChangedPending" : "PendingControllers", typeof(HashSet<Player.MedsController>), FieldAttributes.Static | FieldAttributes.Private | FieldAttributes.InitOnly);
        var ctor = state.DefineTypeInitializer(); var il = ctor.GetILGenerator(); il.Emit(OpCodes.Newobj, typeof(HashSet<Player.MedsController>).GetConstructor(Type.EmptyTypes)); il.Emit(OpCodes.Stsfld, field); il.Emit(OpCodes.Ret);
        var mark = state.DefineMethod("Mark", MethodAttributes.Assembly | MethodAttributes.Static, typeof(bool), new[] { typeof(Player.MedsController) });
        mark.SetImplementationFlags(MethodImplAttributes.NoInlining); il = mark.GetILGenerator(); il.Emit(OpCodes.Ldsfld, field); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Callvirt, typeof(HashSet<Player.MedsController>).GetMethod("Add")); il.Emit(OpCodes.Ret);
        var resolved = state.CreateType(); _mark = resolved.GetMethod("Mark", BindingFlags.Static | BindingFlags.NonPublic); _pending = (HashSet<Player.MedsController>)resolved.GetField(field.Name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        Chainloader.PluginInfos[QuickCancelCompat.PluginGuid] = new PluginInfo { Instance = (BaseUnityPlugin)Activator.CreateInstance(runtime), Metadata = runtime.GetCustomAttribute<BepInPlugin>() };
    }
    private static void Published(string path)
    {
        using var dll = Mono.Cecil.AssemblyDefinition.ReadAssembly(path);
        Check(dll.Name.Name == "QuickCancelClient", "release assembly identity");
        var plugin = dll.MainModule.GetType("Manimal.QuickCancel.Plugin");
        var metadata = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        Check((string)metadata.ConstructorArguments[0].Value == QuickCancelCompat.PluginGuid && (string)metadata.ConstructorArguments[2].Value == "2.0.0", "release plugin identity/version");
        var state = dll.MainModule.GetType("Manimal.QuickCancel.QuickCancelState");
        var field = state.Fields.Single(f => f.Name == "PendingControllers");
        Check(field.IsStatic && field.IsInitOnly && field.FieldType.FullName == "System.Collections.Generic.HashSet`1<EFT.Player/MedsController>", "released strong pending-controller storage");
        var mark = state.Methods.Single(m => m.Name == "Mark");
        Check(mark.IsStatic && mark.ReturnType.FullName == "System.Boolean" && mark.Parameters.Single().ParameterType.FullName == "EFT.Player/MedsController", "release Mark API");
        var removers = dll.MainModule.Types.SelectMany(t => t.Methods.Concat(t.NestedTypes.SelectMany(n => n.Methods))).Where(m => m.HasBody && m.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference mr && mr.DeclaringType.FullName == state.FullName && mr.Name == "Remove")).Select(m => m.FullName).ToArray();
        Check(removers.Any(n => n.Contains("MedsControllerDropPatch")) && !removers.Any(n => n.Contains("Destroy")), "released Remove does not cover native destruction");
        using var hash = SHA256.Create(); var actual = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        Check(actual == "3C6C87112A7C0739C505A7F204DB2E3F13932DFDC8C568519E34B8BF685463BC", "reviewed release bytes");
    }
    public static int Main(string[] args)
    {
        try
        {
            var mode = args.Length == 0 ? "normal" : args[0];
            if (mode == "published") { Published(args[1]); Console.WriteLine("PASS published " + _checks); return 0; }
            Check(QuickCancelCompat.GetRejectionReason() == null, "absent plugin no-op"); Build(mode);
            var issue = QuickCancelCompat.GetRejectionReason();
            if (mode == "drift" || mode == "older") { Check(issue != null, "invalid contract rejected"); Check(QuickCancelCompat.GetRejectionReason() == issue && Redeploy.Core.CoreLog.Warnings == 1, "bounded refusal warning"); Console.WriteLine("PASS " + mode + " " + _checks); return 0; }
            Check(issue == null, "supported contract");
            Check(Redeploy.Core.CoreLog.Warnings == (mode == "newer" ? 1 : 0), "newer versions use APIs with warning");
            var world = new ClientWorld(); var old = new LocalPlayer { GameWorld = world };
            var game = new LocalGame { GameWorld = world, LocalPlayer = old, Profile = old.Profile }; world.MainPlayer = old; Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
            var meds = new Player.MedsController { _player = old };
            var foreignBody = new LocalPlayer { GameWorld = world, Profile = old.Profile }; var foreignMeds = new Player.MedsController { _player = foreignBody };
            Check(Mark(meds) && Mark(foreignMeds), "normal consumable cancellation unchanged");
            // Native ExecuteAsync yields; dead TryRemoveFromHands succeeds without
            // MedsController.Drop, so Quick Cancel never removes the pending entry.
            world.UnregisterPlayer(old); Check(_pending.Contains(meds), "unowned ordinary death is not rewritten");
            BodyReplacementContext.Begin(game, old); QuickCancelCompat.Prepare(game, old);
            Check(_pending.Contains(meds), "Prepare has no foreign mutation");
            BodyReplacementContext.CancelPreparation(); world.UnregisterPlayer(old); Check(_pending.Contains(meds), "refused preparation remains ordinary");
            BodyReplacementContext.Begin(game, old); QuickCancelCompat.Prepare(game, old);
            new GameWorld().UnregisterPlayer(old); Check(_pending.Contains(meds), "wrong world cannot retire body");
            world.UnregisterPlayer(foreignBody); Check(_pending.Contains(foreignMeds), "same-profile unrelated body untouched");
            world.UnregisterPlayer(old); Check(!_pending.Contains(meds) && _pending.Contains(foreignMeds), "actual derived unregister removes only retired physical controller");
            Check(!Mark(meds) && !_pending.Contains(meds), "delayed retired-owner command cannot pin old body again");
            var next = new LocalPlayer { GameWorld = world, Profile = old.Profile }; world.MainPlayer = next;
            Check(BodyReplacementContext.TryOwnCandidate(next), "replacement physical ownership"); game.LocalPlayer = next; BodyReplacementContext.EndAttempt();
            var nextMeds = new Player.MedsController { _player = next }; Check(Mark(nextMeds), "next body can quick-cancel");
            _pending.Remove(nextMeds); Check(Mark(nextMeds), "normal Drop/failure cleanup permits repeat action");
            QuickCancelCompat.RetireWorld(new GameWorld()); Check(_pending.Contains(nextMeds), "unrelated world cleanup no-op");
            QuickCancelCompat.RetireWorld(world); Check(!_pending.Contains(nextMeds) && _pending.Contains(foreignMeds), "world cleanup retires only Core bodies");
            BodyReplacementContext.Reset(); Check(!Mark(nextMeds), "late callback remains fenced after world reset");
            Check(_pending.Remove(foreignMeds) && Mark(foreignMeds), "ordinary non-Core controller works after cleanup");
            new Harmony("com.maschine.Redeploy.Core.QuickCancelCompat").UnpatchSelf(); Check(QuickCancelCompat.GetRejectionReason() != null, "missing hooks reject replacement");
            Chainloader.PluginInfos.Clear(); Check(QuickCancelCompat.GetRejectionReason() == null, "optional removal no-op");
            Console.WriteLine("PASS " + mode + " " + _checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

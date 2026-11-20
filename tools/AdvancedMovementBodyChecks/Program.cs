using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Redeploy.Core.Compat;
using AdvancedMovement.Moves;
using AdvancedMovement.Viewmodel;
using AdvancedMovement.Body;
using AdvancedMovement.Audio;
using AdvancedMovement.States;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static void Check(bool yes, string why) { if (!yes) throw new Exception(why); _checks++; }
    private static (LocalGame game, LocalPlayer body) World(bool callsBase = false)
    {
        var world = callsBase ? (GameWorld)new BaseCallingWorld() : new ClientWorld();
        var body = new LocalPlayer { GameWorld = world };
        var game = new LocalGame { GameWorld = world, LocalPlayer = body, Profile = body.Profile };
        world.MainPlayer = body; Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
        return (game, body);
    }
    private static void Arm(LocalPlayer body, bool dive = false)
    {
        var ctx = body.MovementContext;
        MoveController._player = body;
        MoveController._move = dive ? null : new SlideState { MovementContext = ctx };
        MoveController._dive = dive ? new DiveState { MovementContext = ctx } : null;
        ctx.CurrentState = ctx.OverridenState = (BaseMovementState)MoveController._move ?? MoveController._dive;
        ctx.SpeedLimit = 20; MoveController.HoldStill = true; MoveController._settleUntil = 25; MoveController._settleLogged = false;
        SteadyWeapon._lastMoveTime = 10;
        AirSlide._player = body; AirSlide._queued = AirSlide._wasInAir = true; AirSlide._airVelocity = new Vector3 { x = 12 }; AirSlide._queuedAt = 10;
        SlideJump._context = ctx; SlideJump._armedAt = 10; SlideJump._velocity = new Vector3 { x = 10 };
        GesturePlayer._player = body; GesturePlayer.Active = true;
        BodyPlayer._local = new BodyAnimator(body);
        WeaponPin._firearm = new Player.FirearmController { _player = body }; WeaponPin.Pinned = true;
        SlideAudio._player = body; SlideAudio._go = new GameObject(); SlideAudio._loop = new AudioSource(); SlideAudio._shots = new AudioSource();
        SlideAudio._active = true; SlideAudio._easing = false;
    }
    private static void Prepare(LocalGame game, LocalPlayer body)
    { BodyReplacementContext.Begin(game, body); AdvancedMovementBodyCompatibility.Prepare(game, body); }
    private static LocalPlayer Replace(LocalGame game, LocalPlayer old)
    {
        var next = new LocalPlayer { GameWorld = game.GameWorld, Profile = old.Profile };
        game.GameWorld.MainPlayer = next;
        Check(BodyReplacementContext.TryOwnCandidate(next), "replacement tracked by exact Core context");
        game.LocalPlayer = next; AdvancedMovementBodyCompatibility.AbortPreparation(); BodyReplacementContext.EndAttempt();
        return next;
    }
    private static void Normal()
    {
        var (game, body) = World(); var world = game.GameWorld;
        var other = new LocalPlayer { GameWorld = world, Profile = body.Profile };
        var remote = new BodyAnimator(other); BodyPlayer.Remote[other] = remote;
        Arm(body);
        world.UnregisterPlayer(body); Check(MoveController.Busy, "ordinary unregister untouched");
        Prepare(game, body); Check(MoveController.Busy && SlideAudio._active, "read-only Prepare");
        AdvancedMovementBodyCompatibility.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        world.UnregisterPlayer(body); Check(MoveController.Busy, "cancelled preparation untouched");
        Prepare(game, body); AdvancedMovementBodyCompatibility.EndAttempt();
        world.UnregisterPlayer(body); Check(MoveController.Busy, "early failed attempt releases provisional ownership");
        BodyReplacementContext.EndAttempt();
        Prepare(game, body);
        new GameWorld().UnregisterPlayer(body); Check(MoveController.Busy, "different world untouched");
        world.UnregisterPlayer(other); Check(MoveController.Busy, "same-profile different body untouched");
        var audio = SlideAudio._go; var loop = SlideAudio._loop; var shots = SlideAudio._shots; var animator = BodyPlayer._local;
        world.UnregisterPlayer(body);
        Check(!MoveController.Busy && MoveController._player == null, "retired slide clears global busy");
        Check(body.MovementContext.Exits == 1 && body.MovementContext.SpeedLimit == 5 && body.MovementContext.OverridenState == null, "only old override exits and restores speed");
        Check(!MoveController.HoldStill && MoveController._settleUntil < 0 && MoveController._settleLogged, "old prone settle cleared");
        Check(SteadyWeapon._lastMoveTime < 0, "old movement weapon steadiness cleared");
        Check(!AirSlide._queued && !AirSlide._wasInAir && AirSlide._player == null && AirSlide._airVelocity.x == 0, "old queued landing cleared");
        Check(SlideJump._context == null && SlideJump._armedAt < 0 && SlideJump._velocity.x == 0, "old slide jump carry cleared");
        Check(!GesturePlayer.Active && GesturePlayer.Stops == 1 && GesturePlayer.Restores == 1, "old gesture restored before stopped");
        Check(!animator.Active && animator.Stops == 1 && BodyPlayer._local == null, "old local body animation stopped");
        Check(remote.Active && BodyPlayer.Remote[other] == remote, "remote body animation untouched");
        Check(!WeaponPin.Pinned && WeaponPin.Clears == 1 && WeaponPin.Restores == 1, "old firearm pin restored and cleared");
        Check(!SlideAudio._active && SlideAudio._player == null && audio.Destroyed && loop.Stops == 1 && shots.Stops == 1, "old audio stopped synchronously and released");
        world.UnregisterPlayer(body); Check(body.MovementContext.Exits == 1 && animator.Stops == 1, "duplicate unregister is idempotent");
        var next = Replace(game, body); Arm(next, dive: true);
        world.UnregisterPlayer(body); Check(MoveController.Busy && MoveController._player == next && SlideAudio._player == next, "late old unregister preserves replacement move");
        Prepare(game, next); world.UnregisterPlayer(next);
        Check(!MoveController.Busy && next.MovementContext.Exits == 1 && next.MovementContext.SpeedLimit == 5, "live dive retires on second body");
        var third = Replace(game, next); Arm(third);
        // A foreign override installed after the slide must not be cancelled.
        var foreign = new BaseMovementState { MovementContext = third.MovementContext };
        third.MovementContext.OverridenState = third.MovementContext.CurrentState = foreign;
        Prepare(game, third); world.UnregisterPlayer(third);
        Check(third.MovementContext.OverridenState == foreign && third.MovementContext.Exits == 0, "foreign override untouched");
        Replace(game, third);
        var (baseGame, baseBody) = World(callsBase: true); Arm(baseBody); Prepare(baseGame, baseBody);
        var before = GesturePlayer.Stops; baseGame.GameWorld.UnregisterPlayer(baseBody);
        Check(GesturePlayer.Stops == before + 1, "base plus override cleans only once");
        // No candidate created: failed creation must not leave global Busy blocking input.
        AdvancedMovementBodyCompatibility.AbortPreparation(); BodyReplacementContext.EndAttempt();
        Check(!MoveController.Busy, "failed creation leaves no old global movement state");
        Arm(baseBody); Prepare(baseGame, baseBody);
        AdvancedMovementBodyCompatibility.RetireWorld(new GameWorld());
        baseGame.GameWorld.UnregisterPlayer(baseBody); Check(!MoveController.Busy, "unrelated world cleanup does not cancel current preparation");
        AdvancedMovementBodyCompatibility.AbortPreparation(); BodyReplacementContext.EndAttempt();
        Arm(baseBody); Prepare(baseGame, baseBody);
        AdvancedMovementBodyCompatibility.RetireWorld(baseGame.GameWorld);
        baseGame.GameWorld.UnregisterPlayer(baseBody); Check(MoveController.Busy, "world teardown drops only provisional ownership");
        AdvancedMovementBodyCompatibility.AbortPreparation(); BodyReplacementContext.EndAttempt();
        // Source death path clears move/dive itself, but leaves old owner and ancillary state.
        Prepare(baseGame, baseBody); MoveController._move = null; MoveController._dive = null;
        baseGame.GameWorld.UnregisterPlayer(baseBody);
        Check(MoveController._player == null && !GesturePlayer.Active && !SlideAudio._active, "native death partial cleanup still retires residual state");
        Replace(baseGame, baseBody);
        // Several independent owners already moved on: old callback must not clear those.
        var (mixedGame, mixedOld) = World(); var mixedNew = new LocalPlayer { GameWorld = mixedGame.GameWorld, Profile = mixedOld.Profile };
        Arm(mixedOld); var oldMove = MoveController._move;
        Arm(mixedNew); MoveController._player = mixedOld; MoveController._move = oldMove;
        Prepare(mixedGame, mixedOld); mixedGame.GameWorld.UnregisterPlayer(mixedOld);
        Check(!MoveController.Busy, "mixed state retires only old move");
        Check(AirSlide._player == mixedNew && AirSlide._queued && SlideJump._context == mixedNew.MovementContext, "new queued state preserved");
        Check(GesturePlayer._player == mixedNew && GesturePlayer.Active && BodyPlayer._local.Player == mixedNew && BodyPlayer._local.Active, "new gesture and animator preserved");
        Check(WeaponPin.Pinned && WeaponPin._firearm._player == mixedNew && SlideAudio._player == mixedNew && SlideAudio._active, "new firearm and audio preserved");
        Check(SteadyWeapon._lastMoveTime == 10, "new animation steadiness preserved");
        AdvancedMovementBodyCompatibility.AbortPreparation(); BodyReplacementContext.EndAttempt();
    }
    private static void Failure()
    {
        var (game, body) = World(); Arm(body); Prepare(game, body);
        body.MovementContext.ThrowOnExit = true; GesturePlayer.ThrowOnRestore = true;
        game.GameWorld.UnregisterPlayer(body);
        Check(!MoveController.Busy && !GesturePlayer.Active, "failed native cleanup still stops global busy and gesture");
        Check(!SlideAudio._active && !WeaponPin.Pinned && BodyPlayer._local == null, "independent resources cleaned after earlier failure");
        Check(AdvancedMovementBodyCompatibility.GetRejectionReason() != null && Redeploy.Core.CoreLog.Warnings == 1, "partial cleanup fails admission with bounded warning");
    }
    private static void Published(string path)
    {
        using var dll = Mono.Cecil.AssemblyDefinition.ReadAssembly(path);
        Check(dll.Name.Name == "AdvancedMovement", "release assembly identity");
        var plugin = dll.MainModule.GetType("AdvancedMovement.Plugin");
        var metadata = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        Check((string)metadata.ConstructorArguments[0].Value == AdvancedMovementBodyCompatibility.PluginGuid && (string)metadata.ConstructorArguments[2].Value == "1.0.1", "release plugin identity/version");
        // Compare every reflected adapter field and callable against the installed release's metadata.
        var contract = typeof(AdvancedMovementBodyCompatibility).GetNestedType("Contract", BindingFlags.NonPublic);
        var value = Activator.CreateInstance(contract, BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { typeof(AdvancedMovement.Plugin).Assembly }, null);
        foreach (var f in contract.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            if (f.GetValue(value) is FieldInfo foreign)
            {
                var type = dll.MainModule.GetType(foreign.DeclaringType.FullName);
                var actual = type.Fields.Single(x => x.Name == foreign.Name);
                Check(actual.IsStatic == foreign.IsStatic && !actual.IsInitOnly && actual.FieldType.FullName.Replace('/', '+') == foreign.FieldType.FullName, "released field contract " + foreign.DeclaringType.FullName + "." + foreign.Name);
            }
            if (f.GetValue(value) is MethodInfo method)
            {
                var actual = dll.MainModule.GetType(method.DeclaringType.FullName).Methods.Single(x => x.Name == method.Name && x.Parameters.Count == 0);
                Check(actual.HasBody && actual.IsStatic == method.IsStatic && actual.ReturnType.FullName == "System.Void", "released method contract " + method.Name);
            }
            if (f.GetValue(value) is PropertyInfo property)
            {
                var actual = dll.MainModule.GetType(property.DeclaringType.FullName).Properties.Single(x => x.Name == property.Name);
                Check(actual.GetMethod.HasBody && !actual.GetMethod.IsStatic && actual.PropertyType.FullName == "EFT.Player", "released animator physical owner");
            }
        }
        foreach (var state in new[] { "ProfileMoveState", "DiveState" })
        {
            var exit = dll.MainModule.GetType("AdvancedMovement.States." + state).Methods.Single(m => m.Name == "Exit");
            Check(exit.Body.Instructions.Any(i => i.Operand is Mono.Cecil.FieldReference fr && fr.Name == "SavedSpeedLimit")
                && exit.Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "set_SpeedLimit"), "released state restores saved speed: " + state);
        }
        Check(!dll.MainModule.GetType("AdvancedMovement.Moves.MoveController").Methods.Single(m => m.Name == "Update").Body.Instructions.Any(i => i.Operand is Mono.Cecil.MethodReference mr && mr.Name == "get_MainPlayer"), "released move update does not own current player identity");
    }
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "published") { Published(args[1]); Console.WriteLine("ADVANCED_MOVEMENT_PUBLISHED_CONTRACT_PASSED " + _checks); return 0; }
            Check(AdvancedMovementBodyCompatibility.GetRejectionReason() == null, "absent plugin no-op");
            Chainloader.PluginInfos[AdvancedMovementBodyCompatibility.PluginGuid] = new PluginInfo
            { Instance = new AdvancedMovement.Plugin(), Metadata = typeof(AdvancedMovement.Plugin).GetCustomAttribute<BepInPlugin>() };
            Check(AdvancedMovementBodyCompatibility.GetRejectionReason() == null, "reviewed plugin contract accepted");
            if (args.Length > 0 && args[0] == "older")
            {
                Chainloader.PluginInfos[AdvancedMovementBodyCompatibility.PluginGuid].Metadata = new BepInPlugin(AdvancedMovementBodyCompatibility.PluginGuid, "Advanced Movement", "1.0.0");
                Check(AdvancedMovementBodyCompatibility.GetRejectionReason() != null, "older contract rejected before mutation");
                Check(AdvancedMovementBodyCompatibility.GetRejectionReason() != null && Redeploy.Core.CoreLog.Warnings == 1, "repeated invalid admission warning bounded");
            }
            else if (args.Length > 0 && args[0] == "failure") Failure(); else Normal();
            Chainloader.PluginInfos.Clear();
            Check(AdvancedMovementBodyCompatibility.GetRejectionReason() == null, "optional removal cleans bounded hooks");
            Check(Harmony.GetPatchInfo(typeof(GameWorld).GetMethod(nameof(GameWorld.UnregisterPlayer)))?.Prefixes.All(x => x.owner != "com.maschine.Redeploy.Core.AdvancedMovementCompat") != false, "base hook removed on optional absence");
            Console.WriteLine("ADVANCED_MOVEMENT_BODY_CHECKS_PASSED " + _checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Redeploy.Core.Compat;
using Foreign = DynamicExternalResolution.DynamicExternalResolution;
using Callbacks = DynamicExternalResolution.DynamicExternalResolutionPatches;

internal static class Program
{
    private static int _checks;
    private static void Check(bool yes, string message) { if (!yes) throw new Exception(message); _checks++; }
    private static LocalPlayer Body(GameWorld world, Profile profile = null)
    {
        var body = new LocalPlayer { GameWorld = world, Profile = profile ?? new Profile() };
        var gun = new Player.FirearmController(); body.HandsController = gun;
        body.Optics.Add(new OpticSight { transform = { parent = gun.ControllerGameObject.transform } });
        return body;
    }
    private static void Begin(LocalGame game)
    { BodyReplacementContext.Begin(game, game.LocalPlayer); DynamicExternalResolutionCompat.Prepare(game, game.LocalPlayer); }
    private static LocalPlayer Commit(LocalGame game, Profile differentProfile = null)
    {
        var next = Body(game.GameWorld, differentProfile ?? game.Profile);
        game.GameWorld.MainPlayer = next;
        Check(BodyReplacementContext.TryOwnCandidate(next), "candidate owns physical replacement");
        game.LocalPlayer = next; if (differentProfile != null) game.Profile = differentProfile;
        DynamicExternalResolutionCompat.EndAttempt(); BodyReplacementContext.EndAttempt();
        return next;
    }
    public static int Main()
    {
        try
        {
            Check(DynamicExternalResolutionCompat.GetRejectionReason() == null, "absent is inert");
            Callbacks.Patch();
            Chainloader.PluginInfos[DynamicExternalResolutionContract.Guid] = new PluginInfo
            { Instance = new Foreign(), Metadata = new BepInPlugin(DynamicExternalResolutionContract.Guid, "DERP", "1.1.2") };
            Check(DynamicExternalResolutionCompat.GetRejectionReason() == null, "bind exact published layout");
            var world = new GameWorld(); var old = Body(world);
            var game = new LocalGame { GameWorld = world, LocalPlayer = old, Profile = old.Profile, Status = GameStatus.Running };
            world.MainPlayer = old; Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
            old.Optics[0].OnEnable();
            Check(ReferenceEquals(Foreign.Cached, old) && Callbacks.AimCalls == 1, "ordinary callbacks preserve upstream cache");
            var unrelated = Body(world, old.Profile); world.MainPlayer = unrelated;
            Check(ReferenceEquals(Foreign.getPlayerInstance(), old), "ordinary same-profile body remains untouched"); world.MainPlayer = old;

            Begin(game);
            Check(Foreign.getPlayerInstance() == null && ReferenceEquals(Foreign.Cached, old), "creation cannot expose cache or mutate it");
            old.Optics[0].OnEnable(); old.Optics[0].OnDisable();
            Check(Callbacks.AimCalls == 1 && Callbacks.ResetCalls == 0, "creation callbacks cannot write shared camera");
            DynamicExternalResolutionCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
            old.Optics[0].OnEnable(); Check(Callbacks.AimCalls == 2, "aborted preparation does not retire outgoing optic");

            var detachedOptic = new OpticSight();
            ((Player.FirearmController)old.HandsController).ControllerGameObject.Children.Add(detachedOptic);
            Begin(game); var next = Commit(game);
            Check(ReferenceEquals(Foreign.Cached, next), "private cache follows committed body without a later optic event");
            next.Optics[0].OnEnable(); Check(ReferenceEquals(Callbacks.Observed, next) && Callbacks.AimCalls == 3, "successor optics use successor animation");
            old.Optics[0].OnDisable(); detachedOptic.OnDisable(); ((Player.FirearmController)old.HandsController).ChangeAimingMode();
            Check(Callbacks.AimCalls == 3 && Callbacks.ResetCalls == 0, "delayed retired body callbacks cannot change successor camera");
            next.ProceduralWeaponAnimation.ScopeAimTransforms.Clear();
            new OpticSight().OnEnable(); ((Player.FirearmController)next.HandsController).ChangeAimingMode();
            Check(Callbacks.AimCalls == 3, "empty hands cannot reach CurrentScope indexing");
            next.Optics[0].OnDisable(); Check(Callbacks.ResetCalls == 1, "live optic exit still restores normal resolution with empty scopes");
            next.ProceduralWeaponAnimation.ScopeAimTransforms.Add(new object());
            next.ProceduralWeaponAnimation.AimIndex = -1;
            new OpticSight().OnEnable(); Check(Callbacks.AimCalls == 3, "negative aim index is not a ready native scope");
            next.ProceduralWeaponAnimation.AimIndex = 0; next.ProceduralWeaponAnimation.ScopeAimTransforms[0] = null;
            new OpticSight().OnEnable(); Check(Callbacks.AimCalls == 3, "null selected scope is not native readiness");
            next.ProceduralWeaponAnimation.ScopeAimTransforms[0] = new object();
            new OpticSight().OnEnable(); Check(Callbacks.AimCalls == 4, "unrelated bot optics retain upstream behavior");
            game.LocalPlayer = unrelated;
            Check(Foreign.getPlayerInstance() == null, "split committed owners reject same-profile unrelated body"); game.LocalPlayer = next;
            Begin(game); DynamicExternalResolutionCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
            next.Optics[0].OnEnable(); Check(Callbacks.AimCalls == 5, "cancel after earlier respawn keeps current optic usable");

            var scavProfile = new Profile(); BodyReplacementContext.Begin(game, next, scavProfile);
            DynamicExternalResolutionCompat.Prepare(game, next); var scav = Commit(game, scavProfile);
            Check(ReferenceEquals(Foreign.Cached, scav), "own-Scav follows physical body with different profile");
            old.Optics[0].transform.parent = ((Player.FirearmController)scav.HandsController).ControllerGameObject.transform;
            old.Optics[0].OnEnable(); Check(Callbacks.AimCalls == 6, "pooled optic legitimately reused by current gun loses tombstone");
            DynamicExternalResolutionCompat.RetireWorld(new GameWorld()); Check(ReferenceEquals(Foreign.Cached, scav), "unrelated retirement preserves cache");
            DynamicExternalResolutionCompat.RetireWorld(world); BodyReplacementContext.Reset();
            Check(Foreign.Cached == null, "owned world retirement releases private cached player");
            var world2 = new GameWorld(); var entry = Body(world2); world2.MainPlayer = entry;
            Singleton<GameWorld>.Instance = world2; Singleton<AbstractGame>.Instance = new LocalGame { GameWorld = world2, LocalPlayer = entry, Profile = entry.Profile };
            next.Optics[0].OnDisable(); Check(Callbacks.ResetCalls == 1, "late retired optic cannot reset next raid");
            entry.Optics[0].OnEnable(); Check(ReferenceEquals(Foreign.Cached, entry) && Callbacks.AimCalls == 7, "subsequent ordinary raid keeps upstream behavior");
            // Missing native hooks are checked at the replacement boundary, not in
            // repeatedly evaluated menu admission. The failed bind stays cached.
            new Harmony("fixture.derp").UnpatchSelf();
            Check(DynamicExternalResolutionCompat.GetRejectionReason() == null, "cached admission performs no repeated Harmony inspection");
            bool rejected = false;
            try { BodyReplacementContext.Begin((LocalGame)Singleton<AbstractGame>.Instance, entry); DynamicExternalResolutionCompat.Prepare((LocalGame)Singleton<AbstractGame>.Instance, entry); }
            catch (MissingMethodException) { rejected = true; }
            Check(rejected, "replacement boundary detects missing native hook");
            DynamicExternalResolutionCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
            Chainloader.PluginInfos.Clear(); Check(DynamicExternalResolutionCompat.GetRejectionReason() == null, "plugin removal unpatches once");
            Check(DynamicExternalResolutionCompat.GetRejectionReason() == null, "subsequent absent admission is inert");
            Console.WriteLine("DERP_CHECKS_PASSED " + _checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

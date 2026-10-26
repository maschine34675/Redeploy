using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    internal static void Check(bool yes, string reason) { if (!yes) throw new Exception(reason); _checks++; }
    private static Camera NewCamera()
    {
        var go = new GameObject { name = "FPS Camera" }; var camera = go.Add<Camera>(); go.Add<PrismEffects>(); return camera;
    }
    private static void SetCamera(Camera camera, Player owner)
    {
        Camera.main = camera; CameraManager.Instance = new CameraManager { Camera = camera };
        Singleton<PlayerCameraController>.Instance = new PlayerCameraController { Player = owner };
    }
    private static void CacheCamera(Camera camera)
    { Fixture.Set("FPSCamera", camera.gameObject); Fixture.Set("FPSCameraCamera", camera); Fixture.Set("FPSCameraPrismEffects", camera.GetComponent<PrismEffects>()); }
    private static void DestroyCamera(Camera camera)
    { camera.Destroyed = true; camera.GetComponent<PrismEffects>().Destroyed = true; camera.gameObject.Destroyed = true; }
    private static LocalPlayer Candidate(LocalGame game)
    {
        var body = new LocalPlayer { GameWorld = game.GameWorld, Profile = game.Profile }; game.GameWorld.MainPlayer = body;
        Check(BodyReplacementContext.TryOwnCandidate(body), "physical candidate ownership"); game.LocalPlayer = body; game.GameWorld.RegisteredPlayers.Add(body); Fixture.Set("Player", body); return body;
    }
    private static void Begin(LocalGame game, LocalPlayer body)
    { BodyReplacementContext.Begin(game, body); AmandsGraphicsCompat.Prepare(game, body); }

    public static int Main(string[] args)
    {
        try
        {
            var mode = args.FirstOrDefault() ?? "normal";
            if (mode == "published") { PublishedMetadata.Check(args[1], args[2]); Console.WriteLine("PASS published " + _checks); return 0; }
            Check(AmandsGraphicsCompat.GetRejectionReason() == null, "absent plugin is inert"); Fixture.Build(mode);
            if (mode == "missing-native") new Harmony("fixture.amand.native").UnpatchSelf();
            var issue = AmandsGraphicsCompat.GetRejectionReason();
            if (mode is "older" or "drift" or "missing-native")
            {
                Check(issue != null, "unreviewed layout/old version refuses safely");
                Check(AmandsGraphicsCompat.GetRejectionReason() == issue && Redeploy.Core.CoreLog.Warnings == 1, "bounded admission warning");
                Console.WriteLine("PASS " + mode + " " + _checks); return 0;
            }
            Check(issue == null, "reviewed runtime contract and native Harmony hooks accepted");
            Check(Redeploy.Core.CoreLog.Warnings == (mode == "newer" ? 1 : 0), "newer compatible API gets warning only");
            var world = new GameWorld(); var old = new LocalPlayer { GameWorld = world }; var game = new LocalGame { GameWorld = world, LocalPlayer = old, Profile = old.Profile };
            world.MainPlayer = old; world.RegisteredPlayers.Add(old); Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
            var initialCamera = NewCamera(); SetCamera(initialCamera, old); CacheCamera(initialCamera);
            Fixture.Set("Player", old); Fixture.Set("levelSettings", new LevelSettings()); Fixture.Set("scene", "Factory_Rework_Day_Scripts");
            var lamp = new Light { range = 200 }; var deadLamp = new Light { range = 100 }; var volume = new VolumetricLight();
            var lights = Fixture.Get<Dictionary<Light, float>>("registeredLights"); lights[lamp] = 100; lights[deadLamp] = 50;
            var volumes = Fixture.Get<Dictionary<VolumetricLight, float>>("registeredVolumetricLights"); volumes[volume] = .2f;

            // Real Harmony dispatch during preparation must remain native; even an
            // old body's deferred fake-null lamp is left untouched until commitment.
            Begin(game, old); var beforeActivate = Fixture.Activations;
            Fixture.Call("UpdateAmandsGraphics"); Fixture.Call("ResetGraphics"); Fixture.Call("ActivateAmandsGraphics", initialCamera.gameObject, initialCamera.GetComponent<PrismEffects>());
            Check(Fixture.Updates == 1 && Fixture.Resets == 1 && Fixture.Activations == beforeActivate + 1, "Prepare does not suppress foreign Update Reset Activate");
            Check(lights.Count == 2 && volumes.Count == 1, "Prepare has no registry mutation");
            AmandsGraphicsCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
            Fixture.Call("UpdateAmandsGraphics"); Check(Fixture.Updates == 2, "Abort leaves ordinary graphics usable");

            // A candidate may reach construction and camera creation without a published
            // success. It must receive no adapter work, including after EndAttempt.
            Begin(game, old); var failed = Candidate(game); var failedCamera = NewCamera(); SetCamera(failedCamera, failed);
            beforeActivate = Fixture.Activations; BodyReplacementContext.EndAttempt(); AmandsGraphicsCompat.Tick();
            Check(Fixture.Activations == beforeActivate && ReferenceEquals(Fixture.Get<GameObject>("FPSCamera"), initialCamera.gameObject), "unpublished failed candidate receives no camera binding");
            game.LocalPlayer = old; world.MainPlayer = old; Fixture.Set("Player", old); SetCamera(initialCamera, old);

            if (mode == "early-native-off") { Fixture.Set("GraphicsMode", false); Fixture.Call("ResetGraphics"); }
            Begin(game, old); AmandsGraphicsCompat.Tick(); AmandsGraphicsCompat.Tick();
            var next = Candidate(game); var nextCamera = NewCamera(); SetCamera(nextCamera, next); AmandsGraphicsCompat.Tick();
            if (mode is "early-native" or "early-native-off")
            {
                DestroyCamera(initialCamera);
                Fixture.Call("ActivateAmandsGraphics", nextCamera.gameObject, nextCamera.GetComponent<PrismEffects>());
                RespawnApi.Publish(old, next); BodyReplacementContext.EndAttempt(); AmandsGraphicsCompat.Tick();
                Check(Fixture.WorldLighting == (mode == "early-native" ? 20 : 10) && Fixture.Get<float>("defaultZeroLevel") == 10, "early native candidate activation preserves original world baseline after commit");
                Check(Fixture.Get<bool>("GraphicsMode") == (mode == "early-native"), "early native activation preserves chosen mode");
                Check(lights.Count == 2 && lights[lamp] == 100 && lamp.range == (mode == "early-native" ? 200 : 100), "early native registry clear preserves still-live original lamp baselines");
                Console.WriteLine("PASS " + mode + " " + _checks); return 0;
            }
            RespawnApi.Publish(old, next); AmandsGraphicsCompat.Tick(); Check(Fixture.Activations == beforeActivate, "construction phase cannot activate new camera");
            BodyReplacementContext.EndAttempt(); AmandsGraphicsCompat.Tick(); Check(Fixture.Activations == beforeActivate, "live old camera is never nulled or overwritten");
            DestroyCamera(initialCamera); deadLamp.Destroyed = true; volume.Destroyed = true;
            var foreignSameProfile = new LocalPlayer { GameWorld = world, Profile = game.Profile }; game.LocalPlayer = foreignSameProfile;
            AmandsGraphicsCompat.Tick(); Check(Fixture.Activations == beforeActivate, "same-profile unrelated physical player cannot bind"); game.LocalPlayer = next;
            AmandsGraphicsCompat.Tick();
            Check(Fixture.Activations == beforeActivate + 1 && ReferenceEquals(Fixture.Get<GameObject>("FPSCamera"), nextCamera.gameObject), "destroyed exact old camera reacquired through native activation");
            Check(ReferenceEquals(Fixture.Get<Camera>("FPSCameraCamera"), nextCamera) && ReferenceEquals(Fixture.Get<PrismEffects>("FPSCameraPrismEffects"), nextCamera.GetComponent<PrismEffects>()), "all three camera caches match live replacement");
            Check(Fixture.WorldLighting == 20 && Fixture.Get<float>("defaultZeroLevel") == 10, "transformed scene value never replaces original baseline");
            Check(lights.Count == 1 && lights[lamp] == 100 && lamp.range == 200 && volumes.Count == 0, "live original lamp values survive and destroyed registry keys retire");
            beforeActivate = Fixture.Activations; AmandsGraphicsCompat.Tick(); AmandsGraphicsCompat.Tick(); Check(Fixture.Activations == beforeActivate, "same live camera activation is idempotent");
            nextCamera.GetComponent<PrismEffects>().OnEnable();
            Check(Fixture.Get<bool>("GraphicsMode") && Fixture.Activations == beforeActivate, "native Prism reenable preserves chosen on mode after binding");
            if (mode == "dead-prism")
            {
                next.HealthController.IsAlive = false;
                world.RegisteredPlayers.Remove(next);
                nextCamera.GetComponent<PrismEffects>().OnEnable();
                Check(Fixture.Get<bool>("GraphicsMode") && Fixture.Activations == beforeActivate, "current corpse replay camera preserves chosen graphics mode");
                Console.WriteLine("PASS dead-prism " + _checks); return 0;
            }
            Fixture.Call("ActivateAmandsGraphics", initialCamera.gameObject, initialCamera.GetComponent<PrismEffects>()); Check(Fixture.Activations == beforeActivate, "late retired camera activation is fenced");

            Fixture.Set("GraphicsMode", false); Fixture.Call("ResetGraphics"); Check(Fixture.WorldLighting == 10 && lamp.range == 100, "Insert off restores original world and lamp baseline");
            Begin(game, next); var third = Candidate(game); var thirdCamera = NewCamera(); SetCamera(thirdCamera, third);
            RespawnApi.Publish(next, third); BodyReplacementContext.EndAttempt(); DestroyCamera(nextCamera); AmandsGraphicsCompat.Tick();
            Check(!Fixture.Get<bool>("GraphicsMode") && Fixture.WorldLighting == 10, "user disabled graphics mode survives next body replacement");
            Check(ReferenceEquals(Fixture.Get<Camera>("FPSCameraCamera"), thirdCamera), "disabled graphics still binds current camera for future toggle");
            Fixture.Set("GraphicsMode", true); Fixture.Call("UpdateAmandsGraphics"); Check(Fixture.WorldLighting == 20 && lamp.range == 200, "graphics reenable after two respawns uses original baseline");
            lamp.Destroyed = true; Fixture.Call("ResetGraphics"); Check(lights.Count == 0, "destroyed lamp after binding is pruned before native reset");

            if (mode == "native-fault")
            {
                Fixture.Set("GraphicsMode", false); Fixture.Call("ResetGraphics");
                Begin(game, third); var fourth = Candidate(game); var fourthCamera = NewCamera(); SetCamera(fourthCamera, fourth);
                RespawnApi.Publish(third, fourth); BodyReplacementContext.EndAttempt(); DestroyCamera(thirdCamera); Fixture.ThrowActivation = true; AmandsGraphicsCompat.Tick();
                Check(AmandsGraphicsCompat.GetRejectionReason() != null && Fixture.Get<float>("defaultZeroLevel") == 10 && !Fixture.Get<bool>("GraphicsMode"), "native failure keeps original defaults and disabled mode and refuses further replacement");
                Console.WriteLine("PASS native-fault " + _checks); return 0;
            }
            AmandsGraphicsCompat.RetireWorld(new GameWorld()); Fixture.Call("ActivateAmandsGraphics", initialCamera.gameObject, initialCamera.GetComponent<PrismEffects>());
            Check(Fixture.Activations == beforeActivate + 1, "unrelated world disposal does not release old-camera fence");
            AmandsGraphicsCompat.RetireWorld(world); BodyReplacementContext.Reset();
            var calls = Fixture.Updates; Fixture.Call("UpdateAmandsGraphics"); Check(Fixture.Updates == calls + 1, "ordinary graphics behavior restored after owned world retires");
            beforeActivate = Fixture.Activations; Fixture.Call("ActivateAmandsGraphics", thirdCamera.gameObject, thirdCamera.GetComponent<PrismEffects>()); Check(Fixture.Activations == beforeActivate, "late owned callback cannot reactivate after raid retirement");
            var secondWorld = new GameWorld(); var entry = new LocalPlayer { GameWorld = secondWorld }; var secondGame = new LocalGame { GameWorld = secondWorld, LocalPlayer = entry, Profile = entry.Profile };
            secondWorld.MainPlayer = entry; secondWorld.RegisteredPlayers.Add(entry); Singleton<GameWorld>.Instance = secondWorld; Singleton<AbstractGame>.Instance = secondGame;
            var entryCamera = NewCamera(); SetCamera(entryCamera, entry); CacheCamera(entryCamera); Fixture.Set("Player", entry); Fixture.Set("levelSettings", new LevelSettings()); Fixture.Set("defaultZeroLevel", 30f); Fixture.Set("GraphicsMode", true); Fixture.WorldLighting = 40;
            Begin(secondGame, entry); var replacement = Candidate(secondGame); var finalCamera = NewCamera(); SetCamera(finalCamera, replacement);
            RespawnApi.Publish(entry, replacement); BodyReplacementContext.EndAttempt(); DestroyCamera(entryCamera); AmandsGraphicsCompat.Tick();
            Check(Fixture.WorldLighting == 40 && Fixture.Get<float>("defaultZeroLevel") == 30, "second raid uses its own fresh baseline instead of first raid defaults");
            AmandsGraphicsCompat.RetireWorld(secondWorld); BodyReplacementContext.Reset();
            new Harmony("com.maschine.Redeploy.Core.AmandsGraphicsCompat").UnpatchSelf(); Check(AmandsGraphicsCompat.GetRejectionReason() != null, "removed compatibility hook is detected");
            Chainloader.PluginInfos.Clear(); Check(AmandsGraphicsCompat.GetRejectionReason() == null, "absent optional plugin returns to inert admission");
            Console.WriteLine("PASS " + mode + " " + _checks); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

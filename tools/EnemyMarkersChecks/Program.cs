using System.Numerics;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using flir.enemymarkers;
using HarmonyLib;
using Redeploy.Core.Compat;
using UnityEngine;

int checks = 0;
void Check(bool pass, string message) { if (!pass) throw new Exception(message); checks++; }
void Reject(Action action, string message) { try { action(); } catch { checks++; return; } throw new Exception(message); }
Camera CameraFor(Player body, float angle)
{
    var camera = new GameObject().AddComponent<Camera>();
    camera.transform.rotation = Quaternion.CreateFromYawPitchRoll(angle, angle / 2, angle / 3);
    Camera.main = camera; CameraManager.Instance.Camera = camera;
    Singleton<PlayerCameraController>.Instance = new PlayerCameraController { Player = body };
    return camera;
}
(LocalGame Game, GameWorld World, LocalPlayer Body, EmComponent Em) Raid(bool clientWorld = false)
{
    GameWorld world = clientWorld ? new GameObject().AddComponent<LocalGameWorld>() : new GameObject().AddComponent<GameWorld>();
    var body = new LocalPlayer { GameWorld = world, Profile = new Profile(), IsYourPlayer = true };
    var game = new LocalGame { GameWorld = world, LocalPlayer = body, Profile = body.Profile };
    Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
    world.RegisterPlayer(body);
    var em = world.gameObject.AddComponent<EmComponent>(); em.Attach(world);
    return (game, world, body, em);
}
Player Bot(GameWorld world, Profile profile = null) => new() { GameWorld = world, Profile = profile ?? new Profile() };

Check(EnemyMarkersCompat.GetRejectionReason() == null, "absent mod must allow replacement");
var start = typeof(GameWorld).GetMethod(nameof(GameWorld.OnGameStarted));
var upstream = new Harmony("fixture.enemy.upstream");
var future = FutureFixture.Create(false);
var futureMetadata = future.GetCustomAttribute<BepInPlugin>();
upstream.Patch(start, new HarmonyMethod(future.Assembly.GetType("NewGamePatch").GetMethod("PatchPrefix")));
Chainloader.PluginInfos[EnemyMarkersContract.PluginGuid] = new PluginInfo { Instance = (BaseUnityPlugin)Activator.CreateInstance(future), Metadata = futureMetadata };
Check(EnemyMarkersCompat.GetRejectionReason() == null, "compatible newer API was refused");
Check(Redeploy.Core.CoreLog.Warnings.Any(w => w.Contains("newer than reviewed 1.4.0")), "newer API missing native-unverified warning");
var broken = FutureFixture.Create(true);
Reject(() => EnemyMarkersContract.Resolve(broken, "flir.enemymarkers", new Version(1, 4, 1), out _), "changed future camera type accepted");
var brokenDestroy = FutureFixture.Create(false, true);
Reject(() => EnemyMarkersContract.Resolve(brokenDestroy, "flir.enemymarkers", new Version(1, 4, 1), out _), "changed future teardown signature accepted before admission");
upstream.UnpatchSelf();
upstream.Patch(start, new HarmonyMethod(typeof(NewGamePatch), nameof(NewGamePatch.PatchPrefix)));
var plugin = new Plugin();
var metadata = typeof(Plugin).GetCustomAttribute<BepInPlugin>();
Chainloader.PluginInfos[EnemyMarkersContract.PluginGuid] = new PluginInfo { Instance = plugin, Metadata = metadata };
var contract = EnemyMarkersContract.Resolve(typeof(Plugin), metadata.GUID, metadata.Version, out var normalized);
Check(normalized == new Version(1, 4, 0, 0), "version normalization");
Reject(() => EnemyMarkersContract.Resolve(typeof(Plugin), "wrong", metadata.Version, out _), "wrong GUID accepted");
Reject(() => EnemyMarkersContract.Resolve(typeof(Plugin), metadata.GUID, new Version(1, 3, 9), out _), "old release accepted");
Reject(() => EnemyMarkersContract.Resolve(typeof(Plugin), metadata.GUID, new Version(1, 4, 1), out _), "metadata disagreement accepted");
Reject(() => EnemyMarkersContract.Resolve(typeof(BaseUnityPlugin), metadata.GUID, metadata.Version, out _), "wrong plugin accepted");
upstream.UnpatchSelf(); Reject(contract.Verify, "missing upstream hook accepted");
upstream.Patch(start, new HarmonyMethod(typeof(NewGamePatch), nameof(NewGamePatch.PatchPrefix)));
Check(EnemyMarkersCompat.GetRejectionReason() == null, "production patch installation failed");

// Ordinary raids remain upstream-owned, including their known local-event behavior.
var ordinary = Raid(); CameraFor(ordinary.Body, .1f); ordinary.Em.Add(ordinary.Body);
Check(ordinary.Em.Markers.ContainsKey(ordinary.Body), "ordinary local event was changed");
ordinary.Em.Teardown();
Check(ordinary.Em.OriginalCleanupCompleted && ordinary.Body.InterfaceHandlers == 1, "ordinary cleanup behavior was changed");
var brokenOrdinary = Raid(); var brokenBot = Bot(brokenOrdinary.World); brokenOrdinary.Em.Add(brokenBot); brokenBot.Destroyed = true;
Reject(brokenOrdinary.Em.Teardown, "unowned destroyed-body teardown unexpectedly corrected");
Check(!brokenOrdinary.Em.OriginalCleanupCompleted, "destroyed-body positive control did not abort original cleanup tail");
var early = Raid(); var earlyCamera = CameraFor(early.Body, .2f); var earlyBot = Bot(early.World); early.Em.Add(earlyBot);
BodyReplacementContext.Begin(early.Game, early.Body); EnemyMarkersCompat.Prepare(early.Game, early.Body);
EnemyMarkersCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
earlyCamera.Destroyed = true; CameraFor(early.Body, .7f);
EmComponent.Orient(early.Em.BillboardFor(earlyBot));
Check(early.Em.Markers[earlyBot].Marker.transform.rotation == default, "canceled prepare acquired camera authority");
BodyReplacementContext.Begin(early.Game, early.Body); EnemyMarkersCompat.Prepare(early.Game, early.Body);
BodyReplacementContext.EndAttempt(); EnemyMarkersCompat.Tick(); // failed before unregister
early.Em.Add(early.Body); Check(early.Em.Markers.ContainsKey(early.Body), "pre-unregister failure changed ordinary behavior");
BodyReplacementContext.Reset();

var raid = Raid(); var camera1 = CameraFor(raid.Body, .3f); var bot = Bot(raid.World); raid.Em.Add(bot);
var preserved = raid.Em.Markers[bot]; var billboard = raid.Em.BillboardFor(bot);
BodyReplacementContext.Begin(raid.Game, raid.Body); EnemyMarkersCompat.Prepare(raid.Game, raid.Body);
raid.World.UnregisterPlayer(raid.Body);
var replacement = new LocalPlayer { GameWorld = raid.World, Profile = raid.Body.Profile, IsYourPlayer = true };
raid.World.RegisterPlayer(replacement); // MainPlayer assigned before foreign OnPersonAdd; Game.LocalPlayer is still old.
Check(BodyReplacementContext.OwnsBody(replacement), "registration candidate not acquired by production context");
Check(!raid.Em.Markers.ContainsKey(replacement), "replacement local body became a marker");
Check(raid.Em.Markers.Count == 1 && ReferenceEquals(preserved, raid.Em.Markers[bot]), "persistent marker was rebuilt");
var peer = Bot(raid.World, replacement.Profile); raid.World.RegisterPlayer(peer);
Check(raid.Em.Markers.ContainsKey(peer), "shared-profile nonlocal physical body was suppressed");
var impostor = new LocalPlayer { GameWorld = raid.World, Profile = new Profile(), IsYourPlayer = true };
raid.Em.Add(impostor); Check(raid.Em.Markers.ContainsKey(impostor), "profile-id-only identity suppressed unrelated body");
raid.Em.Add(raid.Body); Check(!raid.Em.Markers.ContainsKey(raid.Body), "retained old local body became a marker");
camera1.Destroyed = true;
var camera2 = CameraFor(replacement, .8f);
EmComponent.Orient(billboard); Check(preserved.Marker.transform.rotation == default, "in-flight replacement used new camera too early");
raid.Game.LocalPlayer = replacement; BodyReplacementContext.EndAttempt(); EnemyMarkersCompat.Tick();
EmComponent.Orient(billboard); Check(preserved.Marker.transform.rotation == camera2.transform.rotation, "existing billboard did not face replacement camera including roll");
Check(preserved.PersistentState == 42 && ReferenceEquals(billboard, raid.Em.BillboardFor(bot)), "marker state or billboard identity changed");
var camera3 = CameraFor(replacement, 1.2f); EmComponent.Orient(billboard);
Check(preserved.Marker.transform.rotation == camera3.transform.rotation, "later camera replacement did not rebind while previous camera remained alive");
var wrongCamera = new GameObject().AddComponent<Camera>(); Camera.main = wrongCamera;
var beforeMismatch = preserved.Marker.transform.rotation; camera3.transform.rotation = Quaternion.Identity;
EmComponent.Orient(billboard); Check(preserved.Marker.transform.rotation == beforeMismatch, "wrong Camera.main was accepted"); Camera.main = camera3;

// A failed creation must not steal the camera; restoring the exact old body can resume.
BodyReplacementContext.Begin(raid.Game, replacement); EnemyMarkersCompat.Prepare(raid.Game, replacement); raid.World.UnregisterPlayer(replacement);
var failed = new LocalPlayer { GameWorld = raid.World, Profile = replacement.Profile, IsYourPlayer = true };
raid.World.RegisterPlayer(failed); BodyReplacementContext.EndAttempt();
var failedCamera = CameraFor(failed, 1.5f); EmComponent.Orient(billboard);
Check(preserved.Marker.transform.rotation == beforeMismatch, "failed uncommitted candidate stole billboard camera");
raid.World.RegisterPlayer(replacement); var recovered = CameraFor(replacement, 1.6f); EmComponent.Orient(billboard);
Check(preserved.Marker.transform.rotation == recovered.transform.rotation, "restored old body did not recover camera after failure");
Check(!raid.Em.Markers.ContainsKey(failed), "failed owned candidate became a marker");
raid.Em.Gone(peer); Check(!raid.Em.Markers.ContainsKey(peer), "ordinary bot death cleanup was suppressed");

var next = Raid(); var nextCamera = CameraFor(next.Body, 2f);
// Missed teardown: the singleton already moved, while the old pump has not ticked.
raid.Em.Gone(peer); raid.Em.Add(Bot(raid.World)); EmComponent.Orient(billboard);
Check(preserved.Marker.transform.rotation == recovered.transform.rotation && raid.Em.Markers.Count == 2, "stale world callback crossed into new world before retirement");
EnemyMarkersCompat.Tick(); BodyReplacementContext.Reset();
EnemyMarkersCompat.RetireWorld(raid.World);
raid.Em.Gone(peer); // Upstream throws for this stale callback: production retired-component fence must intercept it.
raid.Em.Add(Bot(raid.World)); EmComponent.Orient(billboard);
Check(preserved.Marker.transform.rotation == recovered.transform.rotation && raid.Em.Markers.Count == 2, "retired callbacks mutated marker state");
int otherSubscriberCalls = 0;
bot.OnIPlayerDeadOrUnspawn += _ => otherSubscriberCalls++;
Check(bot.InterfaceHandlers == 2 && impostor.InterfaceHandlers == 1, "teardown fixture lost foreign death subscriptions");
bot.Destroyed = true;
raid.Em.Markers[impostor].Marker.Destroyed = true;
raid.Em.Teardown();
Check(raid.Em.OriginalCleanupCompleted && raid.Em.Markers.Count == 0, "owned destroyed-body teardown failed to run original cleanup tail");
Check(bot.InterfaceHandlers == 1 && impostor.InterfaceHandlers == 0, "owned cleanup did not remove exactly its actual interface death handlers");
Check(preserved.Marker.Destroyed, "owned world-space marker leaked at destruction");
bot.RaiseGone(); Check(otherSubscriberCalls == 1, "cleanup removed another subscriber or left a failing foreign handler");
Check(next.Em.Markers.Count == 0 && !next.Em.OriginalCleanupCompleted, "late old-world teardown changed newer component");
next.Em.Add(next.Body);
Check(next.Em.Markers.ContainsKey(next.Body), "new ordinary world inherited retired ownership");
BodyReplacementContext.Begin(next.Game, next.Body); EnemyMarkersCompat.Prepare(next.Game, next.Body); next.World.UnregisterPlayer(next.Body);
EnemyMarkersCompat.RetireWorld(raid.World); // late old pump must not retire this world
var nextBody = new LocalPlayer { GameWorld = next.World, Profile = next.Body.Profile, IsYourPlayer = true };
next.World.RegisterPlayer(nextBody); Check(!next.Em.Markers.ContainsKey(nextBody), "late old-world retirement cleared new ownership");
EnemyMarkersCompat.RetireWorld(next.World); BodyReplacementContext.Reset();
var client = Raid(true); var clientCamera = CameraFor(client.Body, 2.4f); var clientBot = Bot(client.World); client.Em.Add(clientBot);
client.World.RegisteredPlayers.Remove(client.Body); // Native death already removed it; override never calls base now.
BodyReplacementContext.Begin(client.Game, client.Body); EnemyMarkersCompat.Prepare(client.Game, client.Body);
client.World.UnregisterPlayer(client.Body);
var clientBody = new LocalPlayer { GameWorld = client.World, Profile = client.Body.Profile, IsYourPlayer = true };
client.World.RegisterPlayer(clientBody);
Check(!client.Em.Markers.ContainsKey(clientBody), "runtime override early return bypassed local-body exclusion");
client.Game.LocalPlayer = clientBody; BodyReplacementContext.EndAttempt(); clientCamera.Destroyed = true;
var clientNewCamera = CameraFor(clientBody, 2.7f); EmComponent.Orient(client.Em.BillboardFor(clientBot));
Check(client.Em.Markers[clientBot].Marker.transform.rotation == clientNewCamera.transform.rotation, "runtime override early return bypassed billboard adoption");
client.Em.Teardown();
Check(client.Em.OriginalCleanupCompleted && clientBot.InterfaceHandlers == 0, "owned active-world teardown failed before retirement");
EnemyMarkersCompat.RetireWorld(client.World); BodyReplacementContext.Reset();
new Harmony("com.maschine.Redeploy.Core.EnemyMarkersCompat").UnpatchSelf();
Check(EnemyMarkersCompat.GetRejectionReason() != null, "removed Core hook accepted at admission");
Check(EnemyMarkersCompat.GetRejectionReason() == null, "retryable admission failure did not reinstall hooks");
Chainloader.PluginInfos.Remove(EnemyMarkersContract.PluginGuid);
Check(EnemyMarkersCompat.GetRejectionReason() == null, "absent mod after previous binding did not return to no-op");

string published = args.Length == 1 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "../../../../../../ArenaMode/artifacts/compat-seven-20261025/visual/enemy-release/BepInEx/plugins/flir.enemymarkers/flir.enemymarkers.dll"));
PublishedMetadata.Verify(published, Check);
Console.WriteLine($"ENEMY_MARKERS_CHECKS_PASSED checks={checks}");

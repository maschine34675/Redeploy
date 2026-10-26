using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using PiPDisabler;
using Redeploy.Core.Compat;
using UnityEngine;

int checks = 0;
void Check(bool value, string why) { if (!value) throw new Exception(why); checks++; }
void Reject(Action action, string why) { try { action(); } catch { checks++; return; } throw new Exception(why); }
(LocalGame Game, GameWorld World, LocalPlayer Body) Raid()
{
    var world = new GameObject().AddComponent<LocalGameWorld>();
    var body = new LocalPlayer { GameWorld = world, Profile = new Profile(), IsYourPlayer = true };
    var game = new LocalGame { GameWorld = world, LocalPlayer = body, Profile = body.Profile };
    Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game; world.RegisterPlayer(body);
    return (game, world, body);
}
(GameObject Root, OpticSight Optic, MeshFilter Filter, Mesh Original, Mesh Cut) Gun(Player body)
{
    var root = new GameObject(); root.transform.parent = body.transform;
    body.HandsController = new Player.FirearmController { ControllerGameObject = root, Item = new() };
    var child = new GameObject(); child.transform.parent = root.transform;
    return (root, child.AddComponent<OpticSight>(), child.AddComponent<MeshFilter>(), new Mesh(), new Mesh());
}
void Cache(string id, (GameObject Root, OpticSight Optic, MeshFilter Filter, Mesh Original, Mesh Cut) gun)
    => MeshSurgeryManager.Add(id, gun.Root, gun.Filter, gun.Original, gun.Cut);
void Prepare(LocalGame game, LocalPlayer body)
{ BodyReplacementContext.Begin(game, body); PipDisablerCompat.Prepare(game, body); }
LocalPlayer Replace(LocalGame game, LocalPlayer old)
{
    game.GameWorld.UnregisterPlayer(old);
    var next = new LocalPlayer { GameWorld = game.GameWorld, Profile = old.Profile, IsYourPlayer = true };
    game.GameWorld.RegisterPlayer(next);
    Check(BodyReplacementContext.TryOwnCandidate(next), "candidate acquisition");
    game.LocalPlayer = next; BodyReplacementContext.EndAttempt(); PipDisablerCompat.Tick(); return next;
}

Check(PipDisablerCompat.GetRejectionReason() == null, "absent optional mod rejected");
var plugin = new PiPDisablerPlugin(); var attr = plugin.GetType().GetCustomAttribute<BepInPlugin>();
var contract = PipDisablerContract.Resolve(plugin.GetType(), attr.GUID, attr.Version);
Check(plugin.GetType().Assembly.GetName().Version == new Version(1, 0, 0, 0), "fixture exercises independent assembly/plugin versions");
Reject(() => PipDisablerContract.Resolve(plugin.GetType(), "wrong", attr.Version), "wrong GUID accepted");
Reject(() => PipDisablerContract.Resolve(plugin.GetType(), attr.GUID, new Version(2, 0, 9)), "old plugin accepted");
Reject(() => PipDisablerContract.Resolve(plugin.GetType(), attr.GUID, new Version(2, 1, 1)), "reported/attribute disagreement accepted");
ForeignOpticHooks.Install();
Chainloader.PluginInfos[attr.GUID] = new PluginInfo { Instance = plugin, Metadata = attr };
Check(PipDisablerCompat.GetRejectionReason() == null, "binding and actual Harmony hooks failed");

var ordinary = Raid(); var ordinaryGun = Gun(ordinary.Body); var ordinaryBot = new Player { GameWorld = ordinary.World }; var botGun = Gun(ordinaryBot);
ordinaryGun.Optic.OnEnable();
Check(ArenaMode.Compat.PipPreloadCompat.GetRejectionReason() == null, "Arena preload adapter failed to install");
var factory = new ObjectsFactory(); var resource = new ResourceKey("fixture-optic");
var pool = new EFT.AssetsManager.AssetPool<EFT.AssetsManager.WeaponModPoolObject> {
    IEasyAssets = factory.EasyAssets, ResourceKey = resource, AssemblyType = ObjectsFactory.AssemblyType.Local,
    ParentTransform = factory.RaidPools.UnloadableParent };
var source = new TaskCompletionSource<EFT.AssetsManager.IAssetPool>(); source.SetResult(pool);
factory.RaidPools.PoolsDictionary[resource] = new ObjectsFactory.Pools.AssetPoolData { Source = source };
OpticSight role = null;
pool.Instantiate = parent => { var obj = new GameObject(); obj.transform.parent = parent; role = obj.AddComponent<OpticSight>(); role.OnEnable(); return obj; };
ArenaMode.Session.ArenaSession.ArmedForNextRaid = true;
using (ArenaMode.Kits.KitOpticPreloadGuard.Begin(factory, new[] { resource }, loadingScreen: true))
{
    int beforeHide = ScopeLifecycle.HideCalls;
    pool.GetOriginalGameObject(pool.ParentObject.transform);
    Check(role != null && role.Calls == 0 && ReferenceEquals(ScopeLifecycle.Active, ordinaryGun.Optic), "cold preload foreign enable stole reticle before Core ownership");
    Check(ArenaMode.Kits.KitOpticPreloadGuard.IsWithheldCallback(role), "exact cold role marker missing");
    role.OnDisable();
    Check(role.Calls == 0 && ScopeLifecycle.HideCalls == beforeHide && ReferenceEquals(ScopeLifecycle.Active, ordinaryGun.Optic), "cold preload disable marker vanished before PiP postfix");
    Check(!ArenaMode.Kits.KitOpticPreloadGuard.IsWithheldCallback(role), "paired disable finalizer did not retire exact marker");
    ScopeLifecycle.RestoreBypassedOpticState(role, "after-pair", false);
    Check(ReferenceEquals(ScopeLifecycle.Active, role), "completed role request improperly remained suppressed");
}
ArenaMode.Session.ArenaSession.ArmedForNextRaid = false;
ordinaryGun.Optic.OnEnable(); botGun.Optic.OnEnable();
Check(ReferenceEquals(ScopeLifecycle.Active, botGun.Optic), "ordinary foreign behavior changed");
int hides = ScopeLifecycle.HideCalls; botGun.Optic.OnDisable();
Check(ScopeLifecycle.HideCalls == hides + 1, "ordinary disable changed");
ScopeLifecycle.ForceExit(); ordinaryGun.Optic.OnEnable(); Cache("ordinary-own", ordinaryGun);
int exits = ScopeLifecycle.ExitCalls;
Prepare(ordinary.Game, ordinary.Body); PipDisablerCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
Check(ScopeLifecycle.ExitCalls == exits && !ordinaryGun.Cut.Destroyed, "canceled preparation mutated visuals/cache");
// Foreign preload poisoning must fail before it receives authority to reset globals.
botGun.Optic.OnEnable(); BodyReplacementContext.Begin(ordinary.Game, ordinary.Body);
Reject(() => PipDisablerCompat.Prepare(ordinary.Game, ordinary.Body), "unowned global optic state accepted for teardown");
PipDisablerCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
Check(ReferenceEquals(ScopeLifecycle.Active, botGun.Optic) && !ordinaryGun.Cut.Destroyed, "rejected preparation mutated foreign state");
ScopeLifecycle.ForceExit(); ordinaryGun.Optic.OnEnable(); Cache("foreign-bot", botGun);
MeshSurgeryManager.EmptyForeign("foreign-empty");
ScopeLifecycle.Subscribe(ordinary.Body); MeshSurgeryManager.Subscribe(ordinary.Body);
ordinaryGun.Root.transform.parent = ordinaryBot.transform;
BodyReplacementContext.Begin(ordinary.Game, ordinary.Body);
Reject(() => PipDisablerCompat.Prepare(ordinary.Game, ordinary.Body), "past local observation authorized a gun now parented to a bot");
PipDisablerCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
// EFT death can detach the held firearm before the later Core.Prepare call.
// The earlier exact MainPlayer/local-controller observation is read-only until now.
ordinaryGun.Root.transform.parent = null;
Check(ScopeLifecycle.ExitCalls == exits + 1 && !ordinaryGun.Cut.Destroyed, "pre-Core observation mutated foreign visuals");
ordinary.Body.HandsController = null;
Prepare(ordinary.Game, ordinary.Body);
var first = Replace(ordinary.Game, ordinary.Body);
Check(ordinaryGun.Cut.Destroyed && ReferenceEquals(ordinaryGun.Filter.sharedMesh, ordinaryGun.Original), "owned corpse meshes not restored before cut destruction");
Check(!ordinaryGun.Root.Destroyed && !ordinaryGun.Original.Destroyed, "retained corpse/original mesh destroyed");
Check(!MeshSurgeryManager.Contains("ordinary-own") && MeshSurgeryManager.Contains("foreign-bot") && !botGun.Cut.Destroyed, "foreign cache removed or owned cache retained");
Check(MeshSurgeryManager.Contains("foreign-empty"), "unrelated empty foreign cache removed");
Check(!MeshSurgeryManager.Subscribed && ScopeLifecycle.Active == null, "old subscription/visual survived boundary");
Check(CameraSettingsManager.RestoreCalls == 1, "owned LOD override was not restored at body boundary");
var firstGun = Gun(first); firstGun.Optic.OnEnable();
Check(ReferenceEquals(ScopeLifecycle.Active, firstGun.Optic), "new local optic rejected");
hides = ScopeLifecycle.HideCalls;
botGun.Optic.OnEnable(); botGun.Optic.OnDisable(); ordinaryGun.Optic.OnEnable(); ordinaryGun.Optic.OnDisable();
Check(ReferenceEquals(ScopeLifecycle.Active, firstGun.Optic) && ScopeLifecycle.HideCalls == hides, "bot/retained-body optic stole or hid current reticle");
var loose = new GameObject().AddComponent<OpticSight>(); loose.OnEnable(); loose.OnDisable();
Check(ReferenceEquals(ScopeLifecycle.Active, firstGun.Optic) && ScopeLifecycle.HideCalls == hides, "unmounted preload stole or hid current reticle");
ScopeLifecycle.RestoreBypassedOpticState(loose, "rangefinder", false);
Check(ReferenceEquals(ScopeLifecycle.Active, firstGun.Optic), "unmounted rangefinder bypass stole reticle");
ScopeLifecycle.RestoreBypassedOpticState(firstGun.Optic, "thermal", false);
Check(ReferenceEquals(ScopeLifecycle.Active, firstGun.Optic), "local bypass denied");
var sibling = new GameObject(); sibling.transform.parent = firstGun.Root.transform; var siblingOptic = sibling.AddComponent<OpticSight>();
var skip = new Harmony("fixture.skip-native");
skip.Patch(typeof(OpticSight).GetMethod("OnEnable"), prefix: new HarmonyMethod(typeof(TestHooks), nameof(TestHooks.Skip)) { priority = Priority.Last });
siblingOptic.OnEnable();
Check(ReferenceEquals(ScopeLifecycle.Active, firstGun.Optic) && siblingOptic.Calls == 0, "foreign postfix ignored __runOriginal=false");
skip.UnpatchSelf(); siblingOptic.OnEnable();
Check(ReferenceEquals(ScopeLifecycle.Active, siblingOptic), "native callback context failed to unwind");
skip.Patch(typeof(OpticSight).GetMethod("OnEnable"), prefix: new HarmonyMethod(typeof(TestHooks), nameof(TestHooks.Throw)) { priority = Priority.Last });
Reject(() => siblingOptic.OnEnable(), "native exception fixture did not throw"); skip.UnpatchSelf();
ScopeLifecycle.OnOpticEnabled(siblingOptic);
Check(ReferenceEquals(ScopeLifecycle.Active, siblingOptic), "native exception left a stale suppressed callback frame");
int updates = ScopeLifecycle.UpdateCalls; ScopeLifecycle.CheckAndUpdate("steady");
Check(ScopeLifecycle.UpdateCalls == updates + 1, "live scoped pump was suppressed");
Cache("same-restored-id", firstGun); MeshSurgeryManager.Observe(firstGun.Optic.transform);
Prepare(ordinary.Game, first); ordinary.World.UnregisterPlayer(first);
updates = ScopeLifecycle.UpdateCalls; ScopeLifecycle.CheckAndUpdate("during-replacement");
Check(ScopeLifecycle.UpdateCalls == updates, "transitional old body resumed foreign update");
// Simulate failed replacement recovery restoring the identical old physical body.
ordinary.Game.LocalPlayer = first; ordinary.World.MainPlayer = first; BodyReplacementContext.EndAttempt(); PipDisablerCompat.Tick();
siblingOptic.OnEnable();
Check(ReferenceEquals(ScopeLifecycle.Active, siblingOptic), "failed replacement cannot recover same old body");
Check(firstGun.Cut.Destroyed && firstGun.Root != null, "recovery incorrectly destroyed retained gun");
var recoveredCut = new Mesh(); MeshSurgeryManager.Add("same-restored-id", firstGun.Root, firstGun.Filter, firstGun.Original, recoveredCut);
MeshSurgeryManager.Observe(firstGun.Optic.transform);
Prepare(ordinary.Game, first); var second = Replace(ordinary.Game, first);
Check(recoveredCut.Destroyed && !MeshSurgeryManager.Contains("same-restored-id"), "second retirement with reused item ID failed");
var secondGun = Gun(second); secondGun.Optic.OnEnable(); Cache("second-body", secondGun); MeshSurgeryManager.Observe(secondGun.Optic.transform);
var rebindGun = Gun(second);
MeshSurgeryManager.RebindNext(rebindGun.Root, rebindGun.Filter);
MeshSurgeryManager.Observe(rebindGun.Optic.transform);
Check(ReferenceEquals(secondGun.Filter.sharedMesh, secondGun.Original), "old leased filter was not restored before same-key upstream root rebind");
Check(ReferenceEquals(rebindGun.Filter.sharedMesh, secondGun.Cut) && !secondGun.Cut.Destroyed, "transitional old entries prevented upstream rebind");
rebindGun.Optic.OnEnable();
PipDisablerCompat.RetireWorld(ordinary.World); BodyReplacementContext.Reset();
Check(secondGun.Cut.Destroyed && !MeshSurgeryManager.Contains("second-body") && MeshSurgeryManager.Contains("foreign-bot"), "world retirement escaped owned caches");
Check(ReferenceEquals(rebindGun.Filter.sharedMesh, secondGun.Original), "new physical root was not restored on final retirement");
var nextRaid = Raid(); var nextGun = Gun(nextRaid.Body); nextGun.Optic.OnEnable();
ordinaryGun.Optic.OnEnable(); firstGun.Optic.OnDisable();
Check(ReferenceEquals(ScopeLifecycle.Active, nextGun.Optic), "retired world callback affected second raid");
int nextExits = ScopeLifecycle.ExitCalls; PipDisablerCompat.RetireWorld(ordinary.World);
Check(ScopeLifecycle.ExitCalls == nextExits && ReferenceEquals(ScopeLifecycle.Active, nextGun.Optic), "late old-world disposal cleaned new world");
// Pool reuse by an unowned world must restore vanilla/upstream authority.
ordinaryGun.Root.transform.parent = nextRaid.Body.transform;
nextRaid.Body.HandsController = new Player.FirearmController { ControllerGameObject = ordinaryGun.Root };
ordinaryGun.Optic.OnEnable(); Check(ReferenceEquals(ScopeLifecycle.Active, ordinaryGun.Optic), "pooled local optic in ordinary raid retained stale tombstone");
var future = PublishedMetadata.MakeFuture(typeof(PiPDisablerPlugin).Assembly.Location, false);
var futureAttr = future.GetCustomAttribute<BepInPlugin>();
Check(PipDisablerContract.Resolve(future, futureAttr.GUID, futureAttr.Version) != null, "matching future contracts rejected");
var brokenFuture = PublishedMetadata.MakeFuture(typeof(PiPDisablerPlugin).Assembly.Location, true);
Reject(() => PipDisablerContract.Resolve(brokenFuture, attr.GUID, new Version(2, 1, 1)), "changed future teardown field accepted");
Chainloader.PluginInfos[attr.GUID] = new PluginInfo { Instance = (BaseUnityPlugin)Activator.CreateInstance(future), Metadata = futureAttr };
Check(PipDisablerCompat.GetRejectionReason() == null, "matching future plugin binding rejected");
Check(Redeploy.Core.CoreLog.Warnings.Count(w => w.Contains("newer than reviewed 2.1.0")) == 1, "missing/duplicate future version warning");
PipDisablerCompat.GetRejectionReason();
Check(Redeploy.Core.CoreLog.Warnings.Count(w => w.Contains("newer than reviewed 2.1.0")) == 1, "future warning repeated on revalidation");
Chainloader.PluginInfos[attr.GUID] = new PluginInfo { Instance = plugin, Metadata = attr };
Check(PipDisablerCompat.GetRejectionReason() == null, "reviewed plugin rebind after unowned future-contract test failed");
ScopeLifecycle.ForceExit();
var failingRaid = Raid(); var failingGun = Gun(failingRaid.Body); failingGun.Optic.OnEnable();
Prepare(failingRaid.Game, failingRaid.Body); var failingBody = Replace(failingRaid.Game, failingRaid.Body);
var finalGun = Gun(failingBody); finalGun.Optic.OnEnable(); Cache("unsafe-final-state", finalGun); MeshSurgeryManager.Observe(finalGun.Optic.transform);
// Inject a foreign corruption directly: production callbacks correctly refuse it.
contract.Active.SetValue(null, new GameObject().AddComponent<OpticSight>());
PipDisablerCompat.RetireWorld(failingRaid.World);
var retainedLeases = (System.Collections.ICollection)typeof(PipDisablerCompat).GetField("Leases", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
Check(retainedLeases.Count == 0 && !finalGun.Cut.Destroyed, "failed safe world teardown retained strong leases or destroyed unverified global state");
Check(PipDisablerCompat.GetRejectionReason()?.Contains("different physical body") == true, "unsafe teardown was silently accepted");
BodyReplacementContext.Reset();
PublishedMetadata.Verify(args.Length == 0 ? @"D:\SPT41\Development\ArenaMode\artifacts\compat-seven-20261025\visual\pip-release\BepInEx\plugins\PiP-Disabler\PiP-Disabler.dll" : args[0], Check);
Console.WriteLine($"PiP Disabler checks passed: {checks}; production adapter + real Harmony + released DLL metadata.");
public static class TestHooks { public static bool Skip() => false; public static void Throw() => throw new Exception("native fixture failure"); }

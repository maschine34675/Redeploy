using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HarmonyLib;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;
using UnityEngine;
using VisualAssist;
using Object = UnityEngine.Object;

int checks = 0;
void Check(bool okay, string message) { if (!okay) throw new Exception(message); checks++; }
void Reject(Action action, string message) { try { action(); } catch { checks++; return; } throw new Exception(message); }
(LocalGame Game, GameWorld World, LocalPlayer Body, GrenadeArc Arc) Raid(bool client = false)
{
    var world = new GameObject().AddComponent(client ? typeof(ClientGameWorld) : typeof(GameWorld)) as GameWorld;
    var body = new LocalPlayer { GameWorld = world, Profile = new Profile(), IsYourPlayer = true };
    var game = new LocalGame { GameWorld = world, LocalPlayer = body, Profile = body.Profile };
    Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
    GamePlayerOwner.MyPlayer = body; world.RegisterPlayer(body); world.OnGameStarted();
    return (game, world, body, Singleton<GrenadeArc>.Instance);
}
void Prepare(LocalGame game, LocalPlayer body)
{
    BodyReplacementContext.Begin(game, body); VisualAssistCompat.Prepare(game, body);
}
LocalPlayer Candidate(LocalGame game, Profile profile = null)
{
    var body = new LocalPlayer { GameWorld = game.GameWorld, Profile = profile ?? game.Profile, IsYourPlayer = true };
    game.GameWorld.RegisterPlayer(body);
    Check(BodyReplacementContext.TryOwnCandidate(body), "candidate must be admitted by production context");
    return body;
}
void Commit(LocalGame game, LocalPlayer oldBody, LocalPlayer body, bool published = true)
{
    game.LocalPlayer = body; GamePlayerOwner.MyPlayer = body;
    if (published) RespawnApi.Raise(oldBody, body);
    BodyReplacementContext.EndAttempt(); VisualAssistCompat.Tick();
}
void End(GameWorld world) { VisualAssistCompat.RetireWorld(world); BodyReplacementContext.Reset(); }

Check(VisualAssistCompat.GetRejectionReason() == null, "absent plugin must be inert");
Check(Harmony.GetPatchInfo(typeof(GameWorld).GetMethod(nameof(GameWorld.UnregisterPlayer))) == null, "absent plugin installed game hook");
var upstream = new Harmony("fixture.visualassist");
void Upstream()
{
    upstream.Patch(typeof(GameWorld).GetMethod(nameof(GameWorld.OnGameStarted)), postfix: new HarmonyMethod(typeof(GrenadeAssistGameWorldStartedPostfixPatch), "Postfix"));
    upstream.Patch(typeof(Player).GetMethod(nameof(Player.OnDead)), prefix: new HarmonyMethod(typeof(GrenadeAssistPlayerOnDeadPrefixPatch), "Postfix"));
    upstream.Patch(typeof(Player).GetMethod(nameof(Player.Dispose)), prefix: new HarmonyMethod(typeof(GrenadeAssistPlayerDisposePrefixPatch), "Postfix"));
}
Upstream();
var metadata = typeof(Plugin).GetCustomAttribute<BepInPlugin>();
var contract = VisualAssistContract.Resolve(typeof(Plugin), metadata.GUID, metadata.Version, out var version);
Check(version == new Version(1, 2, 0, 0), "semantic version must ignore generated revision");
Check(VisualAssistContract.Normalize(new Version(1, 2, 0, 23951)) == version, "released revision rejected");
Reject(() => VisualAssistContract.Resolve(typeof(Plugin), "bad", metadata.Version, out _), "wrong GUID admitted");
Reject(() => VisualAssistContract.Resolve(typeof(Plugin), metadata.GUID, new(1, 1, 0), out _), "old version admitted");
Reject(() => VisualAssistContract.Resolve(typeof(Plugin), metadata.GUID, new(1, 2, 1), out _), "metadata skew admitted");
Reject(() => VisualAssistContract.Resolve(typeof(BaseUnityPlugin), metadata.GUID, metadata.Version, out _), "wrong type admitted");
upstream.UnpatchSelf(); Reject(contract.Verify, "missing upstream hook admitted"); Upstream();
Chainloader.PluginInfos[metadata.GUID] = new() { Instance = new Plugin(), Metadata = metadata };
Check(VisualAssistCompat.GetRejectionReason() == null, "valid fixture refused");

if (args.Length > 0 && args[0].StartsWith("--creation-", StringComparison.Ordinal))
{
    var fault = Raid(); Prepare(fault.Game, fault.Body); fault.World.UnregisterPlayer(fault.Body);
    var next = Candidate(fault.Game);
    if (args[0] == "--creation-awake" || args[0] == "--creation-swallowed")
    { GrenadeArc.FailAwake = true; GameObject.SwallowAwakeExceptions = args[0] == "--creation-swallowed"; }
    else if (args[0] == "--creation-owner")
    {
        var bound = (VisualAssistContract)typeof(VisualAssistCompat).GetField("_contract", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        bound.Player = new ThrowingSetField(bound.Player);
    }
    else throw new Exception("Unknown creation fault");
    Commit(fault.Game, fault.Body, next);
    var partial = GrenadeArc.LastCreated;
    Check(partial.Destroyed && partial.Line.Destroyed && partial.Sphere.Destroyed, "partially initialized arc or resources leaked");
    Check(!Renderer.SharedDefault.Destroyed, "partial Awake destroyed a shared primitive material");
    Check(partial.Line.sharedMaterial.Destroyed == (args[0] == "--creation-owner"), "creation material authority does not match completed Awake");
    Check(next.gameObject.GetComponents(typeof(GrenadeArc)).Length == 0 && Singleton<GrenadeArc>.Instance == null, "partial arc remains authoritative");
    Check(VisualAssistCompat.GetRejectionReason() != null, "creation failure did not withhold unsafe later replacements");
    Console.WriteLine($"VISUAL_ASSIST_CREATION_FAULT_CLEANED {args[0]} checks={checks}");
    return;
}

var ordinary = Raid(); ordinary.Arc.LateUpdate(); var nativeSphere = ordinary.Arc.Sphere;
ordinary.Body.OnDead(0);
Check(ordinary.Arc == null && nativeSphere != null, "ordinary death resource behavior unexpectedly changed");
Check(Singleton<GrenadeArc>.Instance == null, "ordinary native singleton release changed");
Check(ordinary.Arc.CleanupCalls == 1, "native cleanup not run");
BodyReplacementContext.Reset();

var refused = Raid(); refused.Arc.LateUpdate(); Prepare(refused.Game, refused.Body);
VisualAssistCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
Check(ReferenceEquals(refused.Arc, Singleton<GrenadeArc>.Instance) && refused.Arc.Line.enabled, "refused preparation modified live assist");
Plugin.GrenadeArcEnabled.Value = false; refused.Arc.LateUpdate();
Check(refused.Arc.Line.enabled, "unowned OFF behavior changed"); Plugin.GrenadeArcEnabled.Value = true;

var dead = Raid(true); dead.Arc.LateUpdate(); var oldLine = dead.Arc.Line; var oldSphere = dead.Arc.Sphere;
var oldLineMaterial = oldLine.sharedMaterial; var oldSphereMaterial = dead.Arc.Knob.sharedMaterial;
var unrelated = dead.Body.gameObject.AddComponent<LineRenderer>(); var unrelatedMaterial = unrelated.sharedMaterial = new Material();
dead.Body.OnDead(0); dead.World.RegisteredPlayers.Remove(dead.Body); // Native override now early-returns.
Check(oldLine != null && oldSphere != null, "capture must stay read-only before commit");
Prepare(dead.Game, dead.Body); dead.World.UnregisterPlayer(dead.Body);
Check(oldLine == null && oldSphere == null && oldLineMaterial == null && oldSphereMaterial == null, "captured initial resources leaked");
Check(unrelated != null && unrelatedMaterial != null, "unrelated renderer or material destroyed");
var body2 = Candidate(dead.Game); dead.Game.LocalPlayer = body2; GamePlayerOwner.MyPlayer = body2;
VisualAssistCompat.Tick(); Check(Singleton<GrenadeArc>.Instance == null, "unpublished candidate acquired preview");
Commit(dead.Game, dead.Body, body2);
var arc2 = Singleton<GrenadeArc>.Instance;
Check(arc2 != null && ReferenceEquals(arc2.localPlayer, body2), "replacement preview not attached");
Check(body2.gameObject.GetComponents(typeof(GrenadeArc)).Length == 1, "duplicate replacement arc");
Check(arc2.GrenadeThrow.Equals(default(GrenadeThrow)) && !arc2.Line.enabled, "replacement inherited stale throw/visibility");
arc2.LateUpdate(); Check(arc2.Line.enabled && arc2.NativeUpdates == 1, "replacement native preview did not resume");
Plugin.GrenadeArcEnabled.Value = false; arc2.LateUpdate();
Check(!arc2.Line.enabled && !arc2.Knob.enabled && arc2.Line.positionCount == 0 && arc2.GrenadeThrow.Equals(default(GrenadeThrow)), "OFF leaves stale rendering/cache");
int destroyedBefore = Object.DestroyCalls; for (int i = 0; i < 100; i++) { arc2.LateUpdate(); VisualAssistCompat.Tick(); }
Check(Object.DestroyCalls == destroyedBefore && arc2.NativeUpdates == 1, "steady disabled frames mutate native resources");
Plugin.GrenadeArcEnabled.Value = true; arc2.LateUpdate(); Check(arc2.NativeUpdates == 2, "ON did not restore native preview");
GamePlayerOwner.MyPlayer = dead.Body; arc2.LateUpdate(); Check(!arc2.Line.enabled, "non-authoritative body rendered"); GamePlayerOwner.MyPlayer = body2;

// A late old callback really does contain an arc: otherwise the upstream Release bug is not exercised.
var lateOldArc = dead.Body.gameObject.AddComponent<GrenadeArc>(); lateOldArc.localPlayer = dead.Body;
var lateSphere = lateOldArc.Sphere;
dead.Body.Dispose();
Check(lateOldArc == null && lateSphere == null, "old native callback did not clean its own arc");
Check(ReferenceEquals(Singleton<GrenadeArc>.Instance, arc2), "old Dispose cleared the new singleton");
dead.Arc.OnDestroy(); Check(ReferenceEquals(Singleton<GrenadeArc>.Instance, arc2), "delayed old OnDestroy cleared the new singleton");
RespawnApi.Raise(dead.Body, body2); VisualAssistCompat.Tick();
Check(body2.gameObject.GetComponents(typeof(GrenadeArc)).Length == 1, "duplicate publication recreated preview");

// Rewire failure after new body becomes authoritative has no Replaced event; recovery must bind it.
Prepare(dead.Game, body2); dead.World.UnregisterPlayer(body2);
var body3 = Candidate(dead.Game); Commit(dead.Game, body2, body3, false);
var arc3 = Singleton<GrenadeArc>.Instance;
Check(arc2 == null && arc3 != null && ReferenceEquals(arc3.localPlayer, body3), "failed handoff recovery lost preview");
Check(arc2.CleanupCalls == 1, "native cleanup did not run exactly once");

// Failed creation restoring the original living body reinitializes that exact body only.
Prepare(dead.Game, body3); dead.World.UnregisterPlayer(body3); dead.World.RegisterPlayer(body3);
BodyReplacementContext.EndAttempt(); VisualAssistCompat.Tick();
var restored = Singleton<GrenadeArc>.Instance;
Check(restored != null && !ReferenceEquals(restored, arc3) && ReferenceEquals(restored.localPlayer, body3), "living old-body rollback did not restore preview");
VisualAssistCompat.RetireWorld(ordinary.World);
Check(ReferenceEquals(Singleton<GrenadeArc>.Instance, restored), "stale world retirement cleared current preview");
End(dead.World); Check(restored == null && restored.Sphere == null && Singleton<GrenadeArc>.Instance == null, "raid retirement leaked resources");

var second = Raid(); var scav = new Profile { Id = "own-scav" };
BodyReplacementContext.Begin(second.Game, second.Body, scav); VisualAssistCompat.Prepare(second.Game, second.Body);
second.World.UnregisterPlayer(second.Body);
var secondBody = Candidate(second.Game, scav); second.Game.Profile = scav;
Commit(second.Game, second.Body, secondBody);
var secondArc = Singleton<GrenadeArc>.Instance;
Check(ReferenceEquals(secondArc.localPlayer.Profile, scav), "own-Scav profile switch lost the physical body");
dead.Body.Dispose(); VisualAssistCompat.RetireWorld(dead.World);
Check(ReferenceEquals(Singleton<GrenadeArc>.Instance, secondArc), "old raid callbacks cleared a new world");
End(second.World);

var foreign = Raid(); var foreignBody = new LocalPlayer { GameWorld = foreign.World, IsYourPlayer = true, Profile = foreign.Body.Profile };
foreign.Arc.localPlayer = foreignBody; BodyReplacementContext.Begin(foreign.Game, foreign.Body);
Reject(() => VisualAssistCompat.Prepare(foreign.Game, foreign.Body), "same-profile different physical owner admitted");
BodyReplacementContext.CancelPreparation(); foreign.Arc.localPlayer = foreign.Body;
var wrongLine = foreignBody.gameObject.AddComponent<LineRenderer>(); contract.Line.SetValue(foreign.Arc, wrongLine);
BodyReplacementContext.Begin(foreign.Game, foreign.Body);
Reject(() => VisualAssistCompat.Prepare(foreign.Game, foreign.Body), "foreign resource ownership admitted");
BodyReplacementContext.CancelPreparation(); Check(wrongLine != null, "refusal destroyed a foreign renderer");

// No stale target collection or whole-process unpatching when the optional plugin is removed.
new Harmony("com.maschine.Redeploy.Core.VisualAssistCompat").UnpatchSelf();
Check(VisualAssistCompat.GetRejectionReason() != null, "missing Core hook admitted");
Check(VisualAssistCompat.GetRejectionReason() == null, "retryable hook failure did not recover");
Chainloader.PluginInfos.Clear(); Check(VisualAssistCompat.GetRejectionReason() == null, "absent mod did not become inert again");
Check(Harmony.GetPatchInfo(typeof(Player).GetMethod(nameof(Player.OnDead)))?.Prefixes.Any(p => p.owner == upstream.Id) == true, "foreign hooks removed");

PublishedMetadata.Verify(args.Length == 1 ? args[0] : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
    "../../../../../../ArenaMode/artifacts/mod-refresh-20261120/visualassist/release/BepInEx/plugins/VisualAssist/VisualAssist.dll")), Check);
Console.WriteLine($"VISUAL_ASSIST_CHECKS_PASSED checks={checks}");

// Reflection can fail after the native component exists. Keep reads real while injecting
// one external write failure; the production adapter must clean its exact allocation.
sealed class ThrowingSetField(FieldInfo original) : FieldInfo
{
    public override FieldAttributes Attributes => original.Attributes;
    public override RuntimeFieldHandle FieldHandle => original.FieldHandle;
    public override Type FieldType => original.FieldType;
    public override Type DeclaringType => original.DeclaringType!;
    public override string Name => original.Name;
    public override Type ReflectedType => original.ReflectedType!;
    public override object[] GetCustomAttributes(bool inherit) => original.GetCustomAttributes(inherit);
    public override object[] GetCustomAttributes(Type attributeType, bool inherit) => original.GetCustomAttributes(attributeType, inherit);
    public override bool IsDefined(Type attributeType, bool inherit) => original.IsDefined(attributeType, inherit);
    public override object GetValue(object obj) => original.GetValue(obj)!;
    public override void SetValue(object obj, object value, BindingFlags invokeAttr, Binder binder, System.Globalization.CultureInfo culture)
        => throw new InvalidOperationException("synthetic owner assignment failure");
}

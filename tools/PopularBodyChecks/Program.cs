using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using HandsAreNotBusy;
using HarmonyLib;
using HollywoodFX;
using Redeploy.Core;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;
using UnityEngine;
using Version = System.Version;

internal static class Program
{
    private const string Owner = "com.maschine.Redeploy.Core.HandsAreNotBusyCompat";
    private static readonly MethodInfo Fix = typeof(HANB_Component).GetMethod("FixHandsController", BindingFlags.NonPublic | BindingFlags.Instance);
    private static int checks, unpatches;
    private static bool throwAfterPatch, throwUnpatch;
    private static LocalGame game;

    private static int Main(string[] args)
    {
        try
        {
            InstrumentHarmony();
            Absent();
            PartialInstall();
            Install();
            Contracts(args);
            OrdinaryAndAbort();
            CommitAndCandidate();
            RecoverOld();
            RecoverNew();
            EarlyAndForeignFx();
            WrongComponent();
            UnpatchFailure();
            Console.WriteLine("POPULAR_BODY_RUNTIME_PASSED assertions=" + checks);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    internal static void Check(bool condition, string name)
    {
        checks++;
        if (!condition) throw new InvalidOperationException("CHECK FAILED: " + name);
    }
    private static void Throws(Action action, string name)
    {
        try { action(); } catch { Check(true, name); return; }
        Check(false, name);
    }
    private static void InstrumentHarmony()
    {
        var observer = new Harmony("popular-body-checks.observer");
        observer.Patch(typeof(Harmony).GetMethod("UnpatchID", BindingFlags.Public | BindingFlags.Static), prefix: new HarmonyMethod(typeof(Program), nameof(BeforeUnpatch)));
        foreach (var method in typeof(Harmony).GetMethods().Where(m => m.Name == "Patch" && m.GetParameters()[0].ParameterType == typeof(MethodBase)))
            observer.Patch(method, postfix: new HarmonyMethod(typeof(Program), nameof(AfterPatch)));
        new Harmony(Owner).UnpatchSelf();
        Check(unpatches == 1, "real Harmony UnpatchSelf instrumentation positive control");
    }
    private static void BeforeUnpatch(string __0)
    {
        if (__0 != Owner) return;
        unpatches++;
        if (throwUnpatch) throw new InvalidOperationException("injected unpatch failure");
    }
    private static void AfterPatch(MethodBase __0)
    {
        if (!throwAfterPatch || __0 != Fix) return;
        throwAfterPatch = false;
        throw new InvalidOperationException("injected failure after hook installation");
    }
    private static void Absent()
    {
        int calls = unpatches, enumerations = GameObject.Enumerations;
        for (int i = 0; i < 10000; i++)
        {
            Check(HollywoodFxCompat.GetRejectionReason() == null, "absent FX accepted");
            Check(HandsAreNotBusyCompat.GetRejectionReason() == null, "absent HANB accepted");
            HollywoodFxCompat.Tick(); HandsAreNotBusyCompat.Tick();
        }
        Check(unpatches == calls, "absent polling never scans Harmony");
        Check(GameObject.Enumerations == enumerations, "absent polling never enumerates components");
    }
    private static void Add(string guid, BaseUnityPlugin plugin)
        => Chainloader.PluginInfos[guid] = new PluginInfo { Instance = plugin, Metadata = plugin.GetType().GetCustomAttribute<BepInPlugin>() };
    private static void PartialInstall()
    {
        Add(HandsAreNotBusyContract.PluginGuid, new HANB_Plugin());
        int calls = unpatches;
        throwAfterPatch = true;
        Check(HandsAreNotBusyCompat.GetRejectionReason()?.Contains("injected failure") == true, "partial install failure reported");
        Check(unpatches == calls + 1 && !(Harmony.GetPatchInfo(Fix)?.Prefixes.Any(p => p.owner == Owner) ?? false), "partial install failure unpatches held hooks");
        for (int i = 0; i < 1000; i++) Check(HandsAreNotBusyCompat.GetRejectionReason() != null, "failed installed bind remains cached");
        Check(unpatches == calls + 1, "cached failure never repatches or unpatches");
        Chainloader.PluginInfos.Clear();
        HandsAreNotBusyCompat.GetRejectionReason();

        Add(HandsAreNotBusyContract.PluginGuid, new HANB_Plugin());
        throwAfterPatch = true; throwUnpatch = true;
        Throws(() => HandsAreNotBusyCompat.GetRejectionReason(), "partial install cleanup failure surfaces");
        Check((bool)typeof(HandsAreNotBusyCompat).GetField("_patched", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null), "partial install cleanup failure retains hook responsibility");
        Check(typeof(HandsAreNotBusyCompat).GetField("_plugin", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == null, "failed cleanup invalidates cached success");
        throwUnpatch = false;
        Check(HandsAreNotBusyCompat.GetRejectionReason() == null, "partial cleanup retry installs complete adapter");
        Chainloader.PluginInfos.Clear(); HandsAreNotBusyCompat.GetRejectionReason();
    }
    private static void Install()
    {
        Add(HollywoodFxContract.PluginGuid, new Plugin());
        Add(HandsAreNotBusyContract.PluginGuid, new HANB_Plugin());
        Check(HollywoodFxCompat.GetRejectionReason() == null, "reviewed or structurally compatible FX installs");
        Check(HandsAreNotBusyCompat.GetRejectionReason() == null, "reviewed or structurally compatible HANB installs");
        int warnings = CoreLog.Warnings.Count, calls = unpatches;
        for (int i = 0; i < 1000; i++) { HollywoodFxCompat.GetRejectionReason(); HandsAreNotBusyCompat.GetRejectionReason(); }
        Check(CoreLog.Warnings.Count == warnings && unpatches == calls, "installed polls are cached and warning bounded");
#if FUTURE_MODS
        Check(CoreLog.Warnings.Count(w => w.Contains("newer than reviewed")) == 2, "newer shape-compatible releases warn once per mod");
#else
        Check(CoreLog.Warnings.All(w => !w.Contains("newer than reviewed")), "reviewed releases do not warn about versions");
#endif
    }
    private static void Contracts(string[] args)
    {
        var fxVersion = typeof(Plugin).GetCustomAttribute<BepInPlugin>().Version;
        var hanbVersion = typeof(HANB_Plugin).GetCustomAttribute<BepInPlugin>().Version;
        var fx = HollywoodFxContract.Resolve(typeof(Plugin), HollywoodFxContract.PluginGuid, fxVersion);
        var hanb = HandsAreNotBusyContract.Resolve(typeof(HANB_Plugin), HandsAreNotBusyContract.PluginGuid, hanbVersion);
        Check(args.Length == 2, "reviewed DLL evidence supplied");
        PublishedMetadata.Verify(args[0], fx, "HollywoodFX.Plugin", HollywoodFxContract.PluginGuid, "3.0.0", "3.0.0.37095", Check);
        PublishedMetadata.Verify(args[1], hanb, "HandsAreNotBusy.HANB_Plugin", HandsAreNotBusyContract.PluginGuid, "1.7.1", "1.0.0.0", Check);
        Throws(() => HollywoodFxContract.Resolve(typeof(Plugin), "foreign", fxVersion), "FX wrong GUID refused");
        Throws(() => HandsAreNotBusyContract.Resolve(typeof(HANB_Plugin), "foreign", hanbVersion), "HANB wrong GUID refused");
        Throws(() => HollywoodFxContract.Resolve(typeof(Plugin), HollywoodFxContract.PluginGuid, new Version(2, 9, 0)), "older FX refused");
        Throws(() => HandsAreNotBusyContract.Resolve(typeof(HANB_Plugin), HandsAreNotBusyContract.PluginGuid, new Version(1, 7, 0)), "older HANB refused");
        Throws(() => HandsAreNotBusyContract.Resolve(typeof(HANB_Plugin), HandsAreNotBusyContract.PluginGuid, new Version(9, 0, 0)), "dishonest version metadata refused");
    }
    private static LocalPlayer NewRaid()
    {
        if (game != null) { HollywoodFxCompat.RetireWorld(game.GameWorld); HandsAreNotBusyCompat.RetireWorld(game.GameWorld); }
        BodyReplacementContext.Reset();
        var world = new GameWorld();
        var body = new LocalPlayer { GameWorld = world };
        game = new LocalGame { GameWorld = world, LocalPlayer = body, Profile = body.Profile };
        Singleton<GameWorld>.Instance = world; Singleton<AbstractGame>.Instance = game;
        world.RegisterPlayer(body); GamePlayerOwner.MyPlayer = body;
        ImpactStatic.LocalPlayer = body;
        return body;
    }
    private static HANB_Component Component(LocalPlayer body, bool enabled = true)
    {
        var component = new HANB_Component { enabled = enabled }; component.SetPlayer(body); body.gameObject.Add(component); return component;
    }
    private static void Prepare(LocalPlayer body, Profile profile = null)
    {
        BodyReplacementContext.Begin(game, body, profile);
        HollywoodFxCompat.Prepare(game, body); HandsAreNotBusyCompat.Prepare(game, body);
    }
    private static LocalPlayer Candidate(LocalPlayer old, Profile profile = null)
    {
        var body = new LocalPlayer { Profile = profile ?? old.Profile, GameWorld = game.GameWorld };
        game.GameWorld.RegisterPlayer(body);
        return body;
    }
    private static void Wire(LocalPlayer body)
    {
        game.GameWorld.RegisterPlayer(body); game.LocalPlayer = body; game.Profile = body.Profile; GamePlayerOwner.MyPlayer = body;
    }
    private static void Publish(LocalPlayer old, LocalPlayer body)
    {
        Check(BodyReplacementContext.TryOwnCandidate(body), "replacement is a physically verified candidate");
        Wire(body); RespawnApi.Publish(old, body); BodyReplacementContext.EndAttempt();
    }
    private static void OrdinaryAndAbort()
    {
        var old = NewRaid(); var enabled = Component(old); var disabled = Component(old, false);
        enabled.Press(); Check(old.HandResets == 1, "ordinary native hotkey runs");
        var foreign = new LocalPlayer { Profile = old.Profile, GameWorld = game.GameWorld };
        Prepare(old);
        Check(enabled.enabled && !disabled.enabled && ReferenceEquals(ImpactStatic.LocalPlayer, old), "preparation preserves FX and component states");
        enabled.Press(); Check(old.HandResets == 1, "prepared old hands fenced");
        enabled.ResetOther(foreign); Check(foreign.HandResets == 1, "same-profile foreign physical body unaffected");
        HollywoodFxCompat.AbortPreparation(); HandsAreNotBusyCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        enabled.Press(); Check(old.HandResets == 2 && enabled.enabled && !disabled.enabled, "canceled preparation restores old hotkey unchanged");
        Prepare(old); HollywoodFxCompat.AbortPreparation(); HandsAreNotBusyCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        Check(true, "cancel leaves adapters ready for next replacement");
    }
    private static void CommitAndCandidate()
    {
        var old = NewRaid(); var oldComponent = Component(old); Prepare(old);
        var next = Candidate(old); var nextComponent = Component(next);
        nextComponent.Press(); Check(next.HandResets == 0, "uncommitted exact candidate hands fenced");
        var foreign = new LocalPlayer { Profile = old.Profile, GameWorld = game.GameWorld };
        nextComponent.ResetOther(foreign); Check(foreign.HandResets == 1, "same-profile foreign body not mistaken for candidate");
        Publish(old, next);
        Check(ReferenceEquals(ImpactStatic.LocalPlayer, next), "FX cache follows committed replacement");
        Check(!oldComponent.enabled && nextComponent.enabled, "only old native component disabled after commit");
        oldComponent.Press(); Check(old.HandResets == 0, "late retired reset fenced");
        nextComponent.Press(); Check(next.HandResets == 1, "new native component runs after commit");
        old.Destroyed = true; oldComponent.Press(); Check(old.HandResets == 0, "destroyed physical wrapper remains fenced");
        game.GameWorld.MainPlayer = old;
        oldComponent.Press(); Check(old.HandResets == 0, "retired tombstone survives stale MainPlayer reassignment");
        game.GameWorld.MainPlayer = next;

        var scavProfile = new Profile { Id = "own-scav-profile" };
        Prepare(next, scavProfile); var scav = Candidate(next, scavProfile); var scavComponent = Component(scav);
        scavComponent.Press(); Check(scav.HandResets == 0, "own-Scav exact candidate fenced");
        Publish(next, scav);
        Check(ReferenceEquals(ImpactStatic.LocalPlayer, scav) && !nextComponent.enabled && scavComponent.enabled, "own-Scav new profile rebinds both mods");
        var previousWorld = game.GameWorld;
        var current = NewRaid(); var currentComponent = Component(current);
        HollywoodFxCompat.RetireWorld(previousWorld); HandsAreNotBusyCompat.RetireWorld(previousWorld);
        Check(ReferenceEquals(ImpactStatic.LocalPlayer, current), "late prior-world retirement cannot clear new FX cache");
        scavComponent.Press(); Check(scav.HandResets == 1, "final current body is not tombstoned by world retirement");
        currentComponent.Press(); Check(current.HandResets == 1, "ordinary next-raid body unaffected");
        oldComponent.Press(); Check(old.HandResets == 0, "old tombstone survives raid retirement");
    }
    private static void RecoverOld()
    {
        var old = NewRaid(); var enabled = Component(old); var disabled = Component(old, false); Prepare(old);
        var rejected = Candidate(old); var rejectedComponent = Component(rejected);
        Check(BodyReplacementContext.TryOwnCandidate(rejected), "candidate validated before recovery to old body");
        Wire(old);
        RespawnApi.Fail(old); HollywoodFxCompat.Tick(); HandsAreNotBusyCompat.Tick();
        enabled.Press(); Check(old.HandResets == 0, "failure cannot release hands while creation is active");
        BodyReplacementContext.EndAttempt();
        enabled.Press(); Check(old.HandResets == 0, "prepared old reset remains fenced until recovery tick");
        HollywoodFxCompat.Tick(); HandsAreNotBusyCompat.Tick();
        enabled.Press(); Check(old.HandResets == 1 && enabled.enabled && !disabled.enabled, "old-body recovery resumes original enabled states");
        rejectedComponent.Press(); Check(rejected.HandResets == 0, "failed candidate is fenced after old-body recovery without prior callback");
        Check(ReferenceEquals(ImpactStatic.LocalPlayer, old), "recovered old FX owner stays current");
        try { Prepare(old); } catch { Check(false, "old-body recovery permits next attempt"); throw; }
        Check(true, "old-body recovery permits next attempt");
        var next = Candidate(old); Component(next); Publish(old, next);
        Check(ReferenceEquals(ImpactStatic.LocalPlayer, next) && !enabled.enabled, "replacement works after old-body recovery");
        NewRaid(); rejected.Destroyed = true; rejectedComponent.Press();
        Check(rejected.HandResets == 0, "failed candidate lease fences late destroyed callback after raid retirement");
    }
    private static void RecoverNew()
    {
        var old = NewRaid(); var component = Component(old); Prepare(old);
        var next = Candidate(old); var nextComponent = Component(next, false); Check(BodyReplacementContext.TryOwnCandidate(next), "failed-attempt candidate lease granted"); Wire(next);
        old.Destroyed = true; RespawnApi.Fail(old); BodyReplacementContext.EndAttempt();
        component.Press(); Check(old.HandResets == 0, "destroyed old reset remains fenced until recovery tick");
        nextComponent.Press(); Check(next.HandResets == 0, "failed candidate remains fenced until recovery tick");
        HollywoodFxCompat.Tick(); HandsAreNotBusyCompat.Tick();
        Check(ReferenceEquals(ImpactStatic.LocalPlayer, next) && !component.enabled, "failed attempt rebinds verified survivor despite destroyed old wrapper");
        Check(!nextComponent.enabled, "surviving new component retains its disabled setting");
        component.Press(); Check(old.HandResets == 0, "failed attempt survivor retires old reset path");
    }
    private static void EarlyAndForeignFx()
    {
        var old = NewRaid(); Component(old); ImpactStatic.LocalPlayer = null; Prepare(old);
        var next = Candidate(old); Component(next); Publish(old, next);
        Check(ImpactStatic.LocalPlayer == null, "early replacement leaves uninitialized upstream FX cache alone");
        ImpactStatic.LocalPlayer = next;
        Prepare(next); var third = Candidate(next); Component(third); Publish(next, third);
        Check(ReferenceEquals(ImpactStatic.LocalPlayer, third), "upstream initialization after early entry supports next rebind");
        var priorWorldBody = third;
        old = NewRaid(); Component(old); ImpactStatic.LocalPlayer = priorWorldBody;
        BodyReplacementContext.Begin(game, old);
        Throws(() => HollywoodFxCompat.Prepare(game, old), "stale previous-world FX owner refuses preparation");
        Check(ReferenceEquals(ImpactStatic.LocalPlayer, priorWorldBody), "foreign FX refusal never mutates cache");
        HollywoodFxCompat.AbortPreparation(); HandsAreNotBusyCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        ImpactStatic.LocalPlayer = old;
        Prepare(old); HollywoodFxCompat.AbortPreparation(); HandsAreNotBusyCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
    }
    private static void WrongComponent()
    {
        var old = NewRaid(); var component = Component(old); var wrong = new LocalPlayer { Profile = old.Profile, GameWorld = game.GameWorld }; component.SetPlayer(wrong);
        BodyReplacementContext.Begin(game, old); HollywoodFxCompat.Prepare(game, old);
        Throws(() => HandsAreNotBusyCompat.Prepare(game, old), "foreign component owner refuses preparation");
        Check(component.enabled, "foreign component refusal never disables it");
        HollywoodFxCompat.AbortPreparation(); HandsAreNotBusyCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
    }
    private static void UnpatchFailure()
    {
        HollywoodFxCompat.RetireWorld(game.GameWorld); HandsAreNotBusyCompat.RetireWorld(game.GameWorld); BodyReplacementContext.Reset();
        Chainloader.PluginInfos.Clear();
        throwUnpatch = true;
        Throws(() => HandsAreNotBusyCompat.GetRejectionReason(), "unpatch failure is surfaced");
        Check((bool)typeof(HandsAreNotBusyCompat).GetField("_patched", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null), "unpatch failure retains cleanup responsibility");
        Check(Harmony.GetPatchInfo(Fix).Prefixes.Count(p => p.owner == Owner) == 1, "unpatch failure leaves actual hook tracked");
        throwUnpatch = false;
        int calls = unpatches;
        Check(HandsAreNotBusyCompat.GetRejectionReason() == null && unpatches == calls + 1, "next poll retries failed unpatch exactly once");
        Check(!(Harmony.GetPatchInfo(Fix)?.Prefixes.Any(p => p.owner == Owner) ?? false), "successful cleanup removes actual hook");
        Absent();
    }
}

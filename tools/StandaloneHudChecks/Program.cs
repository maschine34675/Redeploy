using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using DrakiaXYZ.QuestTracker;
using DrakiaXYZ.QuestTracker.Components;
using DrakiaXYZ.QuestTracker.Helpers;
using EFT;
using EFT.Quests;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;
using AudioPatch = AccessibilityIndicators.Patches.GameWorldUnregisterPlayerPatch;
using AudioPanel = AccessibilityIndicators.IndicatorUI.Panel;

internal static class Program
{
    private static int _checks;
    private static void Check(bool yes, string message) { _checks++; if (!yes) throw new Exception("CHECK FAILED: " + message); }
    private static void Throws(Action action, string message)
    { bool thrown = false; try { action(); } catch { thrown = true; } Check(thrown, message); }
    private static void Succeeds(Action action, string message)
    { bool passed = true; try { action(); } catch { passed = false; } Check(passed, message); }
    private static void Prepare(LocalGame game, LocalPlayer old)
    { BodyReplacementContext.Begin(game, old); QuestTrackerCompat.Prepare(game, old); AudioIndicatorsCompat.Prepare(game, old); }
    private static LocalPlayer Candidate(LocalGame game, Profile profile, string questId)
    {
        var body = new LocalPlayer { GameWorld = game.GameWorld, Profile = profile };
        var quest = new Quest { Id = questId, Progress = 5 }; body.QuestController.Quests.Add(quest);
        profile.QuestsData.Clear(); profile.QuestsData.Add(quest);
        return body;
    }
    private static void Confirm(LocalGame game, LocalPlayer old, LocalPlayer next, bool publish = true)
    {
        game.GameWorld.RegisterPlayer(next);
        Check(BodyReplacementContext.TryOwnCandidate(next), "Core owns candidate");
        game.LocalPlayer = next; game.Profile = next.Profile; GamePlayerOwner.MyPlayer = next;
        if (publish) RespawnApi.Publish(old, next);
        BodyReplacementContext.EndAttempt(); QuestTrackerCompat.Tick(); AudioIndicatorsCompat.Tick();
    }
    public static void Main(string[] args)
    {
        Check(QuestTrackerCompat.GetRejectionReason() == null && AudioIndicatorsCompat.GetRejectionReason() == null, "absent mods no-op");
        var questPlugin = new QuestTrackerPlugin(); var audioPlugin = new AccessibilityIndicators.Plugin();
        var questMeta = (BepInPlugin)Attribute.GetCustomAttribute(typeof(QuestTrackerPlugin), typeof(BepInPlugin));
        var audioMeta = (BepInPlugin)Attribute.GetCustomAttribute(typeof(AccessibilityIndicators.Plugin), typeof(BepInPlugin));
        Chainloader.PluginInfos[QuestTrackerContract.PluginGuid] = new PluginInfo { Instance = questPlugin, Metadata = questMeta };
        Chainloader.PluginInfos[AudioIndicatorsContract.PluginGuid] = new PluginInfo { Instance = audioPlugin, Metadata = audioMeta };
        Check(QuestTrackerCompat.GetRejectionReason() == null, "Quest Tracker contract binds");
        Check(AudioIndicatorsCompat.GetRejectionReason() == null, "Audio contract accepts real 2.1.0/assembly1.5.0 combination");
        // The released mod installs this callback as a postfix on native GameWorld.UnregisterPlayer.
        // Exercise the nested patched callback through an actual Harmony-generated native wrapper too.
        new HarmonyLib.Harmony("fixture.foreign.audio").Patch(typeof(GameWorld).GetMethod(nameof(GameWorld.UnregisterPlayer)),
            postfix: new HarmonyLib.HarmonyMethod(typeof(AudioPatch).GetMethod(nameof(AudioPatch.PatchPostFix))));
        QuestTrackerCompat.GetRejectionReason(); AudioIndicatorsCompat.GetRejectionReason();
#if FUTURE_MODS
        Check(Redeploy.Core.CoreLog.Warnings.Count == 2, "forward versions warn exactly once per adapter");
#else
        Check(Redeploy.Core.CoreLog.Warnings.Count == 0, "reviewed versions need no warning");
#endif
        PublishedMetadata.Verify(args[0], QuestTrackerContract.Resolve(typeof(QuestTrackerPlugin), QuestTrackerContract.PluginGuid, questMeta.Version), QuestTrackerContract.PluginGuid, "1.7.0", "1.7.0.0", Check);
        PublishedMetadata.Verify(args[1], AudioIndicatorsContract.Resolve(typeof(AccessibilityIndicators.Plugin), AudioIndicatorsContract.PluginGuid, audioMeta.Version), AudioIndicatorsContract.PluginGuid, "2.1.0", "1.5.0.0", Check);
        Throws(() => QuestTrackerContract.Resolve(typeof(QuestTrackerPlugin), QuestTrackerContract.PluginGuid, new System.Version(1,6,0)), "old release refuses");
        Throws(() => AudioIndicatorsContract.Resolve(typeof(AccessibilityIndicators.Plugin), "wrong", new System.Version(2,1,0)), "wrong GUID refuses");
        Throws(() => AudioIndicatorsContract.Resolve(typeof(AccessibilityIndicators.Plugin), AudioIndicatorsContract.PluginGuid, new System.Version(2,2,0)), "lying metadata refuses");

        var world = new LocalGameWorld(); var game = new LocalGame { GameWorld = world };
        Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = world;
        var profile = new Profile(); var old = Candidate(game, profile, "quest-a");
        game.LocalPlayer = old; game.Profile = profile; GamePlayerOwner.MyPlayer = old; world.RegisterPlayer(old);
        QuestsTracker.PreferenceOwner = profile; QuestsTracker.Preferences["quest-a"] = new();
        var preference = QuestsTracker.Preferences["quest-a"];
        var tracker = new QuestTrackerComponent(); tracker.Initialize(game, old); AudioPanel.Create();
        var panel = tracker.Panel; var hud = AudioPanel.IndicatorHUD; var rotation = AudioPanel.Rotation;
        panel.Visible = false; tracker.hideTime = 73;
        tracker.Hooked("Update"); rotation.HookedUpdate();
        Check(tracker.Updates == 1 && rotation.Updates == 1, "ordinary initial raid updates unchanged");
        AudioPatch.Hooked(new Player { IsYourPlayer = false }); Check(AudioPanel.Disposes == 0, "bot unregister unchanged");
        Prepare(game, old);
        tracker.Hooked("Update"); rotation.HookedUpdate(); QuestsTracker.Change();
        Check(tracker.Updates == 1 && rotation.Updates == 1, "pending callbacks fenced without foreign mutation");
        Check(ReferenceEquals(tracker.Bound, old) && ReferenceEquals(tracker.Panel, panel), "prepare leaves old tracker intact");
        QuestTrackerCompat.AbortPreparation(); AudioIndicatorsCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        tracker.Hooked("Update"); rotation.HookedUpdate();
        Check(tracker.Updates == 2 && rotation.Updates == 2 && rotation.Active, "cancel releases fences and preserves HUD");

        Succeeds(() => Prepare(game, old), "cancel leaves adapters ready for next replacement");
        AudioPatch.Hooked(old); world.UnregisterPlayer(old);
        rotation.Stop(); // A foreign cleanup may have already stopped the formerly-active compass.
        var rotationUpdates = rotation.Updates;
        AudioIndicatorsCompat.Tick(); rotation.HookedUpdate();
        Check(rotation.Updates == rotationUpdates && !rotation.Active, "unregistered old body cannot resume compass");
        world.RegisterPlayer(old); // LiveKitRespawn.RecoverOldBody restores the original physical player.
        AudioIndicatorsCompat.Tick(); rotation.HookedUpdate();
        Check(rotation.Updates == rotationUpdates && !rotation.Active, "old-body recovery waits for transaction completion");
        BodyReplacementContext.EndAttempt(); QuestTrackerCompat.Tick(); AudioIndicatorsCompat.Tick();
        rotation.HookedUpdate();
        Check(rotation.Active && rotation.Updates == rotationUpdates + 1 && ReferenceEquals(rotation.CameraBody, old), "recovered old body resumes formerly-active compass");
        var audioScope = (StandaloneHudScope)typeof(AudioIndicatorsCompat).GetField("Scope", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).GetValue(null);
        Check(audioScope.Prepared == null && !audioScope.Frozen && ReferenceEquals(audioScope.Body, old) && !audioScope.Retired(old), "old-body recovery clears preparation without tombstone");
        Check(ReferenceEquals(AudioPanel.IndicatorHUD, hud) && ReferenceEquals(AudioPanel.Rotation, rotation), "old-body recovery retains existing HUD");
        Succeeds(() => Prepare(game, old), "old-body recovery allows subsequent replacement");
        AudioPatch.Hooked(old); world.UnregisterPlayer(old);
        Check(AudioPanel.Disposes == 0 && rotation.Active, "exact retired unregister cannot stop compass");
        var next = Candidate(game, profile, "quest-a");
        // Same profile ID is deliberately insufficient authority.
        AudioPatch.Hooked(new LocalPlayer { Profile = profile, GameWorld = world });
        Check(AudioPanel.Disposes == 1, "same-profile foreign physical unregister not suppressed");
        Confirm(game, old, next);
        Check(ReferenceEquals(tracker.Bound, next) && ReferenceEquals(tracker.Controller, next.QuestController), "quest references follow replacement");
        Check(ReferenceEquals(panel.Map.Single(), next.QuestController.Quests.Single()) && ReferenceEquals(panel.Tracked["quest-a"].Item1, next.QuestController.Quests.Single()), "both quest caches rebuilt with new quest objects");
        Check(ReferenceEquals(panel.Tracked["quest-a"].Item2, preference) && ReferenceEquals(QuestsTracker.PreferenceOwner, profile), "tracking preference ownership unchanged");
        Check(ReferenceEquals(panel, tracker.Panel) && !panel.Visible && tracker.hideTime == 73 && tracker.Hash == 5, "panel, visibility, hide timer and progress hash preserved");
        Check(QuestsTracker.Handlers == 1, "no duplicate event handlers");
        Check(ReferenceEquals(AudioPanel.IndicatorHUD, hud) && ReferenceEquals(AudioPanel.Rotation, rotation) && rotation.Active, "same HUD restored only after committed authority");
        rotation.HookedUpdate(); Check(ReferenceEquals(rotation.CameraBody, next), "compass reads replacement camera");
        AudioPatch.Hooked(old); Check(AudioPanel.Disposes == 1 && rotation.Active, "late old-body disposal fenced");
        var updates = tracker.Updates; tracker.Hooked("Update"); Check(tracker.Updates == updates + 1, "confirmed tracker resumes");

        // Profile-switch candidate has a different quest controller but the user's main-PMC preferences remain intact.
        var scav = new Profile { Id = "scav" };
        BodyReplacementContext.Begin(game, next, scav); QuestTrackerCompat.Prepare(game, next); AudioIndicatorsCompat.Prepare(game, next);
        AudioPatch.Hooked(next); world.UnregisterPlayer(next);
        var third = Candidate(game, scav, "scav-quest");
        Confirm(game, next, third, false); // Completed recovery without event delivery must also rebind.
        Check(ReferenceEquals(tracker.Bound, third) && ReferenceEquals(tracker.Controller, third.QuestController), "completed PMC-to-Scav recovery follows exact new controller");
        Check(panel.Tracked.Count == 0 && panel.Map.Count == 1 && ReferenceEquals(QuestsTracker.PreferenceOwner, profile) && QuestsTracker.Preferences.Count == 1, "Scav caches rebuilt without erasing PMC preferences");
        Check(QuestsTracker.Handlers == 1 && !panel.Visible && tracker.hideTime == 73, "repeated swap preserves UI and event ownership");
        AudioPatch.Hooked(next); Check(AudioPanel.Disposes == 1, "second retired body disposal fenced");

        Prepare(game, third); AudioPatch.Hooked(third); world.UnregisterPlayer(third);
        BodyReplacementContext.EndAttempt();
        tracker.Hooked("Update"); rotation.HookedUpdate();
        Check(ReferenceEquals(tracker.Bound, third), "failed creation never adopts nonexistent body");
        game.Status = GameStatus.Stopping;
        QuestTrackerCompat.RetireWorld(world); AudioIndicatorsCompat.RetireWorld(world);
        AudioPatch.Hooked(third); Check(AudioPanel.Disposes == 2 && !rotation.Active, "ordinary final current-body cleanup still runs after retire");
        updates = tracker.Updates; tracker.Hooked("Update"); Check(tracker.Updates == updates, "retired component late update ignored");
        tracker.OnDestroy(); Check(QuestsTracker.Handlers == 0, "native OnDestroy detaches original events");
        BodyReplacementContext.Reset();
        var other = new LocalGameWorld(); Singleton<GameWorld>.Instance = other;
        AudioPanel.Create(); AudioPatch.Hooked(old);
        Check(AudioPanel.Rotation.Active && AudioPanel.Disposes == 2, "old-world callback cannot stop next raid's HUD");
        AudioPatch.Hooked(new LocalPlayer { GameWorld = other });
        Check(!AudioPanel.Rotation.Active && AudioPanel.Disposes == 3, "unowned normal raid final cleanup untouched");
        Console.WriteLine("STANDALONE_HUD_CHECKS_PASSED assertions=" + _checks);
    }
}

using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using RAID_REVIEW;
using Redeploy.Core.Api;
using Redeploy.Core.Compat;
using Redeploy.Core.Engine;
using UnityEngine;
using RR = RAID_REVIEW.RAID_REVIEW;

internal static class Program
{
    private static int _checks, _patchCalls, _unpatchCalls, _failPatchAt;
    private static bool _failUnpatch;
    private const string Owner = "com.maschine.Redeploy.Core.RaidReviewCompat";
    private static LocalGame _game;
    private static ClientGameWorld _world;
    private static LocalPlayer _body;
    private static void Check(bool ok, string name) { _checks++; if (!ok) throw new Exception("CHECK FAILED: " + name); }
    private static void PatchProbe(Harmony __instance)
    {
        if (__instance.Id != Owner) return;
        _patchCalls++;
        if (_failPatchAt > 0 && _patchCalls == _failPatchAt) throw new InvalidOperationException("injected partial patch failure");
    }
    private static void UnpatchProbe(Harmony __instance)
    {
        if (__instance.Id != Owner) return;
        _unpatchCalls++;
        if (_failUnpatch) { _failUnpatch = false; throw new InvalidOperationException("injected unpatch failure"); }
    }
    private static void InstallPlugin() => Chainloader.PluginInfos[ RaidReviewContract.PluginGuid ] = new PluginInfo
    { Instance = new RR(), Metadata = new BepInPlugin(RaidReviewContract.PluginGuid, "RR", "1.10.0") };
    private static void NewWorld(bool claim = true, bool available = true, bool earlySwap = false, bool start = true)
    {
        BodyReplacementContext.Reset(); RespawnApi.Pending = false;
        DeathInterception.Claim = claim; RespawnApi.IsAvailable = available;
        _world = new ClientGameWorld(); _body = Body(new Profile { Id = "pmc" });
        _game = new LocalGame { GameWorld = _world, Profile = _body.Profile, LocalPlayer = _body, Status = GameStatus.Starting };
        Singleton<GameWorld>.Instance = _world; Singleton<AbstractGame>.Instance = _game;
        _world.RegisterPlayer(_body); GamePlayerOwner.MyPlayer = _body;
        RaidReviewCompat.BeforeRaidStart(_world);
        // Shared Core GameStarted postfix resets these between prefix and RR's postfix.
        BodyReplacementContext.Reset();
        if (earlySwap)
        {
            var old = _body; BodyReplacementContext.Begin(_game, old); RaidReviewCompat.Prepare(_game, old);
            _body = Body(old.Profile); Wire(_body); BodyReplacementContext.TryOwnCandidate(_body);
            RespawnApi.Publish(old, _body); BodyReplacementContext.EndAttempt();
        }
        RR.gameWorld = _world; RR.myPlayer = _body; RR.sessionId = "pmc-owner";
        RR.trackingPlayers.Clear(); RecordingRoster.Reset(); RecordingInventory.captured.Clear();
        RAID_REVIEW_Player_OnItemAddedOrRemovedPatch._pendingRemoves.Clear(); Telemetry.sender = new OrderedPacketSender();
        if (start)
        {
            GameWorld world = _world; RAID_REVIEW_Player_OnGameStartedPatch.PatchPostFix(ref world);
        }
    }
    private static LocalPlayer Body(Profile profile)
    {
        var body = new LocalPlayer { Profile = profile, GameWorld = _world };
        body.gameObject.Add(body); return body;
    }
    private static void Wire(LocalPlayer body)
    {
        _world.RegisterPlayer(body); _world.MainPlayer = body; _game.LocalPlayer = body; _game.Profile = body.Profile;
        GamePlayerOwner.MyPlayer = body;
    }
    private static void Resolve()
    { _game.Status = GameStatus.Started; Time.frameCount++; RaidReviewCompat.Tick(); }
    private static LocalPlayer Replace(bool dead, Profile profile = null, Action<LocalPlayer, LocalPlayer> pending = null, bool recover = false)
    {
        var old = _body;
        if (dead) { old.HealthController.IsAlive = false; RecordingRoster.Confirm(old, PlayerStatus.Dead); RecordingRoster.Observe(old); }
        BodyReplacementContext.Begin(_game, old, profile); RaidReviewCompat.Prepare(_game, old);
        var next = Body(profile ?? old.Profile);
        _world.MainPlayer = next;
        Check(BodyReplacementContext.TryOwnCandidate(next), "candidate physically leased");
        var count = Telemetry.sender.Packets.Count;
        RecordingRoster.Add(next); RecordingInventory.Capture(next);
        Check(!RecordingRoster.Observe(next) && Telemetry.sender.Packets.Count == count, "uncommitted candidate telemetry suppressed");
        RAID_REVIEW_Player_OnItemAddedOrRemovedPatch.Capture(next, new Item { Id = "engine-transfer" }, null, true);
        Check(Telemetry.sender.Packets.Count == count, "candidate transfer is not loot");
        pending?.Invoke(old, next);
        Wire(next);
        if (recover)
        {
            RespawnApi.Fail(old); BodyReplacementContext.EndAttempt();
            var held = Telemetry.sender.Packets.Count;
            Check(!RecordingRoster.Observe(next), "candidate stays fenced after attempt ends before recovery");
            RecordingInventory.Capture(next);
            Check(Telemetry.sender.Packets.Count == held, "candidate interval cannot consume inventory snapshot");
            RaidReviewCompat.Tick();
        }
        else { RespawnApi.Publish(old, next); BodyReplacementContext.EndAttempt(); }
        _body = next;
        Check(RecordingRoster.active.TryGetValue(next.ProfileId, out var active) && ReferenceEquals(active, next), "replacement enrolled in live roster");
        Check(RecordingRoster.Observe(next), "replacement survives old confirmed death");
        return old;
    }
    private static T Last<T>() => Telemetry.sender.Packets.Select(p => p.Value).OfType<T>().Last();
    private static void Shot(Player actor, Player victim)
    {
        var shot = new Shot { Player = new Bridge { iPlayer = actor }, HittedBallisticCollider = victim };
        var world = _world; RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.PatchPostFix(ref world, shot);
    }
    private static void Kill(Player actor, Player victim)
        => RAID_REVIEW_Player_OnBeenKilledByAggressorPatch.PatchPostFix(ref victim, actor, default, EBodyPart.Chest, EDamageType.Bullet);
    private static void End(ExitStatus status)
    { Player body = _body; RAID_REVIEW_Player_OnGameSessionEndPatch.PatchPostFix(ref body, status, 0, "factory", "Gate"); }
    private static void NoModAndPartialFailure()
    {
        var probe = new Harmony("rr-harness-probe");
        var patch = typeof(Harmony).GetMethods().Single(m => m.Name == "Patch" && m.GetParameters().Length == 6);
        probe.Patch(patch, new HarmonyMethod(typeof(Program), nameof(PatchProbe)));
        probe.Patch(AccessTools.Method(typeof(Harmony), "UnpatchSelf"), new HarmonyMethod(typeof(Program), nameof(UnpatchProbe)));
        for (var i = 0; i < 2000; i++) Check(RaidReviewCompat.GetRejectionReason() == null, "absent admission stays open");
        Check(_patchCalls == 0 && _unpatchCalls == 0, "absent plugin does not patch or enumerate for unpatch");
        InstallPlugin(); _failPatchAt = 3; _failUnpatch = true;
        Check(RaidReviewCompat.GetRejectionReason() != null, "partial patch rejects admission");
        Check((bool)typeof(RaidReviewCompat).GetField("_patched", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null), "throwing cleanup retains held flag");
        for (var i = 0; i < 1000; i++) Check(RaidReviewCompat.GetRejectionReason() != null, "failed identity stays rejected");
        Check(_patchCalls == 3 && _unpatchCalls == 2, "failed bind cached; only held cleanup retries");
        NewWorld(start: false);
        Check(RaidReviewCompat.GetRejectionReason() != null && _patchCalls == 3 && _unpatchCalls == 2, "new raid preserves rejected plugin admission without retrying scans");
        Check(!Harmony.GetAllPatchedMethods().Any(m => Harmony.GetPatchInfo(m).Owners.Contains(Owner)), "partial installation removed fully");
        _failPatchAt = 0; InstallPlugin();
        var rejection = RaidReviewCompat.GetRejectionReason();
        Check(rejection == null, "replacement plugin identity retries successfully: " + rejection);
    }
    private static void Lifecycle()
    {
        foreach (var mode in new[] { "active standalone", "active Arena", "inactive Arena", "disabled Redeploy", "missing server", "Fika", "no driver" })
        {
            var active = mode.StartsWith("active ");
            NewWorld(claim: active, available: mode != "Fika" && mode != "no driver");
            Check(Telemetry.sender.Packets.Count == 0, "startup waits for postfixes: " + mode);
            Resolve();
            Check(Last<TrackingPlayer>().profileId == (active ? "pmc:redeploy:1" : "pmc"), "claimant controls aliases: " + mode);
            Check(Last<TrackingRaid>().profileId == "pmc-owner" && Last<TrackingRaid>().sessionId == "pmc-owner", "real owner/session preserved: " + mode);
            if (!active)
            {
                End(ExitStatus.Survived);
                Check(typeof(RaidReviewCompat).GetField("_world", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == null,
                    "passthrough END releases physical world: " + mode);
            }
        }
        NewWorld(earlySwap: true); Resolve();
        Check(Last<TrackingPlayerInventory>().profileId == "pmc:redeploy:1" && !Telemetry.sender.Faulted, "entry replacement before START uses actual first recorder body");
        NewWorld(start: false);
        var previous = _body;
        BodyReplacementContext.Begin(_game, previous); RaidReviewCompat.Prepare(_game, previous);
        var candidate = Body(previous.Profile); _world.MainPlayer = candidate; BodyReplacementContext.TryOwnCandidate(candidate);
        GameWorld pendingWorld = _world; RAID_REVIEW_Player_OnGameStartedPatch.PatchPostFix(ref pendingWorld);
        Check(Telemetry.sender.Packets.Count == 0 && !Telemetry.sender.Faulted, "in-flight entry replacement defers original RR start");
        Time.realtimeSinceStartup += 31; Time.frameCount++; RaidReviewCompat.Tick();
        Check(!Telemetry.sender.Faulted && Telemetry.sender.Packets.Count == 0, "deferred startup allows Core thirty-second creation deadline");
        Wire(candidate); RespawnApi.Publish(previous, candidate); BodyReplacementContext.EndAttempt(); _body = candidate;
        Resolve(); Resolve();
        Check(Last<TrackingPlayerInventory>().profileId == "pmc:redeploy:1" && Telemetry.sender.Packets.Count(p => p.Action == "START") == 1,
            "deferred original start records confirmed body exactly once");
        NewWorld(start: false); Resolve();
        BodyReplacementContext.Begin(_game, _body); RaidReviewCompat.Prepare(_game, _body); RaidReviewCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        Check(Telemetry.sender.Packets.Count == 0, "disabled recording has no synthetic packets");
        NewWorld(claim: false); End(ExitStatus.Survived); Resolve();
        Check(Telemetry.sender.Packets.Last().Action == "END" && !Telemetry.sender.Packets.Any(p => p.Action == "PLAYER_STATUS"), "same-frame END inactive flushes raw only");
        NewWorld(); End(ExitStatus.Survived); Resolve();
        Check(Telemetry.sender.Packets.Last().Action == "END" && Telemetry.sender.Packets.Select(p => p.Value).OfType<TrackingPlayerDeadOrUnspawned>().LastOrDefault()?.status == PlayerStatus.Extracted, "same-frame END active emits terminal status before END");
        Check(typeof(RaidReviewCompat).GetField("_world", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == null, "completed END releases world references");
        BodyReplacementContext.Begin(_game, _body); RaidReviewCompat.Prepare(_game, _body); BodyReplacementContext.CancelPreparation();
        Check(true, "voluntary recording end does not block later replacement");
        NewWorld(); Replace(false); Resolve();
        Check(Last<TrackingPlayer>().profileId == "pmc:redeploy:2" && Telemetry.sender.Packets.OfType<object>().Count() > 0, "committed replacement before startup flush keeps ordered lives");
        Replace(false); Replace(false);
        Check(Telemetry.sender.Packets.Where(p => p.Action == "PLAYER").Select(p => ((TrackingPlayer)p.Value).profileId).Distinct().Count() == 4,
            "rapid commits retain every participant without upstream capture ticks");
        NewWorld(); Replace(false); DeathInterception.Claim = false; _game.Status = GameStatus.Stopping; End(ExitStatus.Survived);
        Time.frameCount++; RaidReviewCompat.Tick();
        Check(Telemetry.sender.Packets.Where(p => p.Action == "PLAYER").Select(p => ((TrackingPlayer)p.Value).profileId)
            .SequenceEqual(new[] { "pmc:redeploy:1", "pmc:redeploy:2" }), "committed startup ownership survives same-frame driver shutdown");
    }
    private static void MultiLife(string artifact)
    {
        NewWorld(); Resolve();
        var initial = _body; var firstPacket = Last<TrackingPlayer>(); var raw = RR.trackingPlayers["pmc"];
        Check(raw.profileId == "pmc" && firstPacket.profileId == "pmc:redeploy:1" && firstPacket.name == "Tester [life 1]", "metadata detached with life name");
        RAID_REVIEW_Player_OnItemAddedOrRemovedPatch.Capture(_body, new Item { Id = "drop-before-respawn" }, null, false);
        var first = Replace(true);
        Check(Last<TrackingLootItem>().profileId == "pmc:redeploy:1", "pending loot remains original life");
        Check(firstPacket.profileId == "pmc:redeploy:1" && raw.profileId == "pmc", "queued snapshot remains immutable after respawn");
        var count = Telemetry.sender.Packets.Count;
        RecordingRoster.Confirm(first, PlayerStatus.Dead); RecordingRoster.Depart(first); RecordingRoster.Add(first);
        Check(!RecordingRoster.Observe(first) && count == Telemetry.sender.Packets.Count, "old corpse roster callbacks fenced");
        Check(RecordingRoster.Observe(_body), "old late death cannot poison current life");
        Shot(_body, first);
        Check(Last<TrackingBallistic>().profileId == "pmc:redeploy:2" && Last<TrackingBallistic>().hitPlayerId == "pmc:redeploy:1", "shot source and old corpse retain physical provenance");
        Kill(first, _body);
        Check(Last<TrackingRaidKill>().profileId == "pmc:redeploy:1" && Last<TrackingRaidKill>().killedId == "pmc:redeploy:2", "old grenade kill maps both physical actors");
        RAID_REVIEW_Player_OnItemAddedOrRemovedPatch.Capture(_body, new Item { Id = "real-loot" }, null, true);
        Check(Last<TrackingLootItem>().profileId == "pmc:redeploy:2", "real post-respawn loot retained");
        var batch = new[] { new TrackingPlayerData { sessionId = RR.sessionId, profileId = "pmc", time = 2 } };
        Telemetry.SendSnapshot("POSITION_BATCH", batch);
        Check(((object[])Telemetry.sender.Packets.Last().Value).OfType<TrackingPlayerData>().Single().profileId == "pmc:redeploy:2" && batch[0].profileId == "pmc", "position batch detached");
        RespawnApi.Pending = true; count = Telemetry.sender.Packets.Count; End(ExitStatus.Killed);
        Check(Telemetry.sender.Packets.Count == count, "partial PMC statistics end keeps recording open");
        RespawnApi.Pending = false;
        Replace(true, new Profile { Id = "scav" }, (old, candidate) =>
        {
            Shot(first, old);
            Check(Last<TrackingBallistic>().profileId == "pmc:redeploy:1", "old projectile during later creation window retained");
            var stranger = Body(old.Profile); RecordingRoster.Add(stranger);
            Check(ReferenceEquals(RecordingRoster.active[old.ProfileId], stranger), "same-profile foreign wrapper not treated as Core candidate");
        });
        Check(Last<TrackingPlayerInventory>().profileId == "scav:redeploy:3", "own Scav gets fresh recording inventory");
        Check(initial.ProfileId == "pmc" && _body.ProfileId == "scav" && RR.sessionId == "pmc-owner", "actual game IDs unchanged");
        Telemetry.SendSnapshot("POSITION_BATCH", new[] { new TrackingPlayerData { sessionId = RR.sessionId, profileId = "scav", time = 4 } });
        _game.Status = GameStatus.Stopping; RaidReviewCompat.RetireWorld(_world); End(ExitStatus.Survived);
        Check(Last<TrackingPlayerDeadOrUnspawned>().profileId == "scav:redeploy:3" && Last<TrackingPlayerDeadOrUnspawned>().status == PlayerStatus.Extracted, "Scav explicit extraction after Core retirement");
        Check(Telemetry.sender.Packets.Count(p => p.Action == "START") == 1 && Telemetry.sender.Packets.Count(p => p.Action == "END") == 1, "single START and END across all lives");
        Check(Last<TrackingRaid>().profileId == "pmc-owner" && Last<TrackingRaid>().type == "PMC", "recording owner and entry type preserved");
        File.WriteAllText(Path.Combine(artifact, "projected-packets.json"), JsonConvert.SerializeObject(Telemetry.sender.Packets.Select(p => new { action = p.Action, snapshot = p.Value }), Formatting.Indented, new StringEnumConverter()));
        var oldWorld = _world; var oldScav = _body;
        NewWorld(); Resolve(); count = Telemetry.sender.Packets.Count;
        RaidReviewCompat.RetireWorld(oldWorld); RecordingRoster.Depart(oldScav); Shot(oldScav, _body);
        Check(count == Telemetry.sender.Packets.Count && RecordingRoster.Observe(_body), "late old-world teardown cannot corrupt new raid");
    }
    private static void RecoveryAndFailures()
    {
        NewWorld(); Resolve();
        BodyReplacementContext.Begin(_game, _body); RaidReviewCompat.Prepare(_game, _body); RaidReviewCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        Check(RecordingRoster.Observe(_body) && Last<TrackingPlayerDeadOrUnspawned>().profileId == "pmc:redeploy:1", "aborted preparation preserves original life");
        BodyReplacementContext.Begin(_game, _body); RaidReviewCompat.Prepare(_game, _body); RespawnApi.Fail(_body); BodyReplacementContext.EndAttempt(); RaidReviewCompat.Tick();
        Check(RecordingRoster.Observe(_body), "recovered original living body stays observable");
        BodyReplacementContext.Begin(_game, _body); RaidReviewCompat.Prepare(_game, _body);
        var abandoned = Body(_body.Profile); _world.MainPlayer = abandoned; BodyReplacementContext.TryOwnCandidate(abandoned); RecordingRoster.Add(abandoned);
        Wire(_body); RespawnApi.Fail(_body); BodyReplacementContext.EndAttempt(); RaidReviewCompat.Tick();
        var held = Telemetry.sender.Packets.Count; RecordingRoster.Add(abandoned); RecordingInventory.Capture(abandoned);
        Check(!RecordingRoster.Observe(abandoned) && Telemetry.sender.Packets.Count == held, "abandoned candidate remains fenced after original-body recovery");
        BodyReplacementContext.Begin(_game, _body); RaidReviewCompat.Prepare(_game, _body);
        var silentCandidate = Body(_body.Profile); _world.MainPlayer = silentCandidate; BodyReplacementContext.TryOwnCandidate(silentCandidate);
        Wire(_body); RespawnApi.Fail(_body); BodyReplacementContext.EndAttempt(); RaidReviewCompat.Tick();
        held = Telemetry.sender.Packets.Count; RecordingRoster.Add(silentCandidate); RecordingInventory.Capture(silentCandidate);
        Check(!RecordingRoster.Observe(silentCandidate) && Telemetry.sender.Packets.Count == held, "unobserved abandoned candidate cannot join recorder after recovery");
        Replace(false, recover: true);
        Check(Last<TrackingPlayer>().profileId == "pmc:redeploy:2", "recovered committed replacement gets one new life");
        RecordingRoster.ThrowObserve = true;
        try { RecordingRoster.Observe(_body); } catch (InvalidOperationException) { }
        RecordingRoster.ThrowObserve = false;
        Check(typeof(RaidReviewCompat).GetField("_actor", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == null, "exception finalizer restores actor scope");
        var bot = new Player { Profile = new Profile { Id = "bot" }, IsYourPlayer = false, GameWorld = _world }; bot.gameObject.Add(bot);
        RecordingRoster.Nested = () => Shot(bot, _body);
        RecordingRoster.Observe(_body); RecordingRoster.Nested = null;
        Check(Last<TrackingPlayerDeadOrUnspawned>().profileId == "pmc:redeploy:2", "nested scope restores outer actor");
        NewWorld(); for (var i = 0; i < 257; i++) Telemetry.SendSnapshot("SWITCH", new object());
        Check(Telemetry.sender.Faulted && Telemetry.sender.Reason.Contains("overflow"), "bounded startup overflow explicitly faults recording");
        NewWorld(); Telemetry.sender.Reject = true; Resolve();
        Check(Telemetry.sender.Faulted && Telemetry.sender.Reason.Contains("enqueue"), "startup flush rejection explicitly faults recording");
        NewWorld(); Time.realtimeSinceStartup += 11; Time.frameCount++; RaidReviewCompat.Tick();
        Check(Telemetry.sender.Faulted && Telemetry.sender.Reason.Contains("timed out"), "startup unresolved timeout faults recording");
        NewWorld(); Resolve();
        Telemetry.SendSnapshot("LOOT", new TrackingLootItem { sessionId = RR.sessionId, profileId = "pmc" });
        Check(Telemetry.sender.Faulted && Telemetry.sender.Reason.Contains("physical source"), "ambiguous local source faults instead of assigning newest body");
        NewWorld(); Resolve();
        RAID_REVIEW_Player_OnItemAddedOrRemovedPatch.Capture(_body, new Item { Id = "pending-when-sender-fails" }, null, false);
        Telemetry.sender.Reject = true; BodyReplacementContext.Begin(_game, _body);
        var flushRefused = false; try { RaidReviewCompat.Prepare(_game, _body); } catch { flushRefused = true; }
        Check(flushRefused && Telemetry.sender.Faulted, "pending loot enqueue failure aborts preparation before replacement");
        RaidReviewCompat.AbortPreparation(); BodyReplacementContext.CancelPreparation();
        NewWorld(); Resolve(); End(ExitStatus.Left);
        Check(Last<TrackingPlayerDeadOrUnspawned>().status == PlayerStatus.Unspawned, "living leave is not fabricated death");
        NewWorld(); Resolve(); _body.HealthController.IsAlive = false; End(ExitStatus.Killed);
        Check(Last<TrackingPlayerDeadOrUnspawned>().status == PlayerStatus.Dead, "actual final death remains dead");
        foreach (var missing in new[] { "RestoreScope", "AfterSend", "AfterLoot" })
        {
            InstallPlugin(); NewWorld(); Resolve();
            var target = missing == "RestoreScope" ? AccessTools.Method(typeof(RecordingRoster), "Observe")
                : missing == "AfterSend" ? AccessTools.Method(typeof(Telemetry), "SendSnapshot")
                : AccessTools.Method(typeof(RAID_REVIEW_Player_OnItemAddedOrRemovedPatch), "Capture");
            new Harmony(Owner).Unpatch(target, AccessTools.Method(typeof(RaidReviewCompat), missing));
            BodyReplacementContext.Begin(_game, _body);
            var refused = false; try { RaidReviewCompat.Prepare(_game, _body); } catch { refused = true; }
            Check(refused && Telemetry.sender.Faulted, "missing required hook refuses replacement: " + missing);
            BodyReplacementContext.CancelPreparation();
        }
    }
    private static void BallisticPassthrough()
    {
        foreach (var mode in new[] { "ordinary", "inactive Arena", "Fika-like unavailable", "no driver" })
        {
            NewWorld(claim: false, available: mode == "ordinary" || mode == "inactive Arena"); Resolve();
            var collider = new Component { ThrowGameObject = true };
            var shot = new Shot { Player = new Bridge { iPlayer = _body }, HittedBallisticCollider = collider };
            var world = _world;
            RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.OriginalCalls = 0;
            RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.EventFailures = 0;
            RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.PatchPostFix(ref world, shot);
            Check(RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.OriginalCalls == 1
                && RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.EventFailures == 1
                && collider.GameObjectReads == 1 && !Telemetry.sender.Faulted,
                "ordinary ballistic exception remains upstream event-local: " + mode);
            foreach (var guard in new[] { "capture", "tracking" })
            {
                RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.CanCapture = guard != "capture";
                RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.BallisticsTracking = guard != "tracking";
                RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.OriginalCalls = 0; collider.GameObjectReads = 0;
                RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.PatchPostFix(ref world, shot);
                Check(RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.OriginalCalls == 1 && collider.GameObjectReads == 0
                    && !Telemetry.sender.Faulted, "unowned ballistic preserves upstream early guard: " + mode + "/" + guard);
            }
            RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.CanCapture = true;
            RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.BallisticsTracking = true;
            End(ExitStatus.Survived);
            RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.OriginalCalls = 0; collider.GameObjectReads = 0;
            RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.PatchPostFix(ref world, shot);
            Check(RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.OriginalCalls == 0 && collider.GameObjectReads == 0,
                "retired callback world fenced without collider access: " + mode);
        }
        NewWorld(); Resolve();
        var activeCollider = new Component { ThrowGameObject = true };
        var activeShot = new Shot { Player = new Bridge { iPlayer = _body }, HittedBallisticCollider = activeCollider };
        var activeWorld = _world;
        RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.OriginalCalls = 0;
        RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.PatchPostFix(ref activeWorld, activeShot);
        Check(RAID_REVIEW_ClientGameWorld_ShotDelegatePatch.OriginalCalls == 0 && activeCollider.GameObjectReads == 1
            && Telemetry.sender.Faulted && Telemetry.sender.Reason.Contains("stale collider"), "owned ballistic lookup fault contained and recording marked incomplete");
        Check(typeof(RaidReviewCompat).GetField("_actor", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == null,
            "owned ballistic lookup failure restores scope");
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference RetiredBody(RaidReviewLifeLedger ledger)
    { var old = new LocalPlayer(); ledger.Add(old); ledger.Add(new LocalPlayer()); return new WeakReference(old); }
    private static void Memory()
    {
        var ledger = new RaidReviewLifeLedger(); var weak = RetiredBody(ledger);
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        Check(!weak.IsAlive && ledger.Name("same-profile:redeploy:1") == " [life 1]", "retired physical bodies not retained by alias history");
    }
    public static int Main(string[] args)
    {
        try
        {
            var artifact = args[1]; Directory.CreateDirectory(artifact);
            NoModAndPartialFailure();
            var contract = RaidReviewContract.Resolve(typeof(RR), RaidReviewContract.PluginGuid, new System.Version(1, 10, 0));
            PublishedMetadata.Verify(args[0], contract, RaidReviewContract.PluginGuid, "1.10.0", "1.10.0.0", Check);
            Lifecycle(); BallisticPassthrough(); MultiLife(artifact); RecoveryAndFailures(); Memory();
            Console.WriteLine("RAID_REVIEW_RUNTIME_PASSED " + _checks + " assertions; actual Harmony + published metadata + production-linked adapters");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

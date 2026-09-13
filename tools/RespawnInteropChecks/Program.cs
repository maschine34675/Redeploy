using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Comfort.Common;
using EFT;
using UnityEngine;
using YourMod.Interop;

namespace RespawnInteropChecks;

/// <summary>
/// Scripted raids against the unmodified interop file (docs/RESPAWN-API.md, section 8.3).
/// Every Check names the guarantee it stands for; the run fails on the first broken one.
/// </summary>
internal static class Program
{
    private const string ApiTypeName = "Redeploy.Core.Api.RespawnApi, maschine-Redeploy.Core";
    private static int _checks;

    private static int Main()
    {
        try
        {
            TierEquivalenceOnTheSuccessPath();
            FailedReplacementReportsOldToOrphanAndBack();
            LateAttachReportsRaidStart();
            ReplacedAndPollInTheSameFrameAreOneEvent();
            PollHoldsWhileReplacingAndGivesUpAfterTheCap();
            NoFalseRaidStartAfterRaidEndedWithAPooledBody();
            PendingIsUnknownWhenCoreIsUnavailable();
            PartialBindingKeepsTheTracker();
            DestroyUnsubscribesEverything();
            AThrowingConsumerDoesNotBreakTheTracker();
            RuntimeBindingAgainstTheLiteralFacade();
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: " + ex.Message);
            return 1;
        }

        Console.WriteLine($"PASS: {_checks} respawn interop checks.");
        Console.WriteLine("RESPAWN_INTEROP_CHECKS_PASSED");
        return 0;
    }

    // ------------------------------------------------------------ scenarios

    private static void TierEquivalenceOnTheSuccessPath()
    {
        // The same scripted raid twice: bound to Core, and poll-only. The (previous, current)
        // pairs must agree; only raid-end timing and the reason after a destroyed swap body may differ.
        var withCore = ScriptedRaid(bound: true);
        var pollOnly = ScriptedRaid(bound: false);

        Check(withCore.Select(e => e.Pair).SequenceEqual(pollOnly.Select(e => e.Pair)),
            "tier equivalence: same (previous, current) sequence with and without Core\n  core: " + Describe(withCore) + "\n  poll: " + Describe(pollOnly));
        Check(withCore.Select(e => e.Reason).SequenceEqual(new[] { "raid-start", "death", "swap", "raid-end" }),
            "with Core the reasons are exact: " + Describe(withCore));
        Check(pollOnly.Select(e => e.Reason).SequenceEqual(new[] { "raid-start", "death", "swap", "raid-end" }),
            "poll-only infers death from a dead previous body and swap from a living one: " + Describe(pollOnly));
    }

    private static List<Event> ScriptedRaid(bool bound)
    {
        var world = NewWorld();
        var tracker = NewTracker(bound, out var events);
        Check(tracker.IsBound == bound, bound ? "tracker binds to the facade" : "tracker stays unbound without a facade");

        var first = new Player("P1");
        world.MainPlayer = first;
        Update(tracker);
        Check(events.Count == 1 && events[0].Reason == "raid-start" && events[0].Previous == null && ReferenceEquals(events[0].Current, first),
            "the first body arrives as raid-start with previous == null");

        // Death respawn: the corpse stays, a new body is wired.
        var second = new Player("P2");
        first.HealthController.IsAlive = false;
        if (bound)
        {
            MockRespawnApi.Replacing = true;
            world.MainPlayer = second;                 // mid-pipeline: MainPlayer already flipped
            Update(tracker);
            Check(events.Count == 1, "the poll holds while Core reports IsReplacing");
            MockRespawnApi.RaiseReplaced(first, second, "death");
            MockRespawnApi.Replacing = false;
            Update(tracker);
        }
        else
        {
            world.MainPlayer = second;
            Update(tracker);
        }
        Check(events.Count == 2 && ReferenceEquals(events[1].Previous, first) && ReferenceEquals(events[1].Current, second),
            "a death replacement is one event old -> new");

        // Swap: the old body is alive at takeover.
        var third = new Player("P3");
        if (bound)
        {
            MockRespawnApi.Replacing = true;
            world.MainPlayer = third;
            Update(tracker);
            MockRespawnApi.RaiseReplaced(second, third, "swap");
            MockRespawnApi.Replacing = false;
            Update(tracker);
        }
        else
        {
            world.MainPlayer = third;
            Update(tracker);
        }
        Check(events.Count == 3 && ReferenceEquals(events[2].Previous, second) && ReferenceEquals(events[2].Current, third),
            "a swap is one event old -> new");
        UnityEngine.Object.Destroy(second);                        // the requester disposes the swap body a frame later
        Update(tracker);
        Check(events.Count == 3, "destroying the retired swap body is no change");

        // Raid end.
        if (bound)
        {
            MockRespawnApi.RaiseRaidEnded();
            Check(events.Count == 4 && events[3].Reason == "raid-end" && events[3].Current == null, "Core reports raid-end at GameEnd");
            Singleton<GameWorld>.Instance = null;
            Update(tracker);
            Check(events.Count == 4, "the world going away after RaidEnded is no second raid-end");
        }
        else
        {
            Singleton<GameWorld>.Instance = null;
            Update(tracker);
            Check(events.Count == 4 && events[3].Reason == "raid-end" && events[3].Current == null, "without Core raid-end arrives when the world goes away");
        }

        Check(events.Zip(events.Skip(1), (a, b) => ReferenceEquals(a.Current, b.Previous)).All(x => x), "chain invariant: previous is always the last current");
        Destroy(tracker);
        return events;
    }

    private static void FailedReplacementReportsOldToOrphanAndBack()
    {
        var world = NewWorld();
        var tracker = NewTracker(bound: true, out var events);
        var first = new Player("P1");
        world.MainPlayer = first;
        Update(tracker);

        var failures = new List<string>();
        tracker.ReplacementFailed += (body, stage) => failures.Add(body?.Name + ":" + stage);

        first.HealthController.IsAlive = false;
        MockRespawnApi.Replacing = true;
        var orphan = new Player("orphan");
        world.MainPlayer = orphan;                     // rewire failed: MainPlayer points at the orphan
        Update(tracker);
        Check(events.Count == 1, "no poll event while the pipeline still runs");
        MockRespawnApi.RaiseReplacementFailed(first, "rewire");
        MockRespawnApi.Replacing = false;
        Check(failures.SequenceEqual(new[] { "P1:rewire" }), "ReplacementFailed is forwarded with body and stage");
        Update(tracker);
        Check(events.Count == 2 && ReferenceEquals(events[1].Previous, first) && ReferenceEquals(events[1].Current, orphan) && events[1].Reason == "death",
            "after a failed replacement the poll reports old -> orphan");

        world.MainPlayer = first;                      // the requester recovered the old body
        Update(tracker);
        Check(events.Count == 3 && ReferenceEquals(events[2].Previous, orphan) && ReferenceEquals(events[2].Current, first),
            "and orphan -> old when the requester recovers");
        Destroy(tracker);
    }

    private static void LateAttachReportsRaidStart()
    {
        var world = NewWorld();
        var first = new Player("P1");
        var second = new Player("P2");
        world.MainPlayer = first;
        var tracker = NewTracker(bound: true, out var events);   // attached mid-raid, no Update yet
        MockRespawnApi.RaiseReplaced(first, second, "death");
        Check(events.Count == 1 && events[0].Previous == null && ReferenceEquals(events[0].Current, second) && events[0].Reason == "raid-start",
            "a Replaced seen as the first observation is raid-start with previous == null");
        Destroy(tracker);
    }

    private static void ReplacedAndPollInTheSameFrameAreOneEvent()
    {
        var world = NewWorld();
        var tracker = NewTracker(bound: true, out var events);
        var first = new Player("P1");
        world.MainPlayer = first;
        Update(tracker);
        var second = new Player("P2");
        first.HealthController.IsAlive = false;
        world.MainPlayer = second;
        MockRespawnApi.RaiseReplaced(first, second, "death");
        Update(tracker);
        Update(tracker);
        Check(events.Count == 2, "Replaced and the poll in the same frame produce exactly one event");
        Destroy(tracker);
    }

    private static void PollHoldsWhileReplacingAndGivesUpAfterTheCap()
    {
        var world = NewWorld();
        var tracker = NewTracker(bound: true, out var events);
        var first = new Player("P1");
        world.MainPlayer = first;
        Update(tracker);

        var second = new Player("P2");
        MockRespawnApi.Replacing = true;
        Time.realtimeSinceStartup = 100f;
        world.MainPlayer = second;                     // flips three frames before Replaced
        Update(tracker); Update(tracker); Update(tracker);
        Check(events.Count == 1, "MainPlayer flipping while IsReplacing is true produces no poll event");
        MockRespawnApi.RaiseReplaced(first, second, "swap");
        Check(events.Count == 2, "Replaced then reports the wired body once");

        // A pipeline that never reports back must not hold the poll forever.
        var third = new Player("P3");
        world.MainPlayer = third;
        Update(tracker);
        Check(events.Count == 2, "still held while IsReplacing");
        Time.realtimeSinceStartup = 100f + 36f;
        Update(tracker);
        Check(events.Count == 3 && ReferenceEquals(events[2].Current, third), "after 35 s of IsReplacing the poll reports the change anyway");
        MockRespawnApi.Replacing = false;
        Time.realtimeSinceStartup = 0f;
        Destroy(tracker);
    }

    private static void NoFalseRaidStartAfterRaidEndedWithAPooledBody()
    {
        var world = NewWorld();
        var tracker = NewTracker(bound: true, out var events);
        var first = new Player("P1");
        world.MainPlayer = first;
        Update(tracker);
        MockRespawnApi.RaiseRaidEnded();
        Check(events.Count == 2 && events[1].Reason == "raid-end", "raid-end reported");
        world.MainPlayer = new Player("pooled");       // teardown parks a pool body in MainPlayer
        Update(tracker);
        Check(events.Count == 2, "no raid-start for a pooled body after RaidEnded of the same world");
        Destroy(tracker);
    }

    private static void PendingIsUnknownWhenCoreIsUnavailable()
    {
        NewWorld();
        var tracker = NewTracker(bound: true, out _);
        MockRespawnApi.PendingAnswer = true;
        Check(tracker.IsRespawnPending(null) == true, "bound and available: the facade's answer comes through");
        MockRespawnApi.Available = false;
        Check(tracker.IsRespawnPending(null) == null, "bound but unavailable (Fika): IsRespawnPending is null, not false");
        Check(!tracker.IsRespawnAvailable, "IsRespawnAvailable mirrors the facade");
        MockRespawnApi.Available = true;
        Destroy(tracker);

        var unbound = NewTracker(bound: false, out _);
        Check(unbound.IsRespawnPending(null) == null, "unbound: IsRespawnPending is null");
        Destroy(unbound);
    }

    private static void PartialBindingKeepsTheTracker()
    {
        MockRespawnApiDrifted.Reset();
        var world = NewWorld();
        LocalBodyTracker.ResolveType = name => name == ApiTypeName ? typeof(MockRespawnApiDrifted) : null;
        var tracker = Attach(out var events);
        Check(tracker.IsBound, "a facade missing ReplacementFailed and with a drifted DeathClaimed still binds");
        Check(MockRespawnApiDrifted.Subscribers("Replaced") == 1 && MockRespawnApiDrifted.Subscribers("DeathClaimed") == 0,
            "Replaced is subscribed, the drifted event is skipped instead of throwing");
        var first = new Player("P1");
        var second = new Player("P2");
        world.MainPlayer = first;
        Update(tracker);
        MockRespawnApiDrifted.RaiseReplaced(first, second, "death");
        Check(events.Count == 2 && ReferenceEquals(events[1].Current, second), "Replaced still flows through a partial binding");
        Destroy(tracker);
        Check(MockRespawnApiDrifted.Subscribers("Replaced") == 0, "OnDestroy unsubscribes the partial binding");
    }

    private static void DestroyUnsubscribesEverything()
    {
        NewWorld();
        var tracker = NewTracker(bound: true, out _);
        Check(MockRespawnApi.Subscribers("Replaced") == 1 && MockRespawnApi.Subscribers("DeathClaimed") == 1
              && MockRespawnApi.Subscribers("ReplacementFailed") == 1 && MockRespawnApi.Subscribers("RaidEnded") == 1,
            "exactly one subscriber per facade event while bound");
        Destroy(tracker);
        Check(MockRespawnApi.Subscribers("Replaced") == 0 && MockRespawnApi.Subscribers("DeathClaimed") == 0
              && MockRespawnApi.Subscribers("ReplacementFailed") == 0 && MockRespawnApi.Subscribers("RaidEnded") == 0,
            "after OnDestroy every facade event has zero subscribers (the same delegate instance was removed)");
        Check(LocalBodyTracker.Instance == null, "Instance is cleared by the tracker that owned it");
    }

    private static void AThrowingConsumerDoesNotBreakTheTracker()
    {
        var world = NewWorld();
        var logged = new List<string>();
        var tracker = NewTracker(bound: true, out var events);
        tracker.Log = logged.Add;
        tracker.BodyChanged += (previous, current, reason) => throw new InvalidOperationException("consumer bug");
        var reached = 0;
        tracker.BodyChanged += (previous, current, reason) => reached++;
        world.MainPlayer = new Player("P1");
        Update(tracker);
        Check(events.Count == 1 && reached == 1, "a throwing BodyChanged handler does not stop the handlers after it");
        Check(logged.Any(line => line.Contains("BodyChanged subscriber failed") && line.Contains("consumer bug")), "the failure is logged with the exception");
        MockRespawnApi.RaiseDeathClaimed(world.MainPlayer, "Redeploy");
        Check(tracker.Current == world.MainPlayer, "the tracker keeps working afterwards");
        Destroy(tracker);
    }

    private static void RuntimeBindingAgainstTheLiteralFacade()
    {
        // The binding test of section 8.2 against the mock that carries the literal v1 surface:
        // every reflection lookup in the interop file resolves, with exactly one subscriber each.
        NewWorld();
        var tracker = NewTracker(bound: true, out _);
        var claimed = new List<string>();
        var ended = 0;
        tracker.DeathClaimed += (body, driver) => claimed.Add(body.Name + "<-" + driver);
        tracker.RaidEnded += () => ended++;
        var body = new Player("P1");
        MockRespawnApi.RaiseDeathClaimed(body, "ArenaMode");
        MockRespawnApi.RaiseRaidEnded();
        Check(claimed.SequenceEqual(new[] { "P1<-ArenaMode" }) && ended == 1, "DeathClaimed and RaidEnded are forwarded through the binding");
        Destroy(tracker);
        Check(MockRespawnApi.Subscribers("Replaced") == 0, "zero subscribers after OnDestroy");
    }

    // ------------------------------------------------------------ plumbing

    private sealed class Event
    {
        public Player Previous;
        public Player Current;
        public string Reason;
        public (string, string) Pair => (Previous?.Name, Current?.Name);
    }

    private static string Describe(List<Event> events)
        => string.Join(", ", events.Select(e => $"({e.Previous?.Name ?? "null"}->{e.Current?.Name ?? "null"}:{e.Reason})"));

    private static GameWorld NewWorld()
    {
        MockRespawnApi.Reset();
        Time.realtimeSinceStartup = 0f;
        var world = new GameWorld();
        Singleton<GameWorld>.Instance = world;
        return world;
    }

    private static LocalBodyTracker NewTracker(bool bound, out List<Event> events)
    {
        LocalBodyTracker.ResolveType = name => bound && name == ApiTypeName ? typeof(MockRespawnApi) : null;
        return Attach(out events);
    }

    private static LocalBodyTracker Attach(out List<Event> events)
    {
        var host = new GameObject();
        var tracker = host.AddComponent<LocalBodyTracker>();
        var list = new List<Event>();
        tracker.BodyChanged += (previous, current, reason) => list.Add(new Event { Previous = previous, Current = current, Reason = reason });
        events = list;
        Invoke(tracker, "Awake");
        return tracker;
    }

    private static void Update(LocalBodyTracker tracker) => Invoke(tracker, "Update");

    private static void Destroy(LocalBodyTracker tracker)
    {
        Invoke(tracker, "OnDestroy");
        UnityEngine.Object.Destroy(tracker);
    }

    private static void Invoke(LocalBodyTracker tracker, string unityMessage)
    {
        var method = typeof(LocalBodyTracker).GetMethod(unityMessage, BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) throw new InvalidOperationException($"interop file has no {unityMessage}()");
        method.Invoke(tracker, null);
    }

    private static void Check(bool condition, string what)
    {
        _checks++;
        if (!condition) throw new Exception(what);
    }
}

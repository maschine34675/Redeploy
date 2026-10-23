using System.Reflection;
using Redeploy.Core.Compat;
using Redeploy.Core;
using BepInEx.Bootstrap;
using Comfort.Common;
using EFT;
using EFT.HealthSystem;
using HarmonyLib;
using Operation = EFT.Player.MedsController.MedsInHandsOperation;

public static class Program
{
    private static int _checks;
    private static bool _foreignCancel, _foreignPermit;
    private static int _foreignStarts, _foreignCancels;
    private static void Need(bool condition, string label)
    { ++_checks; if (!condition) throw new InvalidOperationException("ASSERT: " + label); }

    // Sentinels make foreign execution observable. They intentionally are not a
    // medical algorithm: an admitted foreign end always vetoes native work.
    public static bool ForeignEnd(Operation op, IHealthEffect effect)
    { op.ForeignRuns++; if (_foreignCancel) op.Callbacks++; return _foreignPermit; }
    public static void ForeignStart(Player body) { _foreignStarts++; if (body.IsYourPlayer) _foreignCancel = false; }
    public static void ForeignCancel(Player body) { _foreignCancels++; if (body.IsYourPlayer) _foreignCancel = true; }

    private static bool RunPrefix(MethodInfo target, object value)
    {
        bool runForeign = true, result = false;
        foreach (var patch in Harmony.GetPatchInfo(target)?.Prefixes.ToArray() ?? Array.Empty<Patch>())
        {
            object[] args = target.ReturnType == typeof(bool) ? new[] { value, (object)result } : new[] { value, target };
            if (!(bool)patch.PatchMethod.Invoke(null, args)) runForeign = false;
            if (target.ReturnType == typeof(bool)) result = (bool)args[1];
        }
        if (runForeign)
        {
            var answer = target.Invoke(null, target.ReturnType == typeof(bool) ? new[] { value, new OtherEffect() } : new[] { value });
            if (target.ReturnType == typeof(bool)) result = (bool)answer;
        }
        return result;
    }

    private static Operation End(Fixture f, Player body, IHealthEffect effect)
    {
        var op = new Operation { _controller = new() { _player = body } };
        if (RunPrefix(f.Prefixes[0], op)) op.OnEffectRemoved(effect);
        return op;
    }

    private static void EndCheck(Fixture f, Player body, bool bypass, string label)
    {
        _foreignCancel = true;
        var op = End(f, body, new MedEffect());
        Need(op.ForeignRuns == (bypass ? 0 : 1), label + " foreign gate");
        Need(op.NativeRuns == (bypass ? 1 : 0), label + " native fallback");
        Need(op.Callbacks == 1, label + " single completion callback");
        op = End(f, body, new OtherEffect());
        Need(op.NativeRuns == (bypass ? 1 : 0) && op.Callbacks == (bypass ? 0 : 1), label + " non-med effect");
        Need(_foreignCancel, label + " foreign global flag untouched");
    }

    private static void Reset()
    {
        Harmony.FailUnpatch = false;
        ContinuousHealingCompat.Shutdown();
        Harmony.Bound.Clear(); Chainloader.PluginInfos.Clear();
        Harmony.DropReceipt = false; Harmony.FailPatchAt = Harmony.PatchCalls = 0;
        BodyReplacementContext.Reset(); BodyReplacementCompatibility.AdditionalScope = null;
        Singleton<AbstractGame>.Instance = null; Singleton<IBotGame>.Instance = null; Singleton<GameWorld>.Instance = null;
        CoreLog.Warnings.Clear(); CoreLog.Debug.Clear();
        _foreignStarts = _foreignCancels = 0; _foreignCancel = _foreignPermit = false;
    }

    private static LocalGame World(bool active)
    {
        var game = new LocalGame { GameWorld = new() };
        game.LocalPlayer = new() { GameWorld = game.GameWorld, Profile = game.Profile, IsYourPlayer = true };
        game.GameWorld.MainPlayer = game.LocalPlayer;
        Singleton<AbstractGame>.Instance = game; Singleton<GameWorld>.Instance = game.GameWorld; Singleton<IBotGame>.Instance = game;
        BodyReplacementCompatibility.AdditionalScope = active ? body =>
            ReferenceEquals(body.GameWorld, game.GameWorld)
            && ReferenceEquals(Singleton<AbstractGame>.Instance, game)
            && ReferenceEquals(Singleton<IBotGame>.Instance, game)
            && ReferenceEquals(Singleton<GameWorld>.Instance, game.GameWorld) : null;
        return game;
    }

    private static LocalPlayer BeginCore(LocalGame game)
    {
        var old = game.LocalPlayer;
        BodyReplacementContext.Begin(game, old);
        ContinuousHealingCompat.Prepare(game, old);
        return old;
    }

    private static LocalPlayer Candidate(LocalGame game)
    {
        var body = new LocalPlayer { GameWorld = game.GameWorld, Profile = game.Profile, IsYourPlayer = true };
        game.GameWorld.MainPlayer = body;
        return body;
    }

    private static void Publish(LocalGame game, LocalPlayer body)
    {
        Need(BodyReplacementContext.TryOwnCandidate(body), "candidate Core ownership");
        game.LocalPlayer = body;
        BodyReplacementContext.EndAttempt();
    }

    private static void ScopeCases()
    {
        Reset(); var f = FixtureFactory.Make(); f.Register(); ContinuousHealingCompat.Initialize();
        Need(ContinuousHealingCompat.GetRejectionReason() == null, "reviewed API admitted");
        var ordinary = World(false);
        EndCheck(f, ordinary.GameWorld.MainPlayer, false, "ordinary local PMC or Scav");
        EndCheck(f, new() { GameWorld = ordinary.GameWorld }, false, "ordinary bot");
        _foreignPermit = true; _foreignCancel = false;
        var permitted = End(f, ordinary.LocalPlayer, new MedEffect());
        Need(permitted.ForeignRuns == 1 && permitted.NativeRuns == 1 && permitted.Callbacks == 1,
            "unowned original permit result preserved");
        _foreignPermit = false;
        var game = World(false); var local = BeginCore(game);
        EndCheck(f, local, false, "current standalone local body");
        _foreignPermit = true; _foreignCancel = false;
        permitted = End(f, local, new MedEffect());
        Need(permitted.ForeignRuns == 1 && permitted.NativeRuns == 1 && permitted.Callbacks == 1,
            "owned current-local permit result preserved");
        _foreignPermit = false;
        var bot = new Player { GameWorld = game.GameWorld };
        EndCheck(f, bot, false, "standalone bot during replacement");
        EndCheck(f, new() { GameWorld = ordinary.GameWorld }, false, "foreign body world");
        EndCheck(f, new(), false, "unowned body");
        EndCheck(f, new LocalPlayer { GameWorld = game.GameWorld, Profile = game.Profile, IsYourPlayer = true }, false,
            "same-profile non-main impostor");

        // Preserve current-player cancellation and resets in the actual prefix seam.
        _foreignCancel = true; RunPrefix(f.Prefixes[1], local); Need(!_foreignCancel, "live local start reaches foreign reset");
        RunPrefix(f.Prefixes[2], local); Need(_foreignCancel, "live local cancel reaches foreign state");
        int starts = _foreignStarts, cancels = _foreignCancels;
        RunPrefix(f.Prefixes[1], bot); RunPrefix(f.Prefixes[2], bot);
        Need(_foreignStarts == starts + 1 && _foreignCancels == cancels + 1, "ordinary bot start and cancel upstream");

        // Native MainPlayer remains the dead local until replacement publication.
        local.HealthController.Alive = false;
        EndCheck(f, local, true, "dead old MainPlayer");
        _foreignCancel = false; RunPrefix(f.Prefixes[2], local);
        Need(!_foreignCancel, "retired body cannot cancel replacement healing");
        _foreignCancel = true; RunPrefix(f.Prefixes[1], local);
        Need(_foreignCancel, "retired body cannot reset replacement healing");
        var replacement = Candidate(game);
        EndCheck(f, replacement, false, "replacement local continues foreign healing");
        Publish(game, replacement);
        local.HealthController.Alive = true; EndCheck(f, local, true, "retired living same-profile body");
        EndCheck(f, bot, false, "ordinary bot after standalone replacement");

        // Repeat live kit replacement: no dead flag is necessary for retirement.
        Need(ReferenceEquals(BeginCore(game), replacement), "repeat uses the published local body");
        var repeated = Candidate(game);
        EndCheck(f, repeated, false, "repeat replacement live local");
        Publish(game, repeated);
        EndCheck(f, replacement, true, "repeat retired live body");
        _foreignCancel = false; RunPrefix(f.Prefixes[2], replacement);
        Need(!_foreignCancel, "repeat retired cancel cannot cancel current healing");

        // Failed attempt before publication retains the live original unchanged.
        BeginCore(game); BodyReplacementContext.EndAttempt();
        EndCheck(f, repeated, false, "failed replacement keeps current local behavior");
        EndCheck(f, bot, false, "failed replacement leaves ordinary bots alone");
        // A body without any healing events is still in Core's weak tombstones.
        BeginCore(game); var silent = Candidate(game); Publish(game, silent);
        BodyReplacementContext.Reset();
        var next = World(false);
        EndCheck(f, silent, true, "silent prior local callback during ordinary reentry");
        EndCheck(f, repeated, true, "previous local callback during ordinary reentry");
        EndCheck(f, next.GameWorld.MainPlayer, false, "next ordinary local");
        EndCheck(f, new() { GameWorld = next.GameWorld }, false, "next ordinary bot");
        repeated.GameWorld = next.GameWorld;
        EndCheck(f, repeated, false, "reused Core body loses old-world ownership");

        // Additional bodies are controlled only by the explicit consumer scope.
        var arena = World(true); var arenaLocal = arena.LocalPlayer;
        var arenaBot = new Player { GameWorld = arena.GameWorld };
        EndCheck(f, arenaBot, true, "Arena opt-in bot");
        EndCheck(f, arenaLocal, false, "Arena opt-in current local body");
        EndCheck(f, new() { GameWorld = ordinary.GameWorld }, false, "foreign body world under Arena scope");
        starts = _foreignStarts; cancels = _foreignCancels;
        RunPrefix(f.Prefixes[1], arenaBot); RunPrefix(f.Prefixes[2], arenaBot);
        Need(_foreignStarts == starts && _foreignCancels == cancels, "Arena bot start and cancel guarded");

        // Capture through start alone; no prior end/cancel must be necessary.
        var startOnly = new Player { GameWorld = arena.GameWorld };
        RunPrefix(f.Prefixes[1], startOnly);
        BodyReplacementCompatibility.AdditionalScope = body => throw new InvalidOperationException("consumer is shutting down");
        EndCheck(f, startOnly, true, "leased consumer body survives consumer fault");
        BodyReplacementCompatibility.AdditionalScope = null;
        EndCheck(f, startOnly, true, "delayed callback after session cleanup");
        next = World(false);
        EndCheck(f, startOnly, true, "previous bot callback during ordinary reentry");
        startOnly.GameWorld = next.GameWorld;
        EndCheck(f, startOnly, false, "reused body loses old-world ownership");

        // A consumer may grant only its exact active world, never a stale pair.
        var correct = World(true);
        Singleton<GameWorld>.Instance = new();
        EndCheck(f, new() { GameWorld = Singleton<GameWorld>.Instance }, false, "mismatched game world");
        Singleton<GameWorld>.Instance = correct.GameWorld;
        Singleton<IBotGame>.Instance = new LocalGame();
        EndCheck(f, new() { GameWorld = correct.GameWorld }, false, "mismatched bot game");
        Singleton<IBotGame>.Instance = correct;
        EndCheck(f, new() { GameWorld = correct.GameWorld, IsYourPlayer = true }, true, "foreign IsYourPlayer body is not local authority");

        var scope = new ContinuousHealingScope(); var bodyKey = new object(); var worldKey = new object();
        Need(!scope.Owns(bodyKey, worldKey, new object(), true), "scope grant rejects unrelated world");
        Need(scope.Owns(bodyKey, worldKey, worldKey, true), "weak body lease acquired");
        scope.Clear(); Need(!scope.Owns(bodyKey, worldKey, worldKey, false), "scope cleanup releases ownership");
        ContinuousHealingCompat.Shutdown(); EndCheck(f, bot, false, "shutdown removes guard");
    }

    private static void DiagnosticCases()
    {
        Reset(); var f = FixtureFactory.Make(); f.Register(); ContinuousHealingCompat.Initialize();
        var game = World(true); var local = game.GameWorld.MainPlayer; var bot = new Player { GameWorld = game.GameWorld };
        for (int i = 0; i < 12; i++)
        {
            End(f, local, new MedEffect()); End(f, bot, new MedEffect());
            foreach (int seam in new[] { 1, 2 }) { RunPrefix(f.Prefixes[seam], local); RunPrefix(f.Prefixes[seam], bot); }
        }
        Need(CoreLog.Debug.Count(m => m.Contains("current local body")) == 3, "current-local reports bounded");
        Need(CoreLog.Debug.Count(m => m.Contains("consumer-owned bot")) == 3, "guard reports bounded");
        local.HealthController.Alive = false;
        End(f, local, new MedEffect()); RunPrefix(f.Prefixes[1], local); RunPrefix(f.Prefixes[2], local);
        Need(CoreLog.Debug.Count(m => m.Contains("retired local body")) == 3, "retirement reports have independent bound");
        Need(CoreLog.Warnings.Count == 0, "normal protection does not warn");
    }

    private static void CancelledPreparationCases()
    {
        Reset(); var f = FixtureFactory.Make(); f.Register(); ContinuousHealingCompat.Initialize();
        var game = World(false); var original = BeginCore(game);
        BodyReplacementContext.CancelPreparation();
        World(false);
        EndCheck(f, original, false, "cancelled initial prepare releases provisional body");
        Need(!BodyReplacementContext.OwnsBody(original), "cancelled initial prepare has no Core tombstone");

        game = World(false); BeginCore(game);
        var replacement = Candidate(game); Publish(game, replacement);
        BeginCore(game); BodyReplacementContext.CancelPreparation();
        Need(BodyReplacementContext.OwnsBody(replacement), "cancelled repeat keeps pre-existing Core tombstone");
        EndCheck(f, replacement, false, "cancelled repeat preserves current local healing");
        World(false);
        EndCheck(f, replacement, true, "cancelled repeat keeps delayed prior-world guard");
    }

    private static void ContractCases()
    {
        Reset(); ContinuousHealingCompat.Initialize();
        Need(ContinuousHealingCompat.GetRejectionReason() == null, "optional absence");
        ContinuousHealingCompat.Prepare(null, null); ContinuousHealingCompat.Tick();
        Need(Harmony.Bound.Count == 0, "absent mod does not install hooks or mutate context");
        var f = FixtureFactory.Make(); f.Register();
        Need(ContinuousHealingCompat.GetRejectionReason() == null, "late discovery");
        bool refused = false;
        try { ContinuousHealingCompat.Prepare(null, null); }
        catch (InvalidOperationException) { refused = true; }
        Need(refused, "present mod requires exact Core preparation context");
        var unowned = World(false);
        BodyReplacementCompatibility.AdditionalScope = body => throw new InvalidOperationException("unowned scope fault");
        EndCheck(f, unowned.LocalPlayer, false, "unowned scope fault stays upstream");
        Need(ContinuousHealingCompat.GetRejectionReason() == null, "unowned scope fault does not poison replacements");
        BodyReplacementCompatibility.AdditionalScope = null;
        ContinuousHealingCompat.GetRejectionReason();
        Need(f.Prefixes.All(p => Harmony.GetPatchInfo(p).Prefixes.Count == 1), "idempotent own receipts");
        foreach (var version in new[] { "1.6.4", "2.0.0" })
        {
            f = FixtureFactory.Make(version); f.Register();
            Need(ContinuousHealingCompat.GetRejectionReason() == null, "newer checked API admitted");
            var game = World(true); EndCheck(f, new() { GameWorld = game.GameWorld }, true, "newer API protects bot");
        }
        Need(CoreLog.Warnings.Count == 1, "newer advisory once");
        foreach (var bad in new[] {
            FixtureFactory.Make("1.6.2"), FixtureFactory.Make(assemblyName: "OtherAssembly"),
            FixtureFactory.Make(assemblyVersion: "1.6.4"), FixtureFactory.Make(metadataVersion: "1.6.4"),
            FixtureFactory.Make(guid: "com.lacyway.CH"), FixtureFactory.Make(defect: "identity"),
            FixtureFactory.Make(defect: "attribute"), FixtureFactory.Make(defect: "result"),
            FixtureFactory.Make(defect: "instance"), FixtureFactory.Make(defect: "name") })
        {
            bad.Register(); Need(ContinuousHealingCompat.GetRejectionReason() != null, "changed API rejects Arena");
            Need(bad.Prefixes.All(p => Harmony.GetPatchInfo(p)?.Prefixes.Count is null or 0), "bad API leaves foreign hooks alone");
        }
        f = FixtureFactory.Make(); f.Register();
        var nativeEnd = typeof(Operation).GetMethod("OnEffectRemoved");
        Harmony.GetPatchInfo(nativeEnd).Prefixes.Clear();
        Need(ContinuousHealingCompat.GetRejectionReason() != null, "missing foreign native receipt rejects Arena");
        f.Register(); Need(ContinuousHealingCompat.GetRejectionReason() == null, "valid replacement repairs binding");
        Harmony.GetPatchInfo(f.Prefixes[1]).Prefixes.Clear();
        Need(ContinuousHealingCompat.GetRejectionReason() != null, "lost own receipt rejects Arena");
        f.Register(); Harmony.GetPatchInfo(nativeEnd).Postfixes.Add(new() { owner = "foreign", PatchMethod = f.Prefixes[0] });
        Need(ContinuousHealingCompat.GetRejectionReason() != null, "new foreign native hook rejects Arena");
        foreach (int failedAt in new[] { 1, 2, 3 })
        {
            Reset(); f = FixtureFactory.Make(); f.Register(); Harmony.FailPatchAt = failedAt;
            Need(ContinuousHealingCompat.GetRejectionReason() != null, "partial hook installation rejects Arena");
            Need(f.Prefixes.All(p => Harmony.GetPatchInfo(p)?.Prefixes.Count is null or 0), "partial hook cleanup");
        }
        Reset(); f = FixtureFactory.Make(); f.Register(); Harmony.DropReceipt = true;
        Need(ContinuousHealingCompat.GetRejectionReason() != null, "missing installation receipt rejects Arena");
        Reset(); f = FixtureFactory.Make(); f.Register(); ContinuousHealingCompat.Initialize();
        var active = World(true); var bot = new Player { GameWorld = active.GameWorld, HealthController = new() { ThrowRead = true } };
        EndCheck(f, bot, true, "proved body survives health-read fault");
        Need(ContinuousHealingCompat.GetRejectionReason() != null, "runtime fault rejects later Arena");
        var ordinary = World(false); EndCheck(f, ordinary.GameWorld.MainPlayer, false, "ordinary survives runtime failure");
        Harmony.FailUnpatch = true; ContinuousHealingCompat.Shutdown();
        EndCheck(f, bot, false, "failed unpatch revokes stale guard authority");
        Harmony.FailUnpatch = false; Chainloader.PluginInfos.Clear();
        Need(ContinuousHealingCompat.GetRejectionReason() == null, "optional removal recovers admission");
    }

    public static int Main(string[] args)
    {
        try
        {
            ScopeCases(); CancelledPreparationCases(); DiagnosticCases(); ContractCases();
            if (args.Length != 1) throw new InvalidOperationException("Supply the separately downloaded published DLL for metadata checks.");
            PublishedMetadata.Verify(args[0], Need);
            Console.WriteLine($"CONTINUOUS_HEALING_CHECKS_PASSED ({_checks})");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
